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

    private static OcrTextLine Line(string text, float y, float score) =>
        new([new OcrPoint(0, y), new OcrPoint(100, y), new OcrPoint(100, y + 10), new OcrPoint(0, y + 10)], text, score);

    private static AchievementRow Row(string code, string name, int order) =>
        new(AchievementId.FromLegacyCode(code), code, order, "1.0", "探索", "今州", name, "", "5", false, null, ProgressStatus.Incomplete);

    private static Achievement Achievement(string code, int order, string name) =>
        new(AchievementId.FromLegacyCode(code), code, order, "1.0", "探索", "今州", name, "", "5", false);
}
