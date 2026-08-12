using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Wuwa.Infrastructure;

public sealed record ReleaseInfo(string TagName, string HtmlUrl, string? Name, DateTimeOffset? PublishedAt);

public enum UpdateCheckStatus
{
    UpdateAvailable,
    Current,
    DevelopmentBuild,
    Unavailable
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, Version? CurrentVersion, Version? LatestVersion, ReleaseInfo? Release, bool IsCached = false, string? Error = null);

public sealed class GitHubUpdateChecker
{
    private const int CacheSchema = 1;
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;
    private readonly string _cachePath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _maximumFallbackAge;

    public GitHubUpdateChecker(
        string owner = "shinobuwz",
        string repository = "Wuthering-Waves-Achievement",
        HttpClient? httpClient = null,
        string? cachePath = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? cacheTtl = null,
        TimeSpan? maximumFallbackAge = null)
    {
        _owner = owner;
        _repository = repository;
        _cachePath = string.IsNullOrWhiteSpace(cachePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WutheringWavesAchievement", "update-cache.json")
            : Path.GetFullPath(cachePath);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _cacheTtl = cacheTtl ?? TimeSpan.FromHours(12);
        _maximumFallbackAge = maximumFallbackAge ?? TimeSpan.FromDays(7);
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WutheringWavesAchievement", "native"));
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string InstalledVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckAsync(string? currentVersion = null, CancellationToken cancellationToken = default)
    {
        currentVersion ??= InstalledVersion;
        var current = ParseVersion(currentVersion);
        var cache = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cache is not null && CacheMatches(cache, currentVersion) && CacheAge(cache) <= _cacheTtl)
            return Evaluate(current, cache.Release!, true);

        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return CacheFallback(cache, currentVersion, current, $"GitHub HTTP {(int)response.StatusCode}.");
            var release = ParseRelease(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var evaluated = Evaluate(current, release, false);
            try { await WriteCacheAsync(new CacheDocument(CacheSchema, _owner, _repository, currentVersion, _clock(), release), cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return evaluated with { Error = $"Update cache could not be written: {exception.Message}" }; }
            return evaluated;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CacheFallback(cache, currentVersion, current, "GitHub update check timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return CacheFallback(cache, currentVersion, current, exception.Message);
        }
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default) =>
        (await CheckAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Release;

    public bool IsTrustedReleaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var prefix = $"/{_owner}/{_repository}/releases/";
        return uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private UpdateCheckResult CacheFallback(CacheDocument? cache, string currentVersionText, Version? current, string error) =>
        cache is not null && CacheMatches(cache, currentVersionText) && CacheAge(cache) <= _maximumFallbackAge
            ? Evaluate(current, cache.Release!, true) with { Error = error }
            : new UpdateCheckResult(UpdateCheckStatus.Unavailable, current, null, null, Error: error);

    private TimeSpan CacheAge(CacheDocument cache)
    {
        var age = _clock() - cache.CheckedAtUtc;
        return age < TimeSpan.Zero ? TimeSpan.MaxValue : age;
    }

    private bool CacheMatches(CacheDocument cache, string currentVersion) =>
        cache.SchemaVersion == CacheSchema && cache.Release is not null &&
        string.Equals(cache.Owner, _owner, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(cache.Repository, _repository, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(cache.CurrentVersion, currentVersion, StringComparison.Ordinal) &&
        IsTrustedReleaseUrl(cache.Release.HtmlUrl) && ParseVersion(cache.Release.TagName) is not null;

    private UpdateCheckResult Evaluate(Version? current, ReleaseInfo release, bool cached)
    {
        var latest = ParseVersion(release.TagName);
        if (current is null || latest is null) return new UpdateCheckResult(UpdateCheckStatus.DevelopmentBuild, current, latest, release, cached, "A semantic version comparison was not possible.");
        var status = latest > current ? UpdateCheckStatus.UpdateAvailable : latest == current ? UpdateCheckStatus.Current : UpdateCheckStatus.DevelopmentBuild;
        return new UpdateCheckResult(status, current, latest, release, cached);
    }

    private ReleaseInfo ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        var html = root.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(html)) throw new InvalidDataException("GitHub release response is missing tag_name or html_url.");
        if (!IsTrustedReleaseUrl(html)) throw new InvalidDataException("GitHub release response contains an untrusted release URL.");
        DateTimeOffset? published = root.TryGetProperty("published_at", out var publishedValue) && publishedValue.ValueKind != JsonValueKind.Null && DateTimeOffset.TryParse(publishedValue.GetString(), out var parsed) ? parsed : null;
        return new ReleaseInfo(tag, html, root.TryGetProperty("name", out var name) ? name.GetString() : null, published);
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        normalized = normalized.Split(['+', '-'], 2)[0];
        return Version.TryParse(normalized, out var parsed) ? parsed : null;
    }

    private async Task<CacheDocument?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath)) return null;
        try
        {
            await using var cache = File.OpenRead(_cachePath);
            return await JsonSerializer.DeserializeAsync<CacheDocument>(cache, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(CacheDocument value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var cache = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(cache, value, cancellationToken: cancellationToken).ConfigureAwait(false);
                await cache.FlushAsync(cancellationToken).ConfigureAwait(false);
                cache.Flush(true);
            }
            File.Move(temporary, _cachePath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private sealed record CacheDocument(int SchemaVersion, string? Owner, string? Repository, string? CurrentVersion, DateTimeOffset CheckedAtUtc, ReleaseInfo? Release);
}
