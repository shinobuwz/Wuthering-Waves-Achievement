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
        MethodCombo.ItemsSource = new[]
        {
            new PagingMethodChoice(OcrPagingMethod.Drag, "鼠标拖动"),
            new PagingMethodChoice(OcrPagingMethod.Wheel, "鼠标滚轮")
        };
        MethodCombo.DisplayMemberPath = nameof(PagingMethodChoice.Label);
        MethodCombo.SelectedValuePath = nameof(PagingMethodChoice.Method);
        MethodCombo.SelectedValue = _original.Method;
        WheelDistanceBox.Text = _original.WheelDistance.ToString(CultureInfo.InvariantCulture);
        DragDistanceBox.Text = _original.DragDistance.ToString(CultureInfo.InvariantCulture);
        WheelIntervalBox.Text = _original.WheelEventIntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
        SettleDelayBox.Text = _original.MinimumSettleMilliseconds.ToString(CultureInfo.InvariantCulture);
        AutoCalibrateCheckBox.IsChecked = _original.AutoCalibrate;
        CalibrationText.Text = BuildCalibrationText(_original);
    }

    public OcrPagingOptions? AcceptedOptions { get; private set; }
    public bool CalibrationRequested { get; private set; }

    private void Save_OnClick(object sender, RoutedEventArgs e) => Accept(calibrate: false);

    private void SaveAndCalibrate_OnClick(object sender, RoutedEventArgs e) => Accept(calibrate: true);

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Accept(bool calibrate)
    {
        ValidationText.Text = string.Empty;
        if (MethodCombo.SelectedValue is not OcrPagingMethod method ||
            !TryReadRange(WheelDistanceBox.Text, 120, 12000, "滚轮总距离", out var wheelDistance) ||
            !TryReadRange(DragDistanceBox.Text, 80, 800, "拖动距离", out var dragDistance) ||
            !TryReadRange(WheelIntervalBox.Text, 20, 300, "滚轮事件间隔", out var wheelInterval) ||
            !TryReadRange(SettleDelayBox.Text, 50, 1500, "停稳检测起始等待", out var settleDelay))
        {
            return;
        }

        var changedInput = method != _original.Method ||
                           wheelDistance != _original.WheelDistance ||
                           dragDistance != _original.DragDistance ||
                           wheelInterval != _original.WheelEventIntervalMilliseconds;
        var options = _original with
        {
            Method = method,
            WheelDistance = wheelDistance,
            DragDistance = dragDistance,
            WheelEventIntervalMilliseconds = wheelInterval,
            MinimumSettleMilliseconds = settleDelay,
            AutoCalibrate = AutoCalibrateCheckBox.IsChecked == true
        };
        AcceptedOptions = changedInput ? options.ClearCalibration() : options.Normalize();
        CalibrationRequested = calibrate;
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
        return $"{options.CalibratedWidth}×{options.CalibratedHeight} · 向下翻页内容位移 {forward} · 向上翻页内容位移 {reverse} · {options.CalibratedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private sealed record PagingMethodChoice(OcrPagingMethod Method, string Label);
}
