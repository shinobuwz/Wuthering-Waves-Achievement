using System.Windows;
using System.Windows.Threading;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public sealed class RotationRuntimeCoordinator : IDisposable
{
    private static readonly string[] GameProcessNames = ["Client-Win64-Shipping.exe", "Wuthering Waves.exe"];
    private readonly Window _shell;
    private readonly Action<string, bool> _restore;
    private readonly IRotationGameMonitor _monitor;
    private readonly Func<IRotationInputSource> _inputSourceFactory;
    private readonly DispatcherTimer _timer;
    private IRotationInputSource? _inputSource;
    private RotationOverlayWindow? _overlay;
    private RotationGameWindow? _gameWindow;
    private RotationRunSession? _session;
    private RotationProfile? _profile;
    private RotationSettings? _settings;
    private int _invalidBoundsSamples;
    private long _generation;
    private bool _stopping;

    public RotationRuntimeCoordinator(
        Window shell,
        Action<string, bool> restore,
        IRotationGameMonitor? monitor = null,
        Func<IRotationInputSource>? inputSourceFactory = null)
    {
        _shell = shell;
        _restore = restore;
        _monitor = monitor ?? new WindowsRotationGameMonitor();
        _inputSourceFactory = inputSourceFactory ?? (() => new WindowsRotationInputSource());
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += Monitor_OnTick;
    }

    public bool IsRunning => _session is not null;

    public async Task StartAsync(RotationProfile profile, RotationSettings settings, bool stopHotKeyAvailable)
    {
        if (IsRunning) return;
        if (!stopHotKeyAvailable) throw new InvalidOperationException("Ctrl+Shift+F11 安全停止快捷键不可用，禁止启动连招。");
        var profileValidation = RotationProfileValidator.Validate(profile);
        var bindingValidation = RotationBindingValidator.Validate(profile, settings.Bindings);
        if (!profileValidation.IsValid || !bindingValidation.IsValid)
            throw new InvalidOperationException(string.Join("；", profileValidation.Errors.Concat(bindingValidation.Errors).Select(issue => issue.Message)));

        RotationOverlayWindow? overlay = null;
        IRotationInputSource? input = null;
        try
        {
            var game = await _monitor.TryFindAsync(GameProcessNames, 800, 600) ?? throw new InvalidOperationException("未找到可运行连招的《鸣潮》窗口。请确认游戏可见且未最小化。");
            var state = _monitor.ReadState(game);
            if (state.Bounds is null) throw new InvalidOperationException("无法取得《鸣潮》客户区位置。");
            _monitor.TryActivate(game);
            var focused = false;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                state = _monitor.ReadState(game);
                if (state.IsForeground && state.Bounds is not null) { focused = true; break; }
            }
            if (!focused) throw new InvalidOperationException("无法确认《鸣潮》已获得前台焦点，连招未启动。");

            var session = new RotationRunSession(profile, settings.Bindings, settings.HeavyThreshold);
            session.Start();
            overlay = new RotationOverlayWindow { Owner = null };
            overlay.ApplySnapshot(session.Snapshot, profile, settings.Bindings);
            overlay.Show();
            overlay.PositionWithin(state.Bounds!);
            input = _inputSourceFactory();
            var generation = Interlocked.Increment(ref _generation);
            input.InputObserved += (_, observed) => OnInputObserved(generation, observed);
            input.Start();

            _gameWindow = game;
            _profile = profile;
            _settings = settings;
            _session = session;
            _overlay = overlay;
            _inputSource = input;
            _invalidBoundsSamples = 0;
            _shell.Hide();
            _timer.Start();
            overlay = null;
            input = null;
        }
        catch
        {
            if (input is not null)
            {
                input.Stop(TimeSpan.FromSeconds(3));
                input.Dispose();
            }
            overlay?.Close();
            if (!_shell.IsVisible) _shell.Show();
            throw;
        }
    }

    public void Stop(RotationStopReason reason = RotationStopReason.UserRequested, string? message = null, bool isError = false, bool restore = true)
    {
        if (_stopping) return;
        _stopping = true;
        try
        {
            Interlocked.Increment(ref _generation);
            _timer.Stop();
            var input = _inputSource;
            _inputSource = null;
            var releaseConfirmed = input is null || input.Stop(TimeSpan.FromSeconds(3));
            input?.Dispose();
            _session?.Stop(reason);
            _session = null;
            _gameWindow = null;
            _profile = null;
            _settings = null;
            if (_overlay is not null)
            {
                _overlay.Close();
                _overlay = null;
            }
            if (restore)
            {
                var restoreMessage = releaseConfirmed
                    ? message ?? StopMessage(reason)
                    : "连招已停止，但无法确认键鼠 Hook 线程已完全退出；请关闭应用后重试。";
                _restore(restoreMessage, isError || !releaseConfirmed);
            }
        }
        finally { _stopping = false; }
    }

    private void OnInputObserved(long generation, RotationObservedInput observed)
    {
        _shell.Dispatcher.BeginInvoke(() =>
        {
            if (generation != _generation || _session is null || _profile is null || _settings is null || _gameWindow is null) return;
            var windowState = _monitor.ReadState(_gameWindow);
            if (!windowState.Exists)
            {
                Stop(RotationStopReason.GameLost, "游戏窗口已关闭，连招运行已安全停止。", true);
                return;
            }
            if (!RotationRuntimeInputGate.CanAccept(observed, _gameWindow, windowState))
            {
                if (!windowState.IsForeground || windowState.Bounds is null)
                {
                    _session.SetGameForeground(false);
                    _overlay?.Hide();
                }
                return;
            }
            _session.SetGameForeground(true);
            var result = _session.Receive(observed.Input);
            _overlay?.ApplySnapshot(result.Snapshot, _profile, _settings.Bindings);
            if (result.Snapshot.StopReason == RotationStopReason.Reselect)
                Stop(RotationStopReason.Reselect);
        });
    }

    private void Monitor_OnTick(object? sender, EventArgs e)
    {
        if (_gameWindow is null || _session is null || _overlay is null || _profile is null || _settings is null) return;
        var state = _monitor.ReadState(_gameWindow);
        if (!state.Exists)
        {
            Stop(RotationStopReason.GameLost, "游戏窗口已关闭，连招运行已安全停止。", true);
            return;
        }
        if (!state.IsVisible || state.IsMinimized || state.Bounds is null)
        {
            _session.SetGameForeground(false);
            _overlay.Hide();
            if (++_invalidBoundsSamples >= 20)
                Stop(RotationStopReason.GameLost, "持续无法取得游戏客户区，连招运行已安全停止。", true);
            return;
        }
        _invalidBoundsSamples = 0;
        if (!state.IsForeground)
        {
            _session.SetGameForeground(false);
            _overlay.Hide();
            return;
        }
        _session.SetGameForeground(true);
        _overlay.ApplySnapshot(_session.Snapshot, _profile, _settings.Bindings);
        if (!_overlay.IsVisible) _overlay.Show();
        _overlay.PositionWithin(state.Bounds);
    }

    private static string StopMessage(RotationStopReason reason) => reason switch
    {
        RotationStopReason.Reselect => "连招已停止，请重新选择流程。",
        RotationStopReason.ApplicationShutdown => "应用关闭，连招监听已释放。",
        RotationStopReason.GameLost => "游戏窗口失效，连招已停止。",
        _ => "连招已安全停止，键鼠监听已释放。"
    };

    public void Dispose() => Stop(RotationStopReason.ApplicationShutdown, restore: false);
}
