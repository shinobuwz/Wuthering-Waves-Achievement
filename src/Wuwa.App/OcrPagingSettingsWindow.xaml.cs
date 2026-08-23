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
        AutoCalibrateCheckBox.IsChecked = _original.AutoCalibrate;
        CalibrationText.Text = BuildCalibrationText(_original);
    }

    public OcrPagingOptions? AcceptedOptions { get; private set; }
    public OcrPagingCalibrationTarget CalibrationTarget { get; private set; }

    private void Save_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.None);

    private void CalibrateAchievement_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.AchievementList);

    private void CalibrateSecondaryTags_OnClick(object sender, RoutedEventArgs e) => Accept(OcrPagingCalibrationTarget.SecondaryTags);

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
            !TryReadRange(SettleDelayBox.Text, 50, 1500, "停稳检测起始等待", out var settleDelay))
        {
            return;
        }

        var options = (_original with
        {
            WheelDistance = wheelDistance,
            SecondaryWheelDistance = secondaryWheelDistance,
            WheelEventIntervalMilliseconds = wheelInterval,
            MinimumSettleMilliseconds = settleDelay,
            AutoCalibrate = AutoCalibrateCheckBox.IsChecked == true
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
            return "尚未校准。启用自动校准后，首次产生可靠翻页位移时会记录当前分辨率和实测距离。";
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
    SecondaryTags
}
