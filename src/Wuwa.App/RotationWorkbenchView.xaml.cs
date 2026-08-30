using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public partial class RotationWorkbenchView : UserControl
{
    private IRotationProfileStore? _profileStore;
    private IRotationSettingsStore? _settingsStore;
    private RotationProfileImportService? _importService;
    private RotationSettings _settings = RotationSettings.Default;
    private Func<RotationProfile, RotationSettings, Task>? _start;
    private RotationBindingAction? _capturingAction;
    private bool _initialized;
    private bool _stopHotKeyAvailable;

    public RotationWorkbenchView() => InitializeComponent();

    public RotationProfile? SelectedProfile => RotationProfileList.SelectedItem as RotationProfile;
    public string? SelectedProfileName => SelectedProfile?.Name;
    public event EventHandler? SelectedProfileChanged;

    public void Configure(string? dataRoot, string? resourceRoot, Func<RotationProfile, RotationSettings, Task> start)
    {
        _profileStore = new JsonRotationProfileStore(dataRoot);
        _settingsStore = new JsonRotationSettingsStore(dataRoot);
        _importService = new RotationProfileImportService(_profileStore, resourceRoot);
        _start = start;
    }

    public async Task InitializeAsync()
    {
        if (_profileStore is null || _settingsStore is null) return;
        try
        {
            _settings = await _settingsStore.LoadAsync();
        }
        catch (Exception exception)
        {
            _settings = RotationSettings.Default;
            SetRuntimeStatus($"连招设置损坏或无法读取，已使用安全默认值：{exception.Message}", true);
        }
        _initialized = true;
        await ReloadProfilesAsync(_settings.SelectedProfileId);
        RefreshBindings();
        ValidateSelection();
    }

    public void SetStopHotKeyAvailability(bool available)
    {
        _stopHotKeyAvailable = available;
        ValidateSelection();
    }

    public void SetRuntimeStatus(string message, bool isError)
    {
        RotationValidationText.Text = message;
        RotationValidationText.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "ErrorBrush" : "MutedTextBrush");
    }

    private async Task ReloadProfilesAsync(RotationProfileId? selectedId = null)
    {
        if (_profileStore is null) return;
        var result = await _profileStore.ListAsync();
        RotationProfileList.ItemsSource = result.Profiles;
        RotationProfileList.SelectedItem = result.Profiles.FirstOrDefault(profile => profile.Id == selectedId) ?? result.Profiles.FirstOrDefault();
        if (result.Issues.Count > 0) SetRuntimeStatus(string.Join("；", result.Issues.Select(issue => issue.Message)), true);
    }

    private async void Import_OnClick(object sender, RoutedEventArgs e)
    {
        if (_importService is null) return;
        var sourcePath = Environment.GetEnvironmentVariable("WUWA_NATIVE_UI_ROTATION_IMPORT_FILE");
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            var dialog = new OpenFileDialog { Filter = "Hekili rotation JSON (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            sourcePath = dialog.FileName;
        }
        try
        {
            var result = await _importService.ImportAsync(sourcePath);
            if (!result.IsSuccess || result.Profile is null)
            {
                SetRuntimeStatus(string.Join("；", result.Errors.Select(issue => issue.Message)), true);
                return;
            }
            await ReloadProfilesAsync(result.Profile.Id);
            var warning = result.Warnings.Count == 0 ? "流程导入成功。" : $"流程导入成功；{string.Join("；", result.Warnings.Select(issue => issue.Message))}";
            SetRuntimeStatus(warning, false);
        }
        catch (Exception exception)
        {
            SetRuntimeStatus($"导入失败：{exception.Message}", true);
        }
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (_profileStore is null || SelectedProfile is not { } profile) return;
        if (MessageBox.Show(Window.GetWindow(this), $"仅删除 Native 流程“{profile.Name}”，是否继续？", "删除连招流程", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _profileStore.DeleteAsync(profile.Id);
            if (_settings.SelectedProfileId == profile.Id)
            {
                _settings = _settings with { SelectedProfileId = null };
                if (_settingsStore is not null) await _settingsStore.SaveAsync(_settings);
            }
            await ReloadProfilesAsync();
        }
        catch (Exception exception)
        {
            SetRuntimeStatus($"删除流程失败：{exception.Message}", true);
        }
    }

    private async void ProfileList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = SelectedProfile;
        ProfileNameText.Text = profile?.Name ?? "请选择流程";
        TeamText.Text = profile is null ? string.Empty : $"队伍：{string.Join(" / ", profile.Team.OrderBy(item => item.Slot).Select(item => $"{item.Slot}. {item.DisplayName}"))} · 初始槽位 {profile.InitialSlot}";
        SequenceText.Text = profile is null ? string.Empty : $"Opener：{profile.Opener.Count} 步\nLoop：{profile.Loop.Count} 步";
        if (_initialized && profile is not null && _settings.SelectedProfileId != profile.Id)
        {
            var previous = _settings;
            _settings = _settings with { SelectedProfileId = profile.Id };
            try
            {
                if (_settingsStore is not null) await _settingsStore.SaveAsync(_settings);
            }
            catch (Exception exception)
            {
                _settings = previous;
                SetRuntimeStatus($"保存流程选择失败：{exception.Message}", true);
                SelectedProfileChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
        ValidateSelection();
        SelectedProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BindingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RotationBindingAction action) return;
        _capturingAction = action;
        RotationValidationText.Text = $"请按下要绑定到 {RotationBindingValidator.GetDisplayName(action)} 的键盘键或鼠标键…";
        Focus();
    }

    private async void Workbench_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingAction is not { } action) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return;
        e.Handled = true;
        await ApplyCapturedBindingAsync(action, new(RotationInputDevice.Keyboard, virtualKey));
    }

    private async void Workbench_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_capturingAction is not { } action) return;
        var code = e.ChangedButton switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 3,
            MouseButton.XButton1 => 4,
            MouseButton.XButton2 => 5,
            _ => 0
        };
        if (code == 0) return;
        e.Handled = true;
        await ApplyCapturedBindingAsync(action, new(RotationInputDevice.Mouse, code));
    }

    private async Task ApplyCapturedBindingAsync(RotationBindingAction action, RotationPhysicalInput input)
    {
        _capturingAction = null;
        var previous = _settings;
        _settings = _settings with { Bindings = _settings.Bindings.With(action, input) };
        try
        {
            if (_settingsStore is not null) await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            _settings = previous;
            RefreshBindings();
            SetRuntimeStatus($"保存键鼠绑定失败：{exception.Message}", true);
            return;
        }
        RefreshBindings();
        ValidateSelection();
    }

    private void RefreshBindings()
    {
        RotationBindingsList.ItemsSource = Enum.GetValues<RotationBindingAction>()
            .Select(action => new BindingRow(
                action,
                RotationBindingValidator.GetDisplayName(action),
                $"RotationBinding{action}Button",
                _settings.Bindings.TryGet(action, out var input) ? FormatInput(input) : "未绑定"))
            .ToArray();
    }

    private void ValidateSelection()
    {
        var profileIssues = RotationProfileValidator.Validate(SelectedProfile).Issues;
        var bindingIssues = RotationBindingValidator.Validate(SelectedProfile, _settings.Bindings).Issues;
        var errors = profileIssues.Concat(bindingIssues).Where(issue => issue.Severity == RotationIssueSeverity.Error).ToArray();
        if (!_stopHotKeyAvailable)
            errors = errors.Append(new RotationIssue("runtime.stopHotKey", "Ctrl+Shift+F11 安全停止快捷键不可用，禁止启动连招。")).ToArray();
        RotationStartButton.IsEnabled = SelectedProfile is not null && errors.Length == 0;
        RotationValidationText.Text = errors.Length == 0 && SelectedProfile is not null ? "流程与键鼠绑定已就绪。" : string.Join("；", errors.Select(issue => issue.Message));
        RotationValidationText.Foreground = (System.Windows.Media.Brush)FindResource(errors.Length == 0 ? "MutedTextBrush" : "ErrorBrush");
    }

    private async void Start_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile || _start is null) return;
        ValidateSelection();
        if (!RotationStartButton.IsEnabled) return;
        await _start(profile, _settings);
    }

    private static string FormatInput(RotationPhysicalInput input) => input.Device switch
    {
        RotationInputDevice.Mouse => input.Code switch { 1 => "鼠标左键", 2 => "鼠标右键", 3 => "鼠标中键", 4 => "鼠标 X1", 5 => "鼠标 X2", _ => $"鼠标 {input.Code}" },
        _ => KeyInterop.KeyFromVirtualKey(input.Code).ToString()
    };

    private sealed record BindingRow(RotationBindingAction Action, string Label, string AutomationId, string InputText);
}
