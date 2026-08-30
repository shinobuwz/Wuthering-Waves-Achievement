namespace Wuwa.Core;

public enum RotationRunStatus { Idle, AwaitingStart, Running, Paused, Finished, Stopped }
public enum RotationRunPhase { None, Start, Opener, Loop, Finished }
public enum RotationStopReason { None, UserRequested, Reselect, GameLost, ApplicationShutdown, InitializationFailed }

public sealed record RotationInputEvent(RotationPhysicalInput Input, bool IsDown);

public sealed record RotationPreviewItem(
    bool IsStart,
    RotationActionKind? Action,
    string Description,
    int CharacterSlot,
    RotationBindingAction? BindingAction);

public sealed record RotationRunSnapshot(
    RotationRunStatus Status,
    RotationRunPhase Phase,
    int CurrentCharacterSlot,
    IReadOnlyList<RotationPreviewItem> Preview,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    RotationStopReason StopReason,
    long Revision);

public sealed record RotationInputResult(
    bool Accepted,
    bool Advanced,
    string DiagnosticCode,
    RotationRunSnapshot Snapshot);

public sealed class RotationRunSession
{
    private readonly RotationProfile _profile;
    private readonly RotationBindingSet _bindings;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _heavyThreshold;
    private readonly HashSet<RotationPhysicalInput> _pressed = new();
    private RotationRunStatus _status = RotationRunStatus.Idle;
    private RotationRunStatus _statusBeforePause = RotationRunStatus.Idle;
    private RotationRunPhase _phase = RotationRunPhase.None;
    private int _stepIndex;
    private int _currentSlot;
    private RotationPhysicalInput? _pendingInput;
    private long _pendingTimestamp;
    private bool _pendingHeavy;
    private string? _diagnosticCode;
    private string? _diagnosticMessage;
    private RotationStopReason _stopReason;
    private long _revision;

    public RotationRunSession(
        RotationProfile profile,
        RotationBindingSet bindings,
        TimeSpan heavyThreshold,
        TimeProvider? timeProvider = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        if (!RotationProfileValidator.Validate(profile).IsValid) throw new ArgumentException("Rotation profile is invalid.", nameof(profile));
        if (!RotationBindingValidator.Validate(profile, bindings).IsValid) throw new ArgumentException("Rotation bindings are invalid.", nameof(bindings));
        if (heavyThreshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(heavyThreshold));
        _heavyThreshold = heavyThreshold;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentSlot = profile.InitialSlot;
    }

    public RotationRunSnapshot Snapshot => CreateSnapshot();

    public RotationRunSnapshot Start()
    {
        _status = RotationRunStatus.AwaitingStart;
        _phase = RotationRunPhase.Start;
        _stepIndex = 0;
        _currentSlot = _profile.InitialSlot;
        _stopReason = RotationStopReason.None;
        ClearTransient();
        SetDiagnostic("session.awaitingStart", "等待 Start 输入。");
        return CreateSnapshot();
    }

    public RotationRunSnapshot Reset()
    {
        if (_status == RotationRunStatus.Stopped) return CreateSnapshot();
        _status = RotationRunStatus.AwaitingStart;
        _phase = RotationRunPhase.Start;
        _stepIndex = 0;
        _currentSlot = _profile.InitialSlot;
        _stopReason = RotationStopReason.None;
        ClearTransient();
        SetDiagnostic("session.reset", "连招已重置，等待 Start 输入。");
        return CreateSnapshot();
    }

    public RotationRunSnapshot Stop(RotationStopReason reason = RotationStopReason.UserRequested)
    {
        if (_status != RotationRunStatus.Stopped)
        {
            _status = RotationRunStatus.Stopped;
            _phase = RotationRunPhase.None;
            _stopReason = reason;
            ClearTransient();
            SetDiagnostic("session.stopped", "连招运行已停止。");
        }
        return CreateSnapshot();
    }

    public RotationRunSnapshot SetGameForeground(bool isForeground)
    {
        if (!isForeground)
        {
            if (_status is RotationRunStatus.AwaitingStart or RotationRunStatus.Running)
            {
                _statusBeforePause = _status;
                _status = RotationRunStatus.Paused;
                ClearTransient();
                SetDiagnostic("session.paused", "游戏不在前台，连招已暂停。");
            }
        }
        else if (_status == RotationRunStatus.Paused)
        {
            _status = _statusBeforePause is RotationRunStatus.AwaitingStart or RotationRunStatus.Running
                ? _statusBeforePause
                : RotationRunStatus.AwaitingStart;
            SetDiagnostic("session.resumed", "游戏回到前台，连招已恢复。");
        }
        return CreateSnapshot();
    }

    public RotationInputResult Receive(RotationInputEvent input)
    {
        if (_status == RotationRunStatus.Paused) return Result(false, false, "input.paused", "游戏不在前台，输入未处理。");
        if (_status is RotationRunStatus.Idle or RotationRunStatus.Finished or RotationRunStatus.Stopped)
            return Result(false, false, "input.inactive", "当前运行状态不接受动作输入。");

        if (input.IsDown)
        {
            if (!_pressed.Add(input.Input)) return Result(false, false, "input.repeat", "忽略重复按下。");
            if (MatchesControl(RotationBindingAction.Reselect, input.Input))
            {
                Stop(RotationStopReason.Reselect);
                return Result(true, false, "control.reselect", "已请求重新选择流程。");
            }
            if (MatchesControl(RotationBindingAction.Reset, input.Input))
            {
                Reset();
                return Result(true, false, "control.reset", "连招已重置。");
            }
            if (_status == RotationRunStatus.AwaitingStart)
            {
                if (!MatchesControl(RotationBindingAction.Start, input.Input))
                    return Result(false, false, "input.expectedStart", "等待 Start 输入。");
                BeginProfile();
                return Result(true, true, "control.start", "连招已开始。");
            }

            var step = CurrentStep();
            var expectedAction = RotationBindingValidator.ToBindingAction(step);
            if (!_bindings.TryGet(expectedAction, out var expectedInput) || expectedInput != input.Input)
                return Result(false, false, "input.wrongPress", $"当前期待 {expectedAction}。");
            if (_pendingInput is not null) return Result(false, false, "input.pending", "当前动作正在等待松开。");
            _pendingInput = input.Input;
            _pendingHeavy = step.Action == RotationActionKind.Heavy;
            _pendingTimestamp = _timeProvider.GetTimestamp();
            return Result(true, false, _pendingHeavy ? "input.heavyHolding" : "input.pressed", "已按下期待动作，等待同一输入松开。");
        }

        _pressed.Remove(input.Input);
        if (_pendingInput is null) return Result(false, false, "input.unexpectedRelease", "没有等待松开的动作。");
        if (_pendingInput.Value != input.Input) return Result(false, false, "input.wrongRelease", "松开的输入与按下动作不一致。");

        if (_pendingHeavy)
        {
            var elapsed = _timeProvider.GetElapsedTime(_pendingTimestamp, _timeProvider.GetTimestamp());
            if (elapsed < _heavyThreshold)
            {
                ClearPending();
                return Result(true, false, "input.heavyShort", $"Heavy 持续时间不足 {_heavyThreshold.TotalMilliseconds:0}ms。");
            }
        }

        ClearPending();
        AdvanceStep();
        return Result(true, true, "input.advanced", "动作匹配，已推进。");
    }

    private void BeginProfile()
    {
        ClearTransient();
        if (_profile.Opener.Count > 0)
        {
            _phase = RotationRunPhase.Opener;
            _stepIndex = 0;
            _status = RotationRunStatus.Running;
        }
        else if (_profile.Loop.Count > 0)
        {
            _phase = RotationRunPhase.Loop;
            _stepIndex = 0;
            _status = RotationRunStatus.Running;
        }
        else
        {
            _phase = RotationRunPhase.Finished;
            _status = RotationRunStatus.Finished;
        }
    }

    private void AdvanceStep()
    {
        var completed = CurrentStep();
        if (completed.Action == RotationActionKind.Intro && completed.TargetSlot is not null)
            _currentSlot = completed.TargetSlot.Value;
        _stepIndex++;
        if (_phase == RotationRunPhase.Opener && _stepIndex >= _profile.Opener.Count)
        {
            if (_profile.Loop.Count > 0)
            {
                _phase = RotationRunPhase.Loop;
                _stepIndex = 0;
            }
            else
            {
                _phase = RotationRunPhase.Finished;
                _status = RotationRunStatus.Finished;
            }
        }
        else if (_phase == RotationRunPhase.Loop && _stepIndex >= _profile.Loop.Count)
        {
            _stepIndex = 0;
        }
    }

    private RotationStep CurrentStep() => _phase switch
    {
        RotationRunPhase.Opener => _profile.Opener[_stepIndex],
        RotationRunPhase.Loop => _profile.Loop[_stepIndex],
        _ => throw new InvalidOperationException("There is no current rotation step.")
    };

    private RotationRunSnapshot CreateSnapshot()
    {
        var preview = BuildPreview();
        return new(_status, _phase, _currentSlot, preview, _diagnosticCode, _diagnosticMessage, _stopReason, _revision);
    }

    private IReadOnlyList<RotationPreviewItem> BuildPreview()
    {
        if (_status == RotationRunStatus.AwaitingStart)
        {
            var items = new List<RotationPreviewItem>
            {
                new(true, null, "START", _currentSlot, RotationBindingAction.Start)
            };
            items.AddRange(SimulatePreview(RotationRunPhase.Opener, 0, _currentSlot, 2));
            if (items.Count < 3 && _profile.Opener.Count == 0)
                items.AddRange(SimulatePreview(RotationRunPhase.Loop, 0, _currentSlot, 3 - items.Count));
            return items.AsReadOnly();
        }
        if (_status is not RotationRunStatus.Running and not RotationRunStatus.Paused) return Array.Empty<RotationPreviewItem>();
        return SimulatePreview(_phase, _stepIndex, _currentSlot, 3);
    }

    private IReadOnlyList<RotationPreviewItem> SimulatePreview(RotationRunPhase phase, int index, int slot, int count)
    {
        var result = new List<RotationPreviewItem>(count);
        var guard = 0;
        while (result.Count < count && guard++ < 64)
        {
            IReadOnlyList<RotationStep> steps = phase == RotationRunPhase.Opener ? _profile.Opener : _profile.Loop;
            if (steps.Count == 0)
            {
                if (phase == RotationRunPhase.Opener && _profile.Loop.Count > 0) { phase = RotationRunPhase.Loop; index = 0; continue; }
                break;
            }
            if (index >= steps.Count)
            {
                if (phase == RotationRunPhase.Opener)
                {
                    if (_profile.Loop.Count == 0) break;
                    phase = RotationRunPhase.Loop;
                }
                index = 0;
                continue;
            }
            var step = steps[index++];
            result.Add(new(false, step.Action, step.Description, slot, RotationBindingValidator.ToBindingAction(step)));
            if (step.Action == RotationActionKind.Intro && step.TargetSlot is not null) slot = step.TargetSlot.Value;
        }
        return result.AsReadOnly();
    }

    private bool MatchesControl(RotationBindingAction action, RotationPhysicalInput input) =>
        _bindings.TryGet(action, out var binding) && binding == input;

    private RotationInputResult Result(bool accepted, bool advanced, string code, string message)
    {
        SetDiagnostic(code, message);
        return new(accepted, advanced, code, CreateSnapshot());
    }

    private void SetDiagnostic(string code, string message)
    {
        _diagnosticCode = code;
        _diagnosticMessage = message;
        _revision++;
    }

    private void ClearPending()
    {
        _pendingInput = null;
        _pendingTimestamp = 0;
        _pendingHeavy = false;
    }

    private void ClearTransient()
    {
        _pressed.Clear();
        ClearPending();
    }
}
