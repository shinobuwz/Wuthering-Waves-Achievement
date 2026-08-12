using System.Text.Json;
using System.Text.Json.Serialization;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

public sealed class JsonAppDataStore : IAppDataStore
{
    private const int SchemaVersion = 1;
    private const string ManifestFileName = "current.json";
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAppDataStore(string? rootDirectory = null, int retainedGenerations = 3)
    {
        _root = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WutheringWavesAchievement")
            : Path.GetFullPath(rootDirectory);
        RetainedGenerations = Math.Max(3, retainedGenerations);
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
                var recovered = await FindNewestValidGenerationAsync(cancellationToken).ConfigureAwait(false);
                if (recovered is null) return null;
                var recoveryManifest = Path.Combine(_root, $".{ManifestFileName}.recovery-{Guid.NewGuid():N}.tmp");
                await WriteJsonAsync(recoveryManifest, new ManifestDocument(SchemaVersion, recovered.Value.Name), cancellationToken).ConfigureAwait(false);
                ReplaceFileAtomically(recoveryManifest, ManifestPath);
                return recovered.Value.State;
            }

            ManifestDocument? manifest = null;
            try
            {
                manifest = await ReadJsonAsync<ManifestDocument>(ManifestPath, cancellationToken).ConfigureAwait(false);
                if (manifest.SchemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(manifest.Generation)) throw new InvalidDataException("Unsupported manifest.");
                return await ReadGenerationAsync(manifest.Generation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                var recovered = await FindNewestValidGenerationAsync(cancellationToken).ConfigureAwait(false);
                if (recovered is not null)
                {
                    var recoveryManifest = Path.Combine(_root, $".{ManifestFileName}.recovery-{Guid.NewGuid():N}.tmp");
                    await WriteJsonAsync(recoveryManifest, new ManifestDocument(SchemaVersion, recovered.Value.Name), cancellationToken).ConfigureAwait(false);
                    ReplaceFileAtomically(recoveryManifest, ManifestPath);
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

            try
            {
                var document = StateDocument.FromState(state, SchemaVersion);
                await WriteJsonAsync(Path.Combine(temporaryDirectory, "state.json"), document, cancellationToken).ConfigureAwait(false);
                await using (var stream = new FileStream(Path.Combine(temporaryDirectory, "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
                {
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var validated = await ReadStateDocumentAsync(Path.Combine(temporaryDirectory, "state.json"), cancellationToken).ConfigureAwait(false);
                ValidateState(validated.ToState());
                Directory.Move(temporaryDirectory, finalDirectory);

                var manifestPath = Path.Combine(_root, $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");
                await WriteJsonAsync(manifestPath, new ManifestDocument(SchemaVersion, generationName), cancellationToken).ConfigureAwait(false);
                ReplaceFileAtomically(manifestPath, ManifestPath);
                await PruneGenerationsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDeleteDirectory(temporaryDirectory);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Name, WorkspaceState State)?> FindNewestValidGenerationAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, "generations");
        if (!Directory.Exists(directory)) return null;
        foreach (var path in Directory.EnumerateDirectories(directory, "generation-*").OrderByDescending(item => item, StringComparer.Ordinal))
        {
            try
            {
                var name = Path.GetFileName(path);
                var state = await ReadGenerationAsync(name, cancellationToken).ConfigureAwait(false);
                ValidateState(state);
                return (name, state);
            }
            catch (IOException) { }
            catch (JsonException) { }
            catch (InvalidDataException) { }
        }
        return null;
    }

    private async Task<WorkspaceState> ReadGenerationAsync(string generationName, CancellationToken cancellationToken)
    {
        if (generationName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || generationName.Contains(Path.DirectorySeparatorChar))
        {
            throw new InvalidDataException("The native manifest contains an invalid generation reference.");
        }

        var path = Path.Combine(_root, "generations", generationName, "state.json");
        var document = await ReadStateDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        if (document.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException("The native generation schema is not supported.");
        }

        return document.ToState();
    }

    private async Task PruneGenerationsAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, "generations");
        var manifest = await ReadJsonAsync<ManifestDocument>(ManifestPath, cancellationToken).ConfigureAwait(false);
        var valid = new List<(string Name, DateTime LastWriteUtc)>();
        foreach (var path in Directory.EnumerateDirectories(directory, "generation-*"))
        {
            var name = Path.GetFileName(path);
            try
            {
                var state = await ReadGenerationAsync(name, cancellationToken).ConfigureAwait(false);
                ValidateState(state);
                valid.Add((name, Directory.GetLastWriteTimeUtc(path)));
            }
            catch
            {
                // Keep invalid generations for forensic recovery; they are never selected.
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
            cancellationToken.ThrowIfCancellationRequested();
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

            if (!state.Statuses.TryGetValue(achievement.Id, out var status) || !Enum.IsDefined(status))
            {
                throw new InvalidDataException($"Achievement {achievement.LegacyCode} has no valid progress status.");
            }
        }
    }

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
        if (OperatingSystem.IsWindows() && File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
        }
        else
        {
            File.Move(temporaryPath, destinationPath, true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { }
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
        IReadOnlyList<AchievementDocument> Achievements,
        IReadOnlyDictionary<string, string> Statuses,
        CategoryDocument Categories,
        MetadataDocument Metadata)
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
            var achievements = Achievements.Select(item => item.ToAchievement()).ToArray();
            var statuses = new Dictionary<AchievementId, ProgressStatus>();
            foreach (var item in Statuses)
            {
                if (!Guid.TryParse(item.Key, out var guid) || !ProgressStatusText.TryParseChinese(item.Value, out var status))
                {
                    throw new InvalidDataException("Native state contains an invalid identity or status.");
                }
                statuses[new AchievementId(guid)] = status;
            }

            return new WorkspaceState(Revision, achievements, statuses, Categories.ToCatalog(), Metadata.ToMetadata());
        }
    }

    private sealed record AchievementDocument(
        string Id,
        string LegacyCode,
        int AbsoluteOrder,
        string Version,
        string FirstCategory,
        string SecondCategory,
        string Name,
        string Description,
        string Reward,
        bool IsHidden,
        string? GroupId,
        string? WikiSourceRef,
        bool IsTombstone,
        IReadOnlyList<string>? MutualExclusionCodes)
    {
        public static AchievementDocument FromAchievement(Achievement item) => new(item.Id.ToString(), item.LegacyCode, item.AbsoluteOrder, item.Version, item.FirstCategory, item.SecondCategory, item.Name, item.Description, item.Reward, item.IsHidden, item.GroupId, item.WikiSourceRef, item.IsTombstone, item.EffectiveMutualExclusionCodes);
        public Achievement ToAchievement() => !Guid.TryParse(Id, out var guid) ? throw new InvalidDataException("Invalid achievement identity.") : new(new AchievementId(guid), LegacyCode, AbsoluteOrder, Version, FirstCategory, SecondCategory, Name, Description, Reward, IsHidden, GroupId, WikiSourceRef, IsTombstone, MutualExclusionCodes);
    }

    private sealed record CategoryDocument(IReadOnlyDictionary<string, int> First, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Second)
    {
        public static CategoryDocument FromCatalog(CategoryCatalog catalog) => new(catalog.FirstCategories, catalog.SecondCategories);
        public CategoryCatalog ToCatalog() => new(First, Second);
    }

    private sealed record MetadataDocument(string? ProfileNickname, string? ProfileUid, string? LegacySourcePath, DateTimeOffset? ImportedAtUtc, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> IdentityMappings, IReadOnlyList<string> Tombstones)
    {
        public static MetadataDocument FromMetadata(WorkspaceMetadata metadata) => new(metadata.ProfileNickname, metadata.ProfileUid, metadata.LegacySourcePath, metadata.ImportedAtUtc, metadata.EffectiveSettings, metadata.EffectiveIdentityMappings, metadata.EffectiveTombstones.Select(item => item.ToString()).ToArray());
        public WorkspaceMetadata ToMetadata() => new(
            ProfileNickname,
            ProfileUid,
            LegacySourcePath,
            ImportedAtUtc,
            Settings,
            IdentityMappings,
            Tombstones
                .Where(item => Guid.TryParse(item, out _))
                .Select(item => new AchievementId(Guid.Parse(item)))
                .ToHashSet());
    }
}
