using System.Text.Json;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class PersistenceAndMigrationTests
{
    [DataTestMethod]
    [DataRow(nameof(JsonStoreCheckpoint.BeforeStateDocumentWrite))]
    [DataRow(nameof(JsonStoreCheckpoint.AfterStateDocumentFlush))]
    [DataRow(nameof(JsonStoreCheckpoint.BeforeCandidateValidation))]
    [DataRow(nameof(JsonStoreCheckpoint.AfterGenerationPromotion))]
    [DataRow(nameof(JsonStoreCheckpoint.BeforeManifestWrite))]
    [DataRow(nameof(JsonStoreCheckpoint.BeforeManifestReplacement))]
    public async Task Save_FailureBeforeCommitLeavesPriorRevisionActive(string checkpointName)
    {
        await WithRoot(async root =>
        {
            var checkpoint = Enum.Parse<JsonStoreCheckpoint>(checkpointName);
            var first = State(1, ProgressStatus.Incomplete);
            await new JsonAppDataStore(root).SaveAsync(first);
            var store = new JsonAppDataStore(root, 3, new ThrowAt(checkpoint));

            await Assert.ThrowsExceptionAsync<InjectedFailure>(() => store.SaveAsync(State(2, ProgressStatus.Completed)));

            var loaded = await new JsonAppDataStore(root).LoadAsync();
            Assert.AreEqual(1, loaded!.Revision);
            Assert.AreEqual(ProgressStatus.Incomplete, loaded.Statuses.Values.Single());
        });
    }

    [DataTestMethod]
    [DataRow(nameof(JsonStoreCheckpoint.AfterGenerationPromotion))]
    [DataRow(nameof(JsonStoreCheckpoint.BeforeManifestWrite))]
    [DataRow(nameof(JsonStoreCheckpoint.BeforeManifestReplacement))]
    public async Task FirstSave_FailureBeforeManifestDoesNotPromoteOrphan(string checkpointName)
    {
        await WithRoot(async root =>
        {
            var checkpoint = Enum.Parse<JsonStoreCheckpoint>(checkpointName);
            var store = new JsonAppDataStore(root, 3, new ThrowAt(checkpoint));
            await Assert.ThrowsExceptionAsync<InjectedFailure>(() => store.SaveAsync(State(1, ProgressStatus.Completed)));

            Assert.IsNull(await new JsonAppDataStore(root).LoadAsync());
            Assert.IsFalse(File.Exists(Path.Combine(root, "current.json")));
        });
    }

    [DataTestMethod]
    [DataRow(nameof(JsonStoreCheckpoint.AfterManifestReplacement))]
    [DataRow(nameof(JsonStoreCheckpoint.BeforePrune))]
    public async Task FailureAfterCommitIsBestEffortAndNewRevisionRemainsConsistent(string checkpointName)
    {
        await WithRoot(async root =>
        {
            var checkpoint = Enum.Parse<JsonStoreCheckpoint>(checkpointName);
            var store = new JsonAppDataStore(root, 3, new ThrowAt(checkpoint));
            await store.SaveAsync(State(1, ProgressStatus.Completed));

            var loaded = await new JsonAppDataStore(root).LoadAsync();
            Assert.AreEqual(1, loaded!.Revision);
            Assert.AreEqual(ProgressStatus.Completed, loaded.Statuses.Values.Single());
        });
    }

    [TestMethod]
    public async Task MalformedCurrentGenerationFallsBackToPriorValidGeneration()
    {
        await WithRoot(async root =>
        {
            var store = new JsonAppDataStore(root);
            await store.SaveAsync(State(1, ProgressStatus.Incomplete));
            await store.SaveAsync(State(2, ProgressStatus.Completed));
            var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(store.ManifestPath));
            var generation = manifest.RootElement.GetProperty("generation").GetString()!;
            await File.WriteAllTextAsync(Path.Combine(root, "generations", generation, "state.json"), "{}");

            var loaded = await new JsonAppDataStore(root).LoadAsync();
            Assert.AreEqual(1, loaded!.Revision);
            Assert.AreEqual(ProgressStatus.Incomplete, loaded.Statuses.Values.Single());
        });
    }

    [TestMethod]
    public async Task CorruptManifestDoesNotPromoteUncommittedOrphan()
    {
        await WithRoot(async root =>
        {
            await new JsonAppDataStore(root).SaveAsync(State(1, ProgressStatus.Incomplete));
            var failing = new JsonAppDataStore(root, 3, new ThrowAt(JsonStoreCheckpoint.AfterGenerationPromotion));
            await Assert.ThrowsExceptionAsync<InjectedFailure>(() => failing.SaveAsync(State(2, ProgressStatus.Completed)));
            await File.WriteAllTextAsync(Path.Combine(root, "current.json"), "{");

            var loaded = await new JsonAppDataStore(root).LoadAsync();
            Assert.AreEqual(1, loaded!.Revision);
            Assert.AreEqual(ProgressStatus.Incomplete, loaded.Statuses.Values.Single());
        });
    }

    [TestMethod]
    public async Task PruneDoesNotLetUncommittedOrphansConsumeRollbackSlots()
    {
        await WithRoot(async root =>
        {
            var store = new JsonAppDataStore(root, 3);
            await store.SaveAsync(State(1, ProgressStatus.Incomplete));
            await store.SaveAsync(State(2, ProgressStatus.Completed));
            for (var revision = 3; revision <= 6; revision++)
            {
                var failing = new JsonAppDataStore(root, 3, new ThrowAt(JsonStoreCheckpoint.AfterGenerationPromotion));
                await Assert.ThrowsExceptionAsync<InjectedFailure>(() => failing.SaveAsync(State(revision, ProgressStatus.Completed)));
            }
            await store.SaveAsync(State(7, ProgressStatus.Completed));
            var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(store.ManifestPath));
            var current = manifest.RootElement.GetProperty("generation").GetString()!;
            await File.WriteAllTextAsync(Path.Combine(root, "generations", current, "state.json"), "{}");

            var loaded = await new JsonAppDataStore(root, 3).LoadAsync();
            Assert.IsNotNull(loaded);
            Assert.IsTrue(loaded.Revision is 1 or 2, $"Unexpected recovered revision {loaded.Revision}.");
        });
    }

    [TestMethod]
    public async Task InvalidTombstoneFallsBackToPriorValidGeneration()
    {
        await WithRoot(async root =>
        {
            var store = new JsonAppDataStore(root);
            await store.SaveAsync(State(1, ProgressStatus.Incomplete));
            await store.SaveAsync(State(2, ProgressStatus.Completed));
            var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(store.ManifestPath));
            var path = Path.Combine(root, "generations", manifest.RootElement.GetProperty("generation").GetString()!, "state.json");
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace("\"tombstones\": []", "\"tombstones\": [\"not-a-guid\"]", StringComparison.Ordinal));

            var loaded = await new JsonAppDataStore(root).LoadAsync();
            Assert.AreEqual(1, loaded!.Revision);
        });
    }

    [TestMethod]
    public async Task LegacyDiscoveryUsesCanonicalUsernameAndRejectsAmbiguousUid()
    {
        await WithRoot(async root =>
        {
            var config = Path.Combine(root, "config.json");
            await File.WriteAllTextAsync(config, "{\"current_user\":\"account-1\",\"users\":{\"account-1\":{\"nickname\":\"Alice\",\"uid\":\"123\"}}}");
            await File.WriteAllTextAsync(Path.Combine(root, "user_progress_123.json"), "{\"401\":{\"获取状态\":\"已完成\"}}");
            var source = new JsonLegacyProfileSource();

            var valid = await source.DiscoverAsync(config);
            Assert.AreEqual(LegacyDiscoveryStatus.Unambiguous, valid.Status);
            Assert.AreEqual("account-1", valid.Candidates.Single().Username);

            await File.WriteAllTextAsync(config, "{\"current_user\":\"one\",\"users\":{\"one\":{\"nickname\":\"Alice\",\"uid\":\"123\"},\"two\":{\"nickname\":\"Bob\",\"uid\":\"123\"}}}");
            var invalid = await source.DiscoverAsync(config);
            Assert.AreEqual(LegacyDiscoveryStatus.Invalid, invalid.Status);
        });
    }

    [TestMethod]
    public async Task LegacyImportWithUnknownCodeDoesNotActivatePartialState()
    {
        var achievement = Achievement();
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([achievement], Categories())));
        var opened = await workspace.OpenAsync();
        var candidate = new LegacyProfileCandidate("Alice", "123", "fixture.json", 2, "account-1");
        var result = await workspace.ImportLegacyProfileAsync(new UnknownCodeLegacySource(candidate), new LegacyImportOptions(candidate, true));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.LegacyImportFailed, result.Error?.Code);
        Assert.AreEqual(opened.Snapshot.Revision, result.Snapshot.Revision);
        Assert.IsNull(result.Snapshot.Metadata.ImportedAtUtc);
    }

    [TestMethod]
    public async Task LegacyReimportPreservesNativeMetadata()
    {
        var achievement = Achievement();
        var metadata = new WorkspaceMetadata(Settings: new Dictionary<string, string> { ["theme"] = "light" }, IdentityMappings: new Dictionary<string, string> { ["old"] = "new" }, Tombstones: new HashSet<AchievementId> { AchievementId.FromLegacyCode("tomb") });
        var store = new InMemoryAppDataStore();
        await store.SaveAsync(State(1, ProgressStatus.Incomplete, metadata));
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([achievement], Categories())));
        await workspace.OpenAsync();
        var candidate = new LegacyProfileCandidate("Alice", "123", "fixture.json", 1, "account-1");
        var result = await workspace.ImportLegacyProfileAsync(new FixedLegacySource(candidate), new LegacyImportOptions(candidate, true));

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.AreEqual("light", result.Snapshot.Metadata.EffectiveSettings["theme"]);
        Assert.AreEqual("new", result.Snapshot.Metadata.EffectiveIdentityMappings["old"]);
        Assert.IsTrue(result.Snapshot.Metadata.EffectiveTombstones.Contains(AchievementId.FromLegacyCode("tomb")));
        Assert.AreEqual("Alice", result.Snapshot.Metadata.ProfileNickname);
    }

    private static WorkspaceState State(long revision, ProgressStatus status, WorkspaceMetadata? metadata = null)
    {
        var achievement = Achievement();
        return new WorkspaceState(revision, [achievement], new Dictionary<AchievementId, ProgressStatus> { [achievement.Id] = status }, Categories(), metadata);
    }

    private static Achievement Achievement() => new(AchievementId.FromLegacyCode("401"), "401", 1, "1.0", "探索", "区域一", "成就", "描述", "星声*5", false);
    private static CategoryCatalog Categories() => new(new Dictionary<string, int> { ["探索"] = 1 }, new Dictionary<string, IReadOnlyDictionary<string, int>> { ["探索"] = new Dictionary<string, int> { ["区域一"] = 1 } });

    private static async Task WithRoot(Func<string, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "wuwa-native-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await action(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class ThrowAt(JsonStoreCheckpoint checkpoint) : IJsonStoreFaultInjector
    {
        public void OnCheckpoint(JsonStoreCheckpoint current) { if (current == checkpoint) throw new InjectedFailure(); }
    }
    private sealed class InjectedFailure : IOException { }

    private sealed class FixedLegacySource(LegacyProfileCandidate candidate) : ILegacyProfileSource
    {
        public Task<LegacyDiscoveryResult> DiscoverAsync(string configPath, CancellationToken cancellationToken = default) => Task.FromResult(new LegacyDiscoveryResult(LegacyDiscoveryStatus.Unambiguous, [candidate], candidate.Username));
        public Task<LegacyProfileProgress> ReadProgressAsync(LegacyProfileCandidate selected, CancellationToken cancellationToken = default) => Task.FromResult(new LegacyProfileProgress(selected, new Dictionary<string, ProgressStatus> { ["401"] = ProgressStatus.Completed }));
    }

    private sealed class UnknownCodeLegacySource(LegacyProfileCandidate candidate) : ILegacyProfileSource
    {
        public Task<LegacyDiscoveryResult> DiscoverAsync(string configPath, CancellationToken cancellationToken = default) => Task.FromResult(new LegacyDiscoveryResult(LegacyDiscoveryStatus.Unambiguous, [candidate], candidate.Username));
        public Task<LegacyProfileProgress> ReadProgressAsync(LegacyProfileCandidate selected, CancellationToken cancellationToken = default) => Task.FromResult(new LegacyProfileProgress(selected, new Dictionary<string, ProgressStatus> { ["401"] = ProgressStatus.Completed, ["missing"] = ProgressStatus.Completed }));
    }
}
