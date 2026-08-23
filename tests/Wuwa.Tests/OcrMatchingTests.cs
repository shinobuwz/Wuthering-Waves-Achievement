using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class OcrMatchingTests
{
    [TestMethod]
    public void CreatePreview_NormalizesMiddleDotsMatchesNamesAndPairsNearestStatus()
    {
        var rows = new[]
        {
            Row("100", "昨日今州", 1),
            Row("200", "猫咪·寻宝", 2)
        };
        var lines = new[]
        {
            Line("猫咪・寻宝", 100, 0.96f),
            Line("2026/08/12", 112, 0.99f),
            Line("不存在的名字", 300, 0.90f)
        };

        var preview = AchievementOcrMatcher.CreatePreview(lines, rows);

        Assert.AreEqual(1, preview.Candidates.Count);
        Assert.AreEqual("200", preview.Candidates[0].LegacyCode);
        Assert.AreEqual(ProgressStatus.Completed, preview.Candidates[0].ProposedStatus);
        Assert.AreEqual(1, preview.CompletedCount);
        Assert.AreEqual(1, preview.Unmatched.Count);
    }

    [TestMethod]
    public void CreatePreview_MapsSharedWangRiJinzhouTitleByDescription()
    {
        const string groupId = BuiltInAchievementRules.WangRiJinzhouGroupId;
        Assert.IsTrue(BuiltInAchievementRules.IsWangRiJinzhouOcrName("日之音・今m]"));
        var rows = new[]
        {
            WangRow("10100001", "Ⅰ", "向陈皮交付30个声匣。", 1, groupId),
            WangRow("10100002", "Ⅱ", "向陈皮交付60个声匣。", 2, groupId),
            WangRow("10100003", "Ⅲ", "向陈皮交付115个声匣。", 3, groupId)
        };
        var lines = new[]
        {
            Line("往日之音·今州", 100, 0.98f, OcrTextKind.AchievementName),
            Line("向陈皮交付60个声匣。", 130, 0.94f, OcrTextKind.AchievementDescription),
            Line("2026/08/12", 112, 0.99f, OcrTextKind.AchievementStatus)
        };

        var preview = AchievementOcrMatcher.CreatePreview(lines, rows);

        Assert.AreEqual(1, preview.Candidates.Count);
        Assert.AreEqual("10100002", preview.Candidates[0].LegacyCode);
        Assert.AreEqual(ProgressStatus.Completed, preview.Candidates[0].ProposedStatus);
        Assert.AreEqual(0, preview.Unmatched.Count);
    }

    [TestMethod]
    public void MatchKnownText_UsesNameNormalizationAndDistanceThreshold()
    {
        var matched = AchievementOcrMatcher.MatchKnownText("猫咪・寻宝]", ["猫咪·寻宝", "昨日今州"], out var confidence);

        Assert.AreEqual("猫咪·寻宝", matched);
        Assert.IsTrue(confidence > 0.7);
        Assert.IsNull(AchievementOcrMatcher.MatchKnownText("完全不存在", ["猫咪·寻宝"], out _));
    }

    [TestMethod]
    public void CreateTargetedSearchCandidate_UsesStatusCropEvenWhenNameOcrIsNoisy()
    {
        var row = Row("10100008", "打上花火", 1);
        var lines = new[]
        {
            Line("打h花火]", 100, 0.82f, OcrTextKind.AchievementName),
            Line("2025/09/26", 112, 0.98f, OcrTextKind.AchievementStatus)
        };

        var candidate = AchievementOcrMatcher.CreateTargetedSearchCandidate(lines, row);

        Assert.AreEqual(row.Id, candidate.AchievementId);
        Assert.AreEqual("打h花火]", candidate.OcrText);
        Assert.AreEqual(ProgressStatus.Completed, candidate.ProposedStatus);
        Assert.AreEqual("2025/09/26", candidate.StatusText);
        Assert.IsFalse(candidate.IsAmbiguous);
        Assert.IsTrue(candidate.MatchConfidence < OcrAchievementCandidate.MinimumApplicableConfidence);
        Assert.IsFalse(candidate.CanApply);
    }

    [TestMethod]
    public void CreateTargetedSearchCandidate_LeavesStatusUnknownWhenStatusCropIsMissing()
    {
        var row = Row("10100013", "消逝余残响", 1);
        var lines = new[]
        {
            Line("消m余残n", 100, 0.75f, OcrTextKind.AchievementName)
        };

        var candidate = AchievementOcrMatcher.CreateTargetedSearchCandidate(lines, row);

        Assert.IsNull(candidate.ProposedStatus);
        Assert.IsFalse(candidate.IsAmbiguous);
        Assert.IsFalse(candidate.CanApply);
    }

    [TestMethod]
    public async Task ApplyOcrPreview_DoesNotUseCandidatesBelowSeventyFivePercentConfidence()
    {
        var achievement = Achievement("100", 1, "先行之证·今州");
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([achievement], CategoryCatalog.Empty)));
        await workspace.OpenAsync();
        var before = workspace.Query().Revision;
        var lowConfidence = new OcrAchievementCandidate(
            achievement.Id,
            achievement.LegacyCode,
            achievement.Name,
            "先行之证今",
            0.74,
            ProgressStatus.Completed,
            "2025/04/21");
        var preview = new OcrScanPreview([lowConfidence], [], 0, 0, 1);

        var applied = await workspace.ApplyOcrPreviewAsync(preview, confirm: true);

        Assert.IsTrue(applied.IsSuccess);
        Assert.AreEqual(0, applied.Updated);
        Assert.AreEqual(1, applied.Unchanged);
        Assert.AreEqual(before, applied.Snapshot.Revision);
        Assert.AreEqual(ProgressStatus.Incomplete, applied.Snapshot.Rows.Single().Status);
    }

    [TestMethod]
    public async Task ApplyOcrPreview_RequiresConfirmationAndCommitsOneRevisionWithoutDowngrade()
    {
        var first = Achievement("100", 1, "昨日今州");
        var second = Achievement("200", 2, "猫咪·寻宝");
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([first, second], CategoryCatalog.Empty)));
        var opened = await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(first.Id, ProgressStatus.Completed);
        var before = workspace.Query().Revision;
        var preview = new OcrScanPreview(
            [
                Candidate(first, ProgressStatus.Incomplete),
                Candidate(second, ProgressStatus.Completed)
            ],
            [],
            1,
            1,
            0);

        var rejected = await workspace.ApplyOcrPreviewAsync(preview, confirm: false);
        var applied = await workspace.ApplyOcrPreviewAsync(preview, confirm: true);
        var rows = workspace.Query().Rows.ToDictionary(row => row.LegacyCode);

        Assert.IsFalse(rejected.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.OcrApplyRequiresConfirmation, rejected.Error?.Code);
        Assert.IsTrue(applied.IsSuccess);
        Assert.AreEqual(before + 1, applied.Snapshot.Revision);
        Assert.AreEqual(1, applied.Updated);
        Assert.AreEqual(1, applied.PreventedDowngrades);
        Assert.AreEqual(ProgressStatus.Completed, rows["100"].Status);
        Assert.AreEqual(ProgressStatus.Completed, rows["200"].Status);
        Assert.IsTrue(applied.Snapshot.Metadata.EffectiveSettings.ContainsKey("ocr.lastAppliedAtUtc"));
    }

    [TestMethod]
    public async Task ChangeStatus_PreservesWorkspaceMetadata()
    {
        var achievement = Achievement("100", 1, "昨日今州");
        var state = new WorkspaceState(
            1,
            [achievement],
            new Dictionary<AchievementId, ProgressStatus> { [achievement.Id] = ProgressStatus.Incomplete },
            CategoryCatalog.Empty,
            new WorkspaceMetadata(Settings: new Dictionary<string, string> { ["theme"] = "light" }));
        var store = new InMemoryAppDataStore();
        await store.SaveAsync(state);
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([achievement], CategoryCatalog.Empty)));
        await workspace.OpenAsync();

        var changed = await workspace.ChangeStatusAsync(achievement.Id, ProgressStatus.Completed);

        Assert.AreEqual("light", changed.Snapshot.Metadata.EffectiveSettings["theme"]);
    }

    private static OcrAchievementCandidate Candidate(Achievement item, ProgressStatus status) =>
        new(item.Id, item.LegacyCode, item.Name, item.Name, 1.0, status, status.ToChinese());

    private static OcrTextLine Line(string text, float y, float score, OcrTextKind kind = OcrTextKind.Unknown) =>
        new([new OcrPoint(0, y), new OcrPoint(100, y), new OcrPoint(100, y + 10), new OcrPoint(0, y + 10)], text, score, kind);

    private static AchievementRow Row(string code, string name, int order) =>
        new(AchievementId.FromLegacyCode(code), code, order, "1.0", "探索", "今州", name, "", "5", false, null, ProgressStatus.Incomplete);

    private static AchievementRow WangRow(string code, string ordinal, string description, int order, string groupId) =>
        new(AchievementId.FromLegacyCode(code), code, order, "1.0", "索拉漫行", "索拉的大地·瑝珑", $"往日之音·今州 {ordinal}", description, "星声*5", false, groupId, ProgressStatus.Incomplete);

    private static Achievement Achievement(string code, int order, string name) =>
        new(AchievementId.FromLegacyCode(code), code, order, "1.0", "探索", "今州", name, "", "5", false);
}
