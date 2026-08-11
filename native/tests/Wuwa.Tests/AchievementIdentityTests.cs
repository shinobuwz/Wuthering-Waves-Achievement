using Wuwa.Core;

namespace Wuwa.Tests;

[TestClass]
public sealed class AchievementIdentityTests
{
    [TestMethod]
    public void FromLegacyCode_IsDeterministicAndDistinct()
    {
        var first = AchievementId.FromLegacyCode("10100001");
        var repeated = AchievementId.FromLegacyCode("10100001");
        var second = AchievementId.FromLegacyCode("10100002");

        Assert.AreEqual(first, repeated);
        Assert.AreNotEqual(first, second);
        Assert.AreNotEqual(Guid.Empty, first.Value);
    }

    [TestMethod]
    public void FromLegacyCode_RejectsBlankCodes()
    {
        Assert.ThrowsException<ArgumentException>(() => AchievementId.FromLegacyCode("  "));
    }

    [TestMethod]
    public void ProgressStatus_ExposesExactlyTheFourCanonicalLabels()
    {
        var labels = Enum.GetValues<ProgressStatus>().Select(ProgressStatusText.ToChinese).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "未完成", "已完成", "暂不可获取", "已占用" },
            labels);
        Assert.AreEqual(4, labels.Length);
    }
}
