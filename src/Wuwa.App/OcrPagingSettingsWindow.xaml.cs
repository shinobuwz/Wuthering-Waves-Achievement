using System.Globalization;
using System.Windows;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrPagingSettingsWindow : Window
{
    private readonly OcrPagingOptions _original;

    public OcrPagingSettingsWindow(OcrPagingOptions options)
    {
        _original = (options ?? throw new ArgumentNullException(nameof(options))).Normalize();
        InitializeComponent();
        WheelDistanceBox.Text = _original.WheelDistance.ToString(CultureInfo.InvariantCulture);
        SecondaryWheelDistanceBox.Text = _original.SecondaryWheelDistance.ToString(CultureInfo.InvariantCulture);
        WheelIntervalBox.Text = _original.WheelEventIntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
        SettleDelayBox.Text = _original.MinimumSettleMilliseconds.ToString(CultureInfo.InvariantCulture);
        TextFieldFocusDelayBox.Text = _original.TextFieldFocusSettleMilliseconds.ToString(CultureInfo.InvariantCulture);
        ModifierDelayBox.Text = _original.ModifierSettleMilliseconds.ToString(CultureInfo.InvariantCulture);
        KeyPressDelayBox.Text = _original.KeyPressMilliseconds.ToString(CultureInfo.InvariantCulture);
        SelectAllDelayBox.Text = _original.SelectAllSettleMilliseconds.ToString(CultureInfo.InvariantCulture);
        ClipboardPasteDelayBox.Text = _original.ClipboardPasteSettleMilliseconds.ToString(CultureInfo.InvariantCulture);
        CalibrationText.Text = BuildCalibrationText(_original);
    }

    public OcrPagingOptions? AcceptedOptions { get; private set; }
    public OcrPagingCalibrationTarget CalibrationTarget { get; private set; }

    private void Save_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.None);

    private void CalibrateAchievement_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.AchievementList);

    private void CalibrateSecondaryTags_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.SecondaryTags);

    private void TestSearchInput_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.SearchInput);

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Accept(OcrPagingCalibrationTarget calibrationTarget)
    {
        ValidationText.Text = string.Empty;
        if (!TryReadRange(WheelDistanceBox.Text, 120, 12000, "成就列表滚轮总距离", out var wheelDistance) ||
            !TryReadRange(SecondaryWheelDistanceBox.Text, 120, OcrPagingOptions.MaximumSecondaryWheelDistance, "二级 Tag 滚轮总距离", out var secondaryWheelDistance) ||
            !TryReadRange(WheelIntervalBox.Text, 20, 300, "滚轮事件间隔", out var wheelInterval) ||
            !TryReadRange(SettleDelayBox.Text, 50, 1500, "停稳检测起始等待", out var settleDelay) ||
            !TryReadRange(TextFieldFocusDelayBox.Text, 50, 2000, "聚焦搜索框后等待", out var textFieldFocusDelay) ||
            !TryReadRange(ModifierDelayBox.Text, 10, 500, "修饰键切换间隔", out var modifierDelay) ||
            !TryReadRange(KeyPressDelayBox.Text, 10, 300, "按键按下时间", out var keyPressDelay) ||
            !TryReadRange(SelectAllDelayBox.Text, 20, 1000, "Ctrl+A 后等待", out var selectAllDelay) ||
            !TryReadRange(ClipboardPasteDelayBox.Text, 50, 2500, "Ctrl+V 后等待", out var clipboardPasteDelay))
        {
            return;
        }

        var options = (_original with
        {
            WheelDistance = wheelDistance,
            SecondaryWheelDistance = secondaryWheelDistance,
            WheelEventIntervalMilliseconds = wheelInterval,
            MinimumSettleMilliseconds = settleDelay,
            AutoCalibrate = false,
            TextFieldFocusSettleMilliseconds = textFieldFocusDelay,
            ModifierSettleMilliseconds = modifierDelay,
            KeyPressMilliseconds = keyPressDelay,
            SelectAllSettleMilliseconds = selectAllDelay,
            ClipboardPasteSettleMilliseconds = clipboardPasteDelay
        }).Normalize();
        if (wheelDistance != _original.WheelDistance)
        {
            options = options with { LastForwardPixels = null, LastReversePixels = null };
        }
        if (secondaryWheelDistance != _original.SecondaryWheelDistance)
        {
            options = options with { LastSecondaryPixels = null };
        }
        AcceptedOptions = options;
        CalibrationTarget = calibrationTarget;
        DialogResult = true;
        Close();
    }

    private bool TryReadRange(string text, int minimum, int maximum, string label, out int value)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
            value < minimum || value > maximum)
        {
            ValidationText.Text = $"{label}必须是 {minimum}–{maximum} 之间的整数。";
            return false;
        }
        return true;
    }

    private static string BuildCalibrationText(OcrPagingOptions options)
    {
        if (options.CalibratedAtUtc is null)
        {
            return "尚未校准。手动校准会按当前固定距离执行往返滚动并记录实测位移。";
        }

        var forward = options.LastForwardPixels is null ? "—" : $"{options.LastForwardPixels} px";
        var reverse = options.LastReversePixels is null ? "—" : $"{options.LastReversePixels} px";
        var secondary = options.LastSecondaryPixels is null ? "—" : $"{options.LastSecondaryPixels} px";
        return $"{options.CalibratedWidth}×{options.CalibratedHeight} · 成就向下 {forward} · 成就向上 {reverse} · 二级 Tag {secondary} · {options.CalibratedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
    }
}

public enum OcrPagingCalibrationTarget
{
    None,
    AchievementList,
    SecondaryTags,
    SearchInput
}
