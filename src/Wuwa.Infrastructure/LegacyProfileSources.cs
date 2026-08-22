using System.Text.Json;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>Read-only adapter for the legacy resources/config.json and user_progress files.</summary>
public sealed class JsonLegacyProfileSource : ILegacyProfileSource
{
    public async Task<LegacyDiscoveryResult> DiscoverAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Invalid("A legacy config path is required.");
        }

        try
        {
            var config = await ReadObjectAsync(configPath, cancellationToken).ConfigureAwait(false);
            var currentUser = GetString(config, "current_user");
            if (!TryGetObject(config, "users", out var users))
            {
                return Invalid("Legacy config does not contain a users object.");
            }

            var candidates = new List<LegacyProfileCandidate>();
            var nicknames = new HashSet<string>(StringComparer.Ordinal);
            var uids = new HashSet<string>(StringComparer.Ordinal);
            var progressPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var username = user.Name;
                var userData = user.Value.ValueKind == JsonValueKind.Object ? user.Value : default;
                var nickname = GetString(userData, "nickname") ?? username;
                var uid = GetString(userData, "uid") ?? username;
                if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(uid) || !nicknames.Add(nickname))
                {
                    return Invalid("Legacy config contains a blank or duplicate nickname.");
                }
                if (!uids.Add(uid))
                {
                    return Invalid("Legacy config contains a duplicate UID.");
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
                var progressPath = Path.Combine(directory, $"user_progress_{uid}.json");
                if (!progressPaths.Add(Path.GetFullPath(progressPath)))
                {
                    return Invalid("Legacy config contains an ambiguous progress path.");
                }
                if (!File.Exists(progressPath))
                {
                    continue;
                }

                try
                {
                    var progress = await ReadObjectAsync(progressPath, cancellationToken).ConfigureAwait(false);
                    candidates.Add(new LegacyProfileCandidate(
                        nickname,
                        uid,
                        progressPath,
                        progress.EnumerateObject().Count(property => property.Value.ValueKind == JsonValueKind.Object),
                        username));
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
                {
                    return Invalid($"Legacy progress file is invalid: {progressPath}: {exception.Message}");
                }
            }

            if (candidates.Count == 0)
            {
                return new LegacyDiscoveryResult(LegacyDiscoveryStatus.NoCandidates, candidates, currentUser);
            }

            var currentCandidate = candidates.FirstOrDefault(item =>
                string.Equals(item.Username, currentUser, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(currentUser) || currentCandidate is null)
            {
                return new LegacyDiscoveryResult(LegacyDiscoveryStatus.InvalidCurrentUser, candidates, currentUser);
            }

            return candidates.Count == 1
                ? new LegacyDiscoveryResult(LegacyDiscoveryStatus.Unambiguous, candidates, currentUser)
                : new LegacyDiscoveryResult(LegacyDiscoveryStatus.RequiresSelection, candidates, currentUser);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Invalid($"Legacy config is corrupt or structurally invalid: {exception.Message}");
        }
        catch (IOException exception)
        {
            return Invalid(exception.Message);
        }
    }

    public async Task<LegacyProfileProgress> ReadProgressAsync(LegacyProfileCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var document = await ReadObjectAsync(candidate.ProgressPath, cancellationToken).ConfigureAwait(false);
        var statuses = new Dictionary<string, ProgressStatus>(StringComparer.Ordinal);
        foreach (var property in document.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.Value.ValueKind != JsonValueKind.Object || !TryGetProperty(property.Value, ["获取状态", "status", "Status"], out var statusValue))
            {
                throw new InvalidDataException($"Legacy progress entry '{property.Name}' has no status object.");
            }

            var text = statusValue.ValueKind == JsonValueKind.String ? statusValue.GetString() : statusValue.ToString();
            if (!ProgressStatusText.TryParseChinese(text?.Trim(), out var status))
            {
                throw new InvalidDataException($"Legacy progress entry '{property.Name}' contains an unknown status.");
            }

            statuses[property.Name] = status;
        }

        return new LegacyProfileProgress(candidate, statuses);
    }

    private static async Task<JsonElement> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Legacy JSON document must be an object: {path}");
        }
        return document.RootElement.Clone();
    }

    private static LegacyDiscoveryResult Invalid(string message) => new(
        LegacyDiscoveryStatus.Invalid,
        Array.Empty<LegacyProfileCandidate>(),
        Error: new WorkspaceError(WorkspaceErrorCode.LegacyDiscoveryFailed, message));

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString().Trim()
            : null;

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetProperty(JsonElement element, IReadOnlyList<string> names, out JsonElement value)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value)) return true;
        }
        value = default;
        return false;
    }
}
