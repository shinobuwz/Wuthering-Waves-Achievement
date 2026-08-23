using Wuwa.Core;

namespace Wuwa.Tests;

[TestClass]
public sealed class OcrScanHistoryTests
{
    [TestMethod]
    public void History_RecordsNormalizedCategoryPairsAndKeepsLatestScan()
    {
        var first = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var second = first.AddMinutes(10);
        var history = new OcrScanHistory()
            .Record("索拉战士", "昨日今州・其一", 3, first)
            .Record(" 索拉战士 ", "昨日今州·其一", 5, second)
            .Record("索拉战士", "昨日今州·其二", 2, second);

        Assert.IsTrue(history.Contains("索拉战士", "昨日今州・其一"));
        Assert.AreEqual(1, history.PrimaryCategoryCount);
        Assert.AreEqual(2, history.EffectiveCategories.Count);
        Assert.AreEqual(5, history.EffectiveCategories.Single(item => item.SecondaryName.Contains("其一", StringComparison.Ordinal)).Pages);
    }

    [TestMethod]
    public void History_RoundTripsSkipPreferenceAndCanClearEntries()
    {
        var source = new OcrScanHistory(SkipPreviouslyScanned: false)
            .Record("一级", "二级", 4, DateTimeOffset.UnixEpoch);
        var settings = new Dictionary<string, string>
        {
            [OcrScanHistory.SettingKey] = source.ToSettingValue()
        };

        var parsed = OcrScanHistory.FromSettings(settings);
        var removed = parsed.Remove("一级", "二级");
        var cleared = parsed.Clear();

        Assert.IsFalse(parsed.SkipPreviouslyScanned);
        Assert.IsTrue(parsed.Contains("一级", "二级"));
        Assert.IsFalse(removed.Contains("一级", "二级"));
        Assert.AreEqual(0, cleared.EffectiveCategories.Count);
        Assert.IsFalse(cleared.SkipPreviouslyScanned);
    }
}
