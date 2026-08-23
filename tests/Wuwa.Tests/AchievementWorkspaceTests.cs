using System.Text.Json;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class AchievementWorkspaceTests
{
    [TestMethod]
    public async Task BuiltInWangRiJinzhouRowsAreGroupedAndWikiSyncRetainsTheOverride()
    {
        var achievements = new[]
        {
            Achievement("10100001", 1, name: "往日之音·今州 Ⅰ", description: "向陈皮交付30个声匣。"),
            Achievement("10100002", 2, name: "往日之音·今州 Ⅱ", description: "向陈皮交付60个声匣。"),
            Achievement("10100003", 3, name: "往日之音·今州 Ⅲ", description: "向陈皮交付115个声匣。")
        };
        var workspace = CreateWorkspace(achievements);

        var opened = await workspace.OpenAsync();
        Assert.IsTrue(opened.IsSuccess);
        Assert.IsTrue(opened.Snapshot.Rows.All(row => row.GroupId == BuiltInAchievementRules.WangRiJinzhouGroupId));

        var remote = achievements
            .Select(item => item with { GroupId = "wiki-should-not-win" })
            .ToArray();
        var synced = await workspace.SyncWikiAsync(new FixedWikiSource(remote));

        Assert.IsTrue(synced.IsSuccess, synced.Error?.Message);
        Assert.IsTrue(synced.Snapshot.Rows.All(row => row.GroupId == BuiltInAchievementRules.WangRiJinzhouGroupId));
    }

    [TestMethod]
    public async Task OpenAndQuery_ReturnRowsAndStatisticsFromTheSameRevision()
    {
        var workspace = CreateWorkspace(
            Achievement("100", 1, "1.0", "探索", "区域一", "晨光", "找到第一处地标"),
            Achievement("200", 2, "1.1", "战斗", "技巧", "静默回响", "完成无声挑战", isHidden: true));

        var opened = await workspace.OpenAsync();
        var view = workspace.Query(new AchievementQuery(SearchText: "无声", Hidden: HiddenFilter.HiddenOnly));

        Assert.IsTrue(opened.IsSuccess);
        Assert.AreEqual(opened.Snapshot.Revision, view.Revision);
        Assert.AreEqual(1, view.Rows.Count);
        Assert.AreEqual("200", view.Rows[0].LegacyCode);
        Assert.AreEqual(view.Revision, view.Statistics.Revision);
        Assert.AreEqual(1, view.Statistics.Total);
        Assert.AreEqual(0, view.Statistics.Completed);
        Assert.AreEqual(1, view.Statistics.Incomplete);
        Assert.AreEqual(1, view.Statistics.Hidden);
    }

    [TestMethod]
    public async Task Query_NameSearchTextOnlyReturnsIncompleteNameMatches()
    {
        var matching = Achievement("050", 1, name: "晨光回响", description: "描述一");
        var descriptionOnly = Achievement("051", 2, name: "静默", description: "晨光回响");
        var completed = Achievement("052", 3, name: "晨光回响", description: "已完成");
        var unavailable = Achievement("053", 4, name: "晨光回响", description: "暂不可获取");
        var workspace = CreateWorkspace(matching, descriptionOnly, completed, unavailable);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(completed.Id, ProgressStatus.Completed);
        await workspace.ChangeStatusAsync(unavailable.Id, ProgressStatus.Unavailable);

        var view = workspace.Query(new AchievementQuery(NameSearchText: "晨光", Status: ProgressStatus.Incomplete));

        CollectionAssert.AreEqual(new[] { "050" }, view.Rows.Select(row => row.LegacyCode).ToArray());
    }

    [TestMethod]
    public async Task Query_AppliesVersionCategoryStatusAndObtainabilityFilters()
    {
        var completed = Achievement("100", 1, "1.0", "探索", "区域一", "晨光", "地标");
        var incomplete = Achievement("200", 2, "1.1", "战斗", "技巧", "回响", "挑战");
        var unavailable = Achievement("300", 3, "1.1", "战斗", "技巧", "绝版", "暂时无法获取");
        var workspace = CreateWorkspace(completed, incomplete, unavailable);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(completed.Id, ProgressStatus.Completed);
        await workspace.ChangeStatusAsync(unavailable.Id, ProgressStatus.Unavailable);

        var view = workspace.Query(new AchievementQuery(
            Version: "1.1",
            FirstCategory: "战斗",
            SecondCategory: "技巧",
            Completion: CompletionFilter.IncompleteOnly,
            Obtainability: ObtainabilityFilter.ObtainableOnly));

        CollectionAssert.AreEqual(new[] { "200" }, view.Rows.Select(row => row.LegacyCode).ToArray());
        Assert.AreEqual(1, view.Statistics.Total);
    }

    [TestMethod]
    public async Task Query_IncompleteFirstOrderingIsStableWithinStatusBuckets()
    {
        var first = Achievement("100", 1, name: "first");
        var second = Achievement("200", 2, name: "second");
        var third = Achievement("300", 3, name: "third");
        var workspace = CreateWorkspace(first, second, third);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(first.Id, ProgressStatus.Completed);

        var view = workspace.Query(new AchievementQuery(Sort: AchievementSort.IncompleteFirst));

        CollectionAssert.AreEqual(new[] { "200", "300", "100" }, view.Rows.Select(row => row.LegacyCode).ToArray());
    }

    [TestMethod]
    public async Task ChangeStatus_RejectsUnknownValuesWithoutAdvancingRevision()
    {
        var achievement = Achievement("100", 1);
        var workspace = CreateWorkspace(achievement);
        var opened = await workspace.OpenAsync();

        var result = await workspace.ChangeStatusAsync(achievement.Id, (ProgressStatus)999);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.InvalidStatus, result.Error?.Code);
        Assert.AreEqual(opened.Snapshot.Revision, result.Snapshot.Revision);
        Assert.AreEqual(ProgressStatus.Incomplete, result.Snapshot.Rows.Single().Status);
    }

    [TestMethod]
    public async Task ChangeStatuses_UpdatesAllSelectedRowsInOneRevision()
    {
        var first = Achievement("120", 1, name: "first");
        var second = Achievement("121", 2, name: "second");
        var third = Achievement("122", 3, name: "third");
        var workspace = CreateWorkspace(first, second, third);
        var opened = await workspace.OpenAsync();

        var result = await workspace.ChangeStatusesAsync([first.Id, third.Id], ProgressStatus.Completed);

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.AreEqual(opened.Snapshot.Revision + 1, result.Snapshot.Revision);
        Assert.AreEqual(ProgressStatus.Completed, result.Snapshot.Rows.Single(row => row.Id == first.Id).Status);
        Assert.AreEqual(ProgressStatus.Incomplete, result.Snapshot.Rows.Single(row => row.Id == second.Id).Status);
        Assert.AreEqual(ProgressStatus.Completed, result.Snapshot.Rows.Single(row => row.Id == third.Id).Status);
        Assert.AreEqual(2, result.Snapshot.Statistics.Completed);
    }

    [TestMethod]
    public async Task ChangeStatus_CancellationReturnsStructuredFailureAndLeavesStateActive()
    {
        var achievement = Achievement("150", 1);
        var workspace = CreateWorkspace(achievement);
        await workspace.OpenAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await workspace.ChangeStatusAsync(achievement.Id, ProgressStatus.Completed, cancellation.Token);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.Cancelled, result.Error?.Code);
        Assert.AreEqual(1, result.Snapshot.Revision);
        Assert.AreEqual(ProgressStatus.Incomplete, result.Snapshot.Rows.Single().Status);
    }

    [TestMethod]
    public async Task ChangeStatus_CompletingOccupiedMemberTransfersThreeMemberGroup()
    {
        var a = Achievement("101", 1, groupId: "choice-3");
        var b = Achievement("102", 2, groupId: "choice-3");
        var c = Achievement("103", 3, groupId: "choice-3");
        var workspace = CreateWorkspace(a, b, c);
        await workspace.OpenAsync();

        var firstCompletion = await workspace.ChangeStatusAsync(a.Id, ProgressStatus.Completed);
        AssertStatuses(firstCompletion, (a.Id, ProgressStatus.Completed), (b.Id, ProgressStatus.Occupied), (c.Id, ProgressStatus.Occupied));

        var transferred = await workspace.ChangeStatusAsync(b.Id, ProgressStatus.Completed);
        Assert.IsTrue(transferred.IsSuccess);
        AssertStatuses(transferred, (a.Id, ProgressStatus.Occupied), (b.Id, ProgressStatus.Completed), (c.Id, ProgressStatus.Occupied));
        Assert.AreEqual(firstCompletion.Snapshot.Revision + 1, transferred.Snapshot.Revision);
        Assert.AreEqual(transferred.Snapshot.Revision, transferred.Snapshot.Statistics.Revision);
        Assert.AreEqual(1, transferred.Snapshot.Statistics.Total);
        Assert.AreEqual(1, transferred.Snapshot.Statistics.Completed);
        Assert.AreEqual(1, transferred.Snapshot.Statistics.GroupedChoiceCount);
    }

    [TestMethod]
    public async Task ChangeStatus_ReopeningCompletedMemberResetsTwoMemberGroup()
    {
        var a = Achievement("201", 1, groupId: "choice-2");
        var b = Achievement("202", 2, groupId: "choice-2");
        var workspace = CreateWorkspace(a, b);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(a.Id, ProgressStatus.Completed);

        var reopened = await workspace.ChangeStatusAsync(a.Id, ProgressStatus.Incomplete);

        AssertStatuses(reopened, (a.Id, ProgressStatus.Incomplete), (b.Id, ProgressStatus.Incomplete));
        Assert.AreEqual(1, reopened.Snapshot.Statistics.Total);
        Assert.AreEqual(0, reopened.Snapshot.Statistics.Completed);
        Assert.AreEqual(1, reopened.Snapshot.Statistics.Incomplete);
    }

    [TestMethod]
    public async Task ChangeStatus_ReopeningOccupiedMemberResetsWholeGroup()
    {
        var a = Achievement("211", 1, groupId: "choice-2b");
        var b = Achievement("212", 2, groupId: "choice-2b");
        var workspace = CreateWorkspace(a, b);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(a.Id, ProgressStatus.Completed);

        var reopened = await workspace.ChangeStatusAsync(b.Id, ProgressStatus.Incomplete);

        AssertStatuses(reopened, (a.Id, ProgressStatus.Incomplete), (b.Id, ProgressStatus.Incomplete));
    }

    [TestMethod]
    public async Task Statistics_CountEachGroupOnceAndExposeFilteredDistributions()
    {
        var a = Achievement("201", 1, "1.0", "探索", "区域一", groupId: "choice-2");
        var b = Achievement("202", 2, "1.0", "探索", "区域一", groupId: "choice-2", isHidden: true);
        var standalone = Achievement("300", 3, "1.0", "探索", "区域二");
        var workspace = CreateWorkspace(a, b, standalone);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(a.Id, ProgressStatus.Completed);

        var view = workspace.Query(new AchievementQuery(FirstCategory: "探索"));

        Assert.AreEqual(2, view.Statistics.Total);
        Assert.AreEqual(1, view.Statistics.Completed);
        Assert.AreEqual(1, view.Statistics.Incomplete);
        Assert.AreEqual(1, view.Statistics.Hidden);
        Assert.AreEqual(1, view.Statistics.GroupedChoiceCount);
        Assert.AreEqual(50d, view.Statistics.CompletionRatePercent, 0.001);
        Assert.AreEqual(2, view.Statistics.ByFirstCategory["探索"]);
        Assert.AreEqual(1, view.Statistics.BySecondCategory["区域一"]);
        Assert.AreEqual(2, view.Statistics.ByVersion["1.0"]);
    }

    [TestMethod]
    public async Task Tracking_BatchRoundTripsInStableOrderAndCompletingRemovesTrackedItem()
    {
        var first = Achievement("350", 1, name: "第一条");
        var second = Achievement("351", 2, name: "第二条");
        var store = new InMemoryAppDataStore();
        var source = new FixedAchievementLibrarySource(new AchievementLibrary([first, second], CategoryCatalog.Empty));
        var workspace = new AchievementWorkspace(store, source);
        await workspace.OpenAsync();

        var added = await workspace.AddTrackedAchievementsAsync([second.Id, first.Id]);

        Assert.IsTrue(added.IsSuccess, added.Error?.Message);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, added.Snapshot.Metadata.EffectiveTrackedAchievementIds.ToArray());

        var reopened = new AchievementWorkspace(store, source);
        var opened = await reopened.OpenAsync();
        Assert.IsTrue(opened.IsSuccess, opened.Error?.Message);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, opened.Snapshot.Metadata.EffectiveTrackedAchievementIds.ToArray());

        var completed = await reopened.ChangeStatusAsync(second.Id, ProgressStatus.Completed);
        Assert.IsTrue(completed.IsSuccess, completed.Error?.Message);
        CollectionAssert.AreEqual(new[] { first.Id }, completed.Snapshot.Metadata.EffectiveTrackedAchievementIds.ToArray());
        Assert.AreEqual(ProgressStatus.Completed, completed.Snapshot.Rows.Single(row => row.Id == second.Id).Status);
    }

    [TestMethod]
    public async Task Tracking_RejectsCompletedUnavailableAndOccupiedAchievements()
    {
        var completed = Achievement("360", 1);
        var unavailable = Achievement("361", 2);
        var choiceA = Achievement("362", 3, groupId: "choice-track");
        var choiceB = Achievement("363", 4, groupId: "choice-track");
        var workspace = CreateWorkspace(completed, unavailable, choiceA, choiceB);
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(completed.Id, ProgressStatus.Completed);
        await workspace.ChangeStatusAsync(unavailable.Id, ProgressStatus.Unavailable);
        await workspace.ChangeStatusAsync(choiceA.Id, ProgressStatus.Completed);

        var completedResult = await workspace.AddTrackedAchievementsAsync([completed.Id]);
        var unavailableResult = await workspace.AddTrackedAchievementsAsync([unavailable.Id]);
        var occupiedResult = await workspace.AddTrackedAchievementsAsync([choiceB.Id]);

        Assert.IsFalse(completedResult.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.TrackingInvalid, completedResult.Error?.Code);
        Assert.IsFalse(unavailableResult.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.TrackingInvalid, unavailableResult.Error?.Code);
        Assert.IsFalse(occupiedResult.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.TrackingInvalid, occupiedResult.Error?.Code);
    }

    [TestMethod]
    public async Task Tracking_CompletingChoiceGroupClearsAllAffectedTrackedItems()
    {
        var first = Achievement("370", 1, groupId: "choice-track-2");
        var second = Achievement("371", 2, groupId: "choice-track-2");
        var workspace = CreateWorkspace(first, second);
        await workspace.OpenAsync();
        await workspace.AddTrackedAchievementsAsync([first.Id, second.Id]);

        var completed = await workspace.ChangeStatusAsync(first.Id, ProgressStatus.Completed);

        Assert.IsTrue(completed.IsSuccess, completed.Error?.Message);
        Assert.AreEqual(0, completed.Snapshot.Metadata.EffectiveTrackedAchievementIds.Count);
        Assert.AreEqual(ProgressStatus.Completed, completed.Snapshot.Rows.Single(row => row.Id == first.Id).Status);
        Assert.AreEqual(ProgressStatus.Occupied, completed.Snapshot.Rows.Single(row => row.Id == second.Id).Status);
    }

    [TestMethod]
    public async Task Open_CleansTrackedItemsThatAreNoLongerIncomplete()
    {
        var completed = Achievement("380", 1);
        var incomplete = Achievement("381", 2);
        var store = new InMemoryAppDataStore();
        var source = new FixedAchievementLibrarySource(new AchievementLibrary([completed, incomplete], CategoryCatalog.Empty));
        var stale = new WorkspaceState(
            4,
            [completed, incomplete],
            new Dictionary<AchievementId, ProgressStatus>
            {
                [completed.Id] = ProgressStatus.Completed,
                [incomplete.Id] = ProgressStatus.Incomplete
            },
            CategoryCatalog.Empty,
            new WorkspaceMetadata(TrackedAchievementIds: [completed.Id, incomplete.Id]));
        await store.SaveAsync(stale);

        var workspace = new AchievementWorkspace(store, source);
        var opened = await workspace.OpenAsync();

        Assert.IsTrue(opened.IsSuccess, opened.Error?.Message);
        CollectionAssert.AreEqual(new[] { incomplete.Id }, opened.Snapshot.Metadata.EffectiveTrackedAchievementIds.ToArray());
        Assert.AreEqual(5, opened.Snapshot.Revision);
    }

    [TestMethod]
    public async Task JsonStore_RoundTripsStatusAndRetainsGenerations()
    {
        var root = Path.Combine(Path.GetTempPath(), "wuwa-native-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var achievement = Achievement("401", 1);
            var source = new FixedAchievementLibrarySource(new AchievementLibrary([achievement], CategoryCatalog.Empty));
            var store = new JsonAppDataStore(root);
            var workspace = new AchievementWorkspace(store, source);
            await workspace.OpenAsync();
            await workspace.ChangeStatusAsync(achievement.Id, ProgressStatus.Completed);

            var reopened = new AchievementWorkspace(new JsonAppDataStore(root), source);
            var result = await reopened.OpenAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ProgressStatus.Completed, result.Snapshot.Rows.Single().Status);
            Assert.IsTrue(Directory.EnumerateDirectories(Path.Combine(root, "generations"), "generation-*").Count() >= 2);
            Assert.IsTrue(File.Exists(Path.Combine(root, "current.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task JsonStore_RecoversPriorGenerationWhenManifestPointsToMissingGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), "wuwa-native-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var achievement = Achievement("402", 1);
            var source = new FixedAchievementLibrarySource(new AchievementLibrary([achievement], CategoryCatalog.Empty));
            var store = new JsonAppDataStore(root);
            var workspace = new AchievementWorkspace(store, source);
            await workspace.OpenAsync();
            await workspace.ChangeStatusAsync(achievement.Id, ProgressStatus.Completed);
            var manifest = Path.Combine(root, "current.json");
            var json = await File.ReadAllTextAsync(manifest);
            var current = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("generation").GetString();
            Directory.Delete(Path.Combine(root, "generations", current!), true);

            var reopened = new AchievementWorkspace(new JsonAppDataStore(root), source);
            var result = await reopened.OpenAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ProgressStatus.Incomplete, result.Snapshot.Rows.Single().Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task LegacySource_ReadsProfilesWithoutChangingLegacyFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "wuwa-legacy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = Path.Combine(root, "config.json");
            var progress = Path.Combine(root, "user_progress_123.json");
            await File.WriteAllTextAsync(config, "{\"current_user\":\"Alice\",\"users\":{\"Alice\":{\"nickname\":\"Alice\",\"uid\":\"123\"}}}");
            await File.WriteAllTextAsync(progress, "{\"401\":{\"获取状态\":\"已完成\"}}");
            var before = await File.ReadAllBytesAsync(progress);
            var source = new JsonLegacyProfileSource();

            var discovered = await source.DiscoverAsync(config);
            var candidate = discovered.Candidates.Single();
            var read = await source.ReadProgressAsync(candidate);
            var after = await File.ReadAllBytesAsync(progress);

            Assert.AreEqual(LegacyDiscoveryStatus.Unambiguous, discovered.Status);
            Assert.AreEqual(ProgressStatus.Completed, read.Statuses["401"]);
            CollectionAssert.AreEqual(before, after);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task Open_MergesMissingGroupMetadataFromShippedLibraryIntoExistingState()
    {
        var root = Path.Combine(Path.GetTempPath(), "wuwa-native-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var stale = Achievement("501", 1);
            var shipped = Achievement("501", 1, groupId: "progression-test");
            var store = new JsonAppDataStore(root);
            var oldState = new WorkspaceState(
                4,
                [stale],
                new Dictionary<AchievementId, ProgressStatus> { [stale.Id] = ProgressStatus.Completed },
                CategoryCatalog.Empty);
            await store.SaveAsync(oldState);

            var source = new FixedAchievementLibrarySource(new AchievementLibrary([shipped], CategoryCatalog.Empty));
            var workspace = new AchievementWorkspace(new JsonAppDataStore(root), source);
            var opened = await workspace.OpenAsync();

            Assert.IsTrue(opened.IsSuccess, opened.Error?.Message);
            Assert.AreEqual("progression-test", opened.Snapshot.Rows.Single().GroupId);
            Assert.AreEqual(ProgressStatus.Completed, opened.Snapshot.Rows.Single().Status);
            Assert.AreEqual(5, opened.Snapshot.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task StatusChange_IsSavedAndVisibleWhenAWorkspaceReopensTheStore()
    {
        var achievement = Achievement("100", 1);
        var store = new InMemoryAppDataStore();
        var source = new FixedAchievementLibrarySource(new AchievementLibrary([achievement], CategoryCatalog.Empty));
        var firstWorkspace = new AchievementWorkspace(store, source);
        await firstWorkspace.OpenAsync();
        var changed = await firstWorkspace.ChangeStatusAsync(achievement.Id, ProgressStatus.Completed);

        var reopenedWorkspace = new AchievementWorkspace(store, source);
        var reopened = await reopenedWorkspace.OpenAsync();

        Assert.AreEqual(changed.Snapshot.Revision, reopened.Snapshot.Revision);
        Assert.AreEqual(ProgressStatus.Completed, reopened.Snapshot.Rows.Single().Status);
    }

    private static AchievementWorkspace CreateWorkspace(params Achievement[] achievements)
    {
        var source = new FixedAchievementLibrarySource(new AchievementLibrary(achievements, CategoryCatalog.Empty));
        return new AchievementWorkspace(new InMemoryAppDataStore(), source);
    }

    private static Achievement Achievement(
        string code,
        int order,
        string version = "1.0",
        string firstCategory = "探索",
        string secondCategory = "区域一",
        string name = "成就",
        string description = "描述",
        bool isHidden = false,
        string? groupId = null) =>
        new(
            AchievementId.FromLegacyCode(code),
            code,
            order,
            version,
            firstCategory,
            secondCategory,
            name,
            description,
            "星声*5",
            isHidden,
            groupId);

    private static void AssertStatuses(WorkspaceCommandResult result, params (AchievementId Id, ProgressStatus Status)[] expected)
    {
        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        foreach (var item in expected)
        {
            Assert.AreEqual(item.Status, result.Snapshot.Rows.Single(row => row.Id == item.Id).Status);
        }
    }

    private sealed class FixedWikiSource(IReadOnlyList<Achievement> achievements) : IWikiAchievementSource
    {
        public Task<WikiFetchResult> FetchAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WikiFetchResult(true, achievements));
    }
}
