using System.Net.Http.Headers;
using System.Text.Json;

namespace Wuwa.Infrastructure;

public sealed record ReleaseInfo(string TagName, string HtmlUrl, string? Name, DateTimeOffset? PublishedAt);

public sealed class GitHubUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;
    private readonly string? _cachePath;

    public GitHubUpdateChecker(string owner = "shinobuwz", string repository = "Wuthering-Waves-Achievement", HttpClient? httpClient = null)
    {
        _owner = owner;
        _repository = repository;
        _cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WutheringWavesAchievement", "update-cache.json");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WutheringWavesAchievement", "native"));
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        var html = root.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(html)) return null;
        DateTimeOffset? published = root.TryGetProperty("published_at", out var publishedValue) && publishedValue.ValueKind != JsonValueKind.Null && DateTimeOffset.TryParse(publishedValue.GetString(), out var parsed) ? parsed : null;
            var result = new ReleaseInfo(tag, html, root.TryGetProperty("name", out var name) ? name.GetString() : null, published);
            await WriteCacheAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (HttpRequestException)
        {
            return await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        }

        async Task<ReleaseInfo?> ReadCacheAsync(CancellationToken token)
        {
            if (_cachePath is null || !File.Exists(_cachePath)) return null;
            await using var cache = File.OpenRead(_cachePath);
            return await JsonSerializer.DeserializeAsync<ReleaseInfo>(cache, cancellationToken: token).ConfigureAwait(false);
        }

        async Task WriteCacheAsync(ReleaseInfo value, CancellationToken token)
        {
            if (_cachePath is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await using var cache = File.Create(_cachePath);
            await JsonSerializer.SerializeAsync(cache, value, cancellationToken: token).ConfigureAwait(false);
        }
    }
}
