using System.Text.Json;
using System.Text.Json.Serialization;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

internal enum JsonStoreCheckpoint
{
    BeforeStateDocumentWrite,
    AfterStateDocumentFlush,
    BeforeCandidateValidation,
    AfterGenerationPromotion,
    BeforeManifestWrite,
    BeforeManifestReplacement,
    AfterManifestReplacement,
    BeforePrune
}

internal interface IJsonStoreFaultInjector
{
    void OnCheckpoint(JsonStoreCheckpoint checkpoint);
}

public sealed class JsonAppDataStore : IAppDataStore
{
    private const int SchemaVersion = 1;
    private const string ManifestFileName = "current.json";
    private const string ActivationMarkerFileName = ".activation-history";
    private const string GenerationCommitMarkerFileName = ".committed";
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IJsonStoreFaultInjector? _faultInjector;

    public JsonAppDataStore(string? rootDirectory = null, int retainedGenerations = 3)
        : this(rootDirectory, retainedGenerations, null)
    {
    }

    internal JsonAppDataStore(string? rootDirectory, int retainedGenerations, IJsonStoreFaultInjector? faultInjector)
    {
        _root = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WutheringWavesAchievement")
            : Path.GetFullPath(rootDirectory);
        RetainedGenerations = Math.Max(3, retainedGenerations);
        _faultInjector = faultInjector;
    }

    public int RetainedGenerations { get; }
    public string RootDirectory => _root;
    public string ManifestPath => Path.Combine(_root, ManifestFileName);
    public bool HasActiveState => File.Exists(ManifestPath);

    public async Task<WorkspaceState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(ManifestPath))
            {
                // A final generation directory alone is not proof that activation committed.  Only
                // stores that have durable activation history may reconstruct a missing pointer.
                if (!File.Exists(Path.Combine(_root, ActivationMarkerFileName))) return null;
                var recovered = await FindNewestValidGenerationAsync(requireCommitMarker: true, cancellationToken).ConfigureAwait(false);
                if (recovered is null) return null;
                await ActivateRecoveredGenerationAsync(recovered.Value.Name, cancellationToken).ConfigureAwait(false);
                return recovered.Value.State;
            }

            try
            {
                var manifest = await ReadJsonAsync<ManifestDocument>(ManifestPath, cancellationToken).ConfigureAwait(false);
                if (manifest.SchemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(manifest.Generation))
                {
                    throw new InvalidDataException("Unsupported manifest.");
                }

                var state = await ReadGenerationAsync(manifest.Generation, cancellationToken).ConfigureAwait(false);
                MarkGenerationCommitted(manifest.Generation);
                MarkActivationHistory();
                return state;
            }
            catch (Exception exception) when (IsRecoverableDataFailure(exception))
            {
                // Marker-aware stores must never activate a promoted-but-uncommitted generation.
                // The unrestricted scan is only for a pre-marker store that has a legacy manifest.
                var markerAware = File.Exists(Path.Combine(_root, ActivationMarkerFileName));
                var recovered = await FindNewestValidGenerationAsync(requireCommitMarker: markerAware, cancellationToken).ConfigureAwait(false);
                if (recovered is not null)
                {
                    await ActivateRecoveredGenerationAsync(recovered.Value.Name, cancellationToken).ConfigureAwait(false);
                    return recovered.Value.State;
                }
                throw new InvalidDataException("No valid native generation could be recovered.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            var generationsDirectory = Path.Combine(_root, "generations");
            Directory.CreateDirectory(generationsDirectory);
            var generationName = $"generation-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            var temporaryDirectory = Path.Combine(generationsDirectory, $".tmp-{Guid.NewGuid():N}");
            var finalDirectory = Path.Combine(generationsDirectory, generationName);
            Directory.CreateDirectory(temporaryDirectory);
            var committed = false;

            try
            {
                _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.BeforeStateDocumentWrite);
                var statePath = Path.Combine(temporaryDirectory, "state.json");
                await WriteJsonAsync(statePath, StateDocument.FromState(state, SchemaVersion), cancellationToken).ConfigureAwait(false);
                _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.AfterStateDocumentFlush);

                _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.BeforeCandidateValidation);
                var validated = await ReadStateDocumentAsync(statePath, cancellationToken).ConfigureAwait(false);
                ValidateState(validated.ToState());
                Directory.Move(temporaryDirectory, finalDirectory);
                _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.AfterGenerationPromotion);

                var temporaryManifestPath = Path.Combine(_root, $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");
                _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.BeforeManifestWrite);
                await WriteJsonAsync(temporaryManifestPath, new ManifestDocument(SchemaVersion, generationName), cancellationToken).ConfigureAwait(false);
                _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.BeforeManifestReplacement);
                ReplaceFileAtomically(temporaryManifestPath, ManifestPath);
                committed = true;

                // Manifest replacement is the commit point. Everything after it is non-cancellable,
                // best-effort housekeeping and must never turn a committed command into a failure.
                MarkGenerationCommitted(generationName);
                MarkActivationHistory();
                try { _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.AfterManifestReplacement); } catch { }
                try
                {
                    _faultInjector?.OnCheckpoint(JsonStoreCheckpoint.BeforePrune);
                    await PruneGenerationsAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Retention cleanup is safe to retry on a later save.
                }
            }
            catch
            {
                TryDeleteDirectory(temporaryDirectory);
                if (!committed) throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ActivateRecoveredGenerationAsync(string generationName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var recoveryManifest = Path.Combine(_root, $".{ManifestFileName}.recovery-{Guid.NewGuid():N}.tmp");
        await WriteJsonAsync(recoveryManifest, new ManifestDocument(SchemaVersion, generationName), cancellationToken).ConfigureAwait(false);
        ReplaceFileAtomically(recoveryManifest, ManifestPath);
        MarkGenerationCommitted(generationName);
        MarkActivationHistory();
    }

    private async Task<(string Name, WorkspaceState State)?> FindNewestValidGenerationAsync(bool requireCommitMarker, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, "generations");
        if (!Directory.Exists(directory)) return null;
        foreach (var path in Directory.EnumerateDirectories(directory, "generation-*").OrderByDescending(item => item, StringComparer.Ordinal))
        {
            if (requireCommitMarker && !File.Exists(Path.Combine(path, GenerationCommitMarkerFileName))) continue;
            try
            {
                var name = Path.GetFileName(path);
                var state = await ReadGenerationAsync(name, cancellationToken).ConfigureAwait(false);
                return (name, state);
            }
            catch (Exception exception) when (IsRecoverableDataFailure(exception)) { }
        }
        return null;
    }

    private async Task<WorkspaceState> ReadGenerationAsync(string generationName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(generationName) ||
            generationName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            generationName.Contains(Path.DirectorySeparatorChar) ||
            generationName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException("The native manifest contains an invalid generation reference.");
        }

        var path = Path.Combine(_root, "generations", generationName, "state.json");
        var document = await ReadStateDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        if (document.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException("The native generation schema is not supported.");
        }

        var state = document.ToState();
        ValidateState(state);
        return state;
    }

    private async Task PruneGenerationsAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, "generations");
        if (!Directory.Exists(directory) || !File.Exists(ManifestPath)) return;
        var manifest = await ReadJsonAsync<ManifestDocument>(ManifestPath, cancellationToken).ConfigureAwait(false);
        var valid = new List<(string Name, DateTime LastWriteUtc)>();
        foreach (var path in Directory.EnumerateDirectories(directory, "generation-*"))
        {
            var name = Path.GetFileName(path);
            if (!File.Exists(Path.Combine(path, GenerationCommitMarkerFileName))) continue;
            try
            {
                await ReadGenerationAsync(name, cancellationToken).ConfigureAwait(false);
                valid.Add((name, Directory.GetLastWriteTimeUtc(path)));
            }
            catch (Exception exception) when (IsRecoverableDataFailure(exception))
            {
                // Invalid generations are retained for forensic recovery and are never selected.
            }
        }

        var keep = valid
            .OrderByDescending(item => item.LastWriteUtc)
            .ThenByDescending(item => item.Name, StringComparer.Ordinal)
            .Take(RetainedGenerations)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
        keep.Add(manifest.Generation);

        foreach (var item in valid.Where(item => !keep.Contains(item.Name)))
        {
            TryDeleteDirectory(Path.Combine(directory, item.Name));
        }
    }

    private static void ValidateState(WorkspaceState state)
    {
        if (state.Revision < 1 || state.Achievements.Count == 0)
        {
            throw new InvalidDataException("Native workspace state is empty or has an invalid revision.");
        }

        var ids = new HashSet<AchievementId>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var achievement in state.Achievements)
        {
            if (!ids.Add(achievement.Id) || !codes.Add(achievement.LegacyCode) || string.IsNullOrWhiteSpace(achievement.LegacyCode))
            {
                throw new InvalidDataException("Native workspace state contains duplicate or blank achievement identity.");
            }
            if (string.IsNullOrWhiteSpace(achievement.Version) || string.IsNullOrWhiteSpace(achievement.FirstCategory) ||
                string.IsNullOrWhiteSpace(achievement.SecondCategory) || string.IsNullOrWhiteSpace(achievement.Name) ||
                string.IsNullOrWhiteSpace(achievement.Description))
            {
                throw new InvalidDataException($"Achievement {achievement.LegacyCode} has missing required fields.");
            }
            if (!state.Statuses.TryGetValue(achievement.Id, out var status) || !Enum.IsDefined(status))
            {
                throw new InvalidDataException($"Achievement {achievement.LegacyCode} has no valid progress status.");
            }
        }

        if (state.Statuses.Count != ids.Count || state.Statuses.Keys.Any(id => !ids.Contains(id)))
        {
            throw new InvalidDataException("Native workspace state contains missing or unknown status identities.");
        }
    }

    private static bool IsRecoverableDataFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or ArgumentException or NullReferenceException;

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, CreateJsonOptions(), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"JSON document '{path}' is empty.");
    }

    private async Task<StateDocument> ReadStateDocumentAsync(string path, CancellationToken cancellationToken) =>
        await ReadJsonAsync<StateDocument>(path, cancellationToken).ConfigureAwait(false);

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceFileAtomically(string temporaryPath, string destinationPath)
    {
        if (OperatingSystem.IsWindows() && File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
        else File.Move(temporaryPath, destinationPath, true);
    }

    private void MarkGenerationCommitted(string generationName)
    {
        try
        {
            var marker = Path.Combine(_root, "generations", generationName, GenerationCommitMarkerFileName);
            if (!File.Exists(marker)) File.WriteAllText(marker, SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private void MarkActivationHistory()
    {
        try
        {
            var marker = Path.Combine(_root, ActivationMarkerFileName);
            if (!File.Exists(marker)) File.WriteAllText(marker, SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record ManifestDocument(int SchemaVersion, string Generation);

    private sealed record StateDocument(
        int SchemaVersion,
        long Revision,
        IReadOnlyList<AchievementDocument>? Achievements,
        IReadOnlyDictionary<string, string>? Statuses,
        CategoryDocument? Categories,
        MetadataDocument? Metadata)
    {
        public static StateDocument FromState(WorkspaceState state, int schemaVersion) => new(
            schemaVersion,
            state.Revision,
            state.Achievements.Select(AchievementDocument.FromAchievement).ToArray(),
            state.Statuses.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToChinese(), StringComparer.Ordinal),
            CategoryDocument.FromCatalog(state.Categories),
            MetadataDocument.FromMetadata(state.Metadata));

        public WorkspaceState ToState()
        {
            if (Achievements is null || Statuses is null || Categories is null || Metadata is null)
            {
                throw new InvalidDataException("Native state is structurally incomplete.");
            }
            var achievements = Achievements.Select(item => item?.ToAchievement() ?? throw new InvalidDataException("Native state contains a null achievement.")).ToArray();
            var statuses = new Dictionary<AchievementId, ProgressStatus>();
            foreach (var item in Statuses)
            {
                if (!Guid.TryParse(item.Key, out var guid) || !ProgressStatusText.TryParseChinese(item.Value, out var status) || !statuses.TryAdd(new AchievementId(guid), status))
                {
                    throw new InvalidDataException("Native state contains an invalid or duplicate identity/status.");
                }
            }
            return new WorkspaceState(Revision, achievements, statuses, Categories.ToCatalog(), Metadata.ToMetadata());
        }
    }

    private sealed record AchievementDocument(
        string? Id, string? LegacyCode, int AbsoluteOrder, string? Version, string? FirstCategory,
        string? SecondCategory, string? Name, string? Description, string? Reward, bool IsHidden,
        string? GroupId, string? WikiSourceRef, bool IsTombstone, IReadOnlyList<string>? MutualExclusionCodes)
    {
        public static AchievementDocument FromAchievement(Achievement item) => new(item.Id.ToString(), item.LegacyCode, item.AbsoluteOrder, item.Version, item.FirstCategory, item.SecondCategory, item.Name, item.Description, item.Reward, item.IsHidden, item.GroupId, item.WikiSourceRef, item.IsTombstone, item.EffectiveMutualExclusionCodes);
        public Achievement ToAchievement()
        {
            if (!Guid.TryParse(Id, out var guid) || string.IsNullOrWhiteSpace(LegacyCode) || Version is null || FirstCategory is null || SecondCategory is null || Name is null || Description is null || Reward is null)
                throw new InvalidDataException("Native state contains an invalid achievement document.");
            return new Achievement(new AchievementId(guid), LegacyCode, AbsoluteOrder, Version, FirstCategory, SecondCategory, Name, Description, Reward, IsHidden, GroupId, WikiSourceRef, IsTombstone, MutualExclusionCodes);
        }
    }

    private sealed record CategoryDocument(IReadOnlyDictionary<string, int>? First, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? Second)
    {
        public static CategoryDocument FromCatalog(CategoryCatalog catalog) => new(catalog.FirstCategories, catalog.SecondCategories);
        public CategoryCatalog ToCatalog() => First is null || Second is null
            ? throw new InvalidDataException("Native category catalog is incomplete.")
            : new CategoryCatalog(First, Second);
    }

    private sealed record MetadataDocument(
        string? ProfileNickname,
        string? ProfileUid,
        string? LegacySourcePath,
        DateTimeOffset? ImportedAtUtc,
        IReadOnlyDictionary<string, string>? Settings,
        IReadOnlyDictionary<string, string>? IdentityMappings,
        IReadOnlyList<string>? Tombstones,
        IReadOnlyList<string>? TrackedAchievementIds)
    {
        public static MetadataDocument FromMetadata(WorkspaceMetadata metadata) => new(
            metadata.ProfileNickname,
            metadata.ProfileUid,
            metadata.LegacySourcePath,
            metadata.ImportedAtUtc,
            metadata.EffectiveSettings,
            metadata.EffectiveIdentityMappings,
            metadata.EffectiveTombstones.Select(item => item.ToString()).ToArray(),
            metadata.EffectiveTrackedAchievementIds.Select(item => item.ToString()).ToArray());

        public WorkspaceMetadata ToMetadata()
        {
            if (Settings is null || IdentityMappings is null || Tombstones is null)
            {
                throw new InvalidDataException("Native metadata is incomplete.");
            }

            var tombstones = new HashSet<AchievementId>();
            foreach (var item in Tombstones)
            {
                if (!Guid.TryParse(item, out var guid) || !tombstones.Add(new AchievementId(guid)))
                {
                    throw new InvalidDataException("Native metadata contains an invalid tombstone identity.");
                }
            }

            var tracked = new List<AchievementId>();
            foreach (var item in TrackedAchievementIds ?? Array.Empty<string>())
            {
                if (!Guid.TryParse(item, out var guid))
                {
                    throw new InvalidDataException("Native metadata contains an invalid tracked achievement identity.");
                }

                var id = new AchievementId(guid);
                if (!tracked.Contains(id))
                {
                    tracked.Add(id);
                }
                else
                {
                    throw new InvalidDataException("Native metadata contains a duplicate tracked achievement identity.");
                }
            }

            return new WorkspaceMetadata(
                ProfileNickname,
                ProfileUid,
                LegacySourcePath,
                ImportedAtUtc,
                Settings,
                IdentityMappings,
                tombstones,
                tracked);
        }
    }
}
