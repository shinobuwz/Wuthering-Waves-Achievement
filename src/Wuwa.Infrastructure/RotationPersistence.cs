using System.Text.Json;
using System.Text.Json.Serialization;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

internal enum RotationStoreCheckpoint { BeforeProfileReplacement, BeforeSettingsReplacement }
internal interface IRotationStoreFaultInjector { void OnCheckpoint(RotationStoreCheckpoint checkpoint); }

public sealed class JsonRotationProfileStore : IRotationProfileStore
{
    private const int SchemaVersion = 1;
    private readonly string _profilesDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = RotationJson.CreateOptions();
    private readonly IRotationStoreFaultInjector? _faultInjector;

    public JsonRotationProfileStore(string? dataRoot = null) : this(dataRoot, null) { }

    internal JsonRotationProfileStore(string? dataRoot, IRotationStoreFaultInjector? faultInjector)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? AppPaths.DataDirectory : Path.GetFullPath(dataRoot);
        _profilesDirectory = Path.Combine(root, "rotations", "profiles");
        _faultInjector = faultInjector;
    }

    public async Task<RotationProfileLoadResult> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_profilesDirectory)) return new(Array.Empty<RotationProfile>(), Array.Empty<RotationIssue>());
            var profiles = new List<RotationProfile>();
            var issues = new List<RotationIssue>();
            foreach (var path in Directory.EnumerateFiles(_profilesDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try { profiles.Add(await ReadAsync(path, cancellationToken).ConfigureAwait(false)); }
                catch (Exception exception) when (RotationJson.IsDataFailure(exception))
                {
                    issues.Add(new("store.profile.invalid", $"无法读取流程 {Path.GetFileName(path)}：{exception.Message}", RotationIssueSeverity.Warning));
                }
            }
            return new(profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCulture).ToArray(), issues.AsReadOnly());
        }
        finally { _gate.Release(); }
    }

    public async Task<RotationProfile?> GetAsync(RotationProfileId id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPath(id);
            return File.Exists(path) ? await ReadAsync(path, cancellationToken).ConfigureAwait(false) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(RotationProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var validation = RotationProfileValidator.Validate(profile);
        if (!validation.IsValid) throw new InvalidDataException(string.Join("; ", validation.Errors.Select(issue => issue.Message)));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_profilesDirectory);
            var destination = GetPath(profile.Id);
            var document = ProfileDocument.FromProfile(profile);
            await RotationJson.WriteAtomicAsync(destination, document, _options, async candidate =>
            {
                var validated = await RotationJson.ReadAsync<ProfileDocument>(candidate, cancellationToken).ConfigureAwait(false);
                var roundTrip = validated.ToProfile();
                if (!RotationProfileValidator.Validate(roundTrip).IsValid) throw new InvalidDataException("Staged rotation profile failed validation.");
            }, cancellationToken, () => _faultInjector?.OnCheckpoint(RotationStoreCheckpoint.BeforeProfileReplacement)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(RotationProfileId id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { File.Delete(GetPath(id)); }
        finally { _gate.Release(); }
    }

    private async Task<RotationProfile> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var document = await RotationJson.ReadAsync<ProfileDocument>(path, cancellationToken).ConfigureAwait(false);
        if (document.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported rotation profile schema.");
        var profile = document.ToProfile();
        var validation = RotationProfileValidator.Validate(profile);
        if (!validation.IsValid) throw new InvalidDataException(string.Join("; ", validation.Errors.Select(issue => issue.Message)));
        return profile;
    }

    private string GetPath(RotationProfileId id)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Rotation profile id is empty.", nameof(id));
        return Path.Combine(_profilesDirectory, $"{id.Value:N}.json");
    }

    private sealed record ProfileDocument(
        int SchemaVersion,
        string? Id,
        string? Name,
        IReadOnlyList<CharacterDocument>? Team,
        int InitialSlot,
        IReadOnlyList<StepDocument>? Opener,
        IReadOnlyList<StepDocument>? Loop)
    {
        public static ProfileDocument FromProfile(RotationProfile profile) => new(
            JsonRotationProfileStore.SchemaVersion,
            profile.Id.Value.ToString("D"),
            profile.Name,
            profile.Team.Select(item => new CharacterDocument(item.Slot, item.CharacterName, item.Alias)).ToArray(),
            profile.InitialSlot,
            profile.Opener.Select(StepDocument.FromStep).ToArray(),
            profile.Loop.Select(StepDocument.FromStep).ToArray());

        public RotationProfile ToProfile()
        {
            if (!Guid.TryParse(Id, out var id) || Name is null || Team is null || Opener is null || Loop is null)
                throw new InvalidDataException("Rotation profile document is incomplete.");
            return new(new(id), Name, Team.Select(item => item.ToCharacter()), InitialSlot, Opener.Select(item => item.ToStep()), Loop.Select(item => item.ToStep()));
        }
    }

    private sealed record CharacterDocument(int Slot, string? CharacterName, string? Alias)
    {
        public RotationCharacterSlot ToCharacter() => string.IsNullOrWhiteSpace(CharacterName)
            ? throw new InvalidDataException("Rotation character name is empty.")
            : new(Slot, CharacterName, Alias);
    }

    private sealed record StepDocument(RotationActionKind Action, string? Description, string? Variant, int? TargetSlot, string? IconReference)
    {
        public static StepDocument FromStep(RotationStep step) => new(step.Action, step.Description, step.Variant, step.TargetSlot, step.IconReference);
        public RotationStep ToStep() => string.IsNullOrWhiteSpace(Description)
            ? throw new InvalidDataException("Rotation step description is empty.")
            : new(Action, Description, Variant, TargetSlot, IconReference);
    }
}

public sealed class JsonRotationSettingsStore : IRotationSettingsStore
{
    private const int SchemaVersion = 1;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = RotationJson.CreateOptions();
    private readonly IRotationStoreFaultInjector? _faultInjector;

    public JsonRotationSettingsStore(string? dataRoot = null) : this(dataRoot, null) { }

    internal JsonRotationSettingsStore(string? dataRoot, IRotationStoreFaultInjector? faultInjector)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? AppPaths.DataDirectory : Path.GetFullPath(dataRoot);
        _settingsPath = Path.Combine(root, "rotations", "settings.json");
        _faultInjector = faultInjector;
    }

    public async Task<RotationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath)) return RotationSettings.Default;
            var document = await RotationJson.ReadAsync<SettingsDocument>(_settingsPath, cancellationToken).ConfigureAwait(false);
            if (document.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported rotation settings schema.");
            return document.ToSettings();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(RotationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.HeavyThreshold <= TimeSpan.Zero) throw new InvalidDataException("Heavy threshold must be positive.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = SettingsDocument.FromSettings(settings);
            await RotationJson.WriteAtomicAsync(_settingsPath, document, _options, async candidate =>
            {
                var validated = await RotationJson.ReadAsync<SettingsDocument>(candidate, cancellationToken).ConfigureAwait(false);
                _ = validated.ToSettings();
            }, cancellationToken, () => _faultInjector?.OnCheckpoint(RotationStoreCheckpoint.BeforeSettingsReplacement)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private sealed record SettingsDocument(
        int SchemaVersion,
        IReadOnlyList<BindingDocument>? Bindings,
        int HeavyThresholdMilliseconds,
        string? SelectedProfileId)
    {
        public static SettingsDocument FromSettings(RotationSettings settings) => new(
            JsonRotationSettingsStore.SchemaVersion,
            settings.Bindings.Bindings.Select(pair => new BindingDocument(pair.Key, pair.Value.Device, pair.Value.Code)).ToArray(),
            checked((int)settings.HeavyThreshold.TotalMilliseconds),
            settings.SelectedProfileId?.Value.ToString("D"));

        public RotationSettings ToSettings()
        {
            if (Bindings is null || HeavyThresholdMilliseconds <= 0) throw new InvalidDataException("Rotation settings are incomplete.");
            RotationProfileId? selected = null;
            if (!string.IsNullOrWhiteSpace(SelectedProfileId))
            {
                if (!Guid.TryParse(SelectedProfileId, out var id)) throw new InvalidDataException("Selected rotation profile id is invalid.");
                selected = new(id);
            }
            return new(new RotationBindingSet(Bindings.Select(item => new RotationBinding(item.Action, new(item.Device, item.Code)))), TimeSpan.FromMilliseconds(HeavyThresholdMilliseconds), selected);
        }
    }

    private sealed record BindingDocument(RotationBindingAction Action, RotationInputDevice Device, int Code);
}

internal static class RotationJson
{
    internal static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, CreateOptions(), cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException($"JSON document '{path}' is empty.");
    }

    internal static async Task WriteAtomicAsync<T>(
        string destination,
        T document,
        JsonSerializerOptions options,
        Func<string, Task> validate,
        CancellationToken cancellationToken,
        Action? beforeReplacement = null)
    {
        var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Destination has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        var committed = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, document, options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            await validate(temporary).ConfigureAwait(false);
            beforeReplacement?.Invoke();
            if (OperatingSystem.IsWindows() && File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination, true);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    internal static bool IsDataFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or ArgumentException;
}
