using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

public sealed class KuroWikiAchievementSource : IWikiAchievementSource
{
    public const string Endpoint = "https://api.kurobbs.com/wiki/core/catalogue/item/getEntryDetail";
    public const string EntryId = "1220879855033786368";
    private readonly HttpClient _httpClient;

    public KuroWikiAchievementSource(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<WikiFetchResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = EntryId })
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Origin", "https://wiki.kurobbs.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://wiki.kurobbs.com/");
        request.Headers.TryAddWithoutValidation("wiki_type", "9");
        request.Headers.TryAddWithoutValidation("User-Agent", "WutheringWavesAchievement/Native");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new WikiFetchResult(false, Array.Empty<Achievement>(), Error: $"Wiki HTTP {(int)response.StatusCode}.", HttpStatusCode: (int)response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var rows = ParseRows(document.RootElement);
            var lastUpdate = document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("lastUpdateTime", out var time) ? time.ToString() : null;
            return new WikiFetchResult(rows.Count > 0, rows, lastUpdate, rows.Count == 0 ? "Wiki response contained no achievement rows." : null, (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new WikiFetchResult(false, Array.Empty<Achievement>(), Error: $"Wiki response could not be parsed: {exception.Message}", HttpStatusCode: (int)response.StatusCode);
        }
    }

    private static IReadOnlyList<Achievement> ParseRows(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("content", out var content) || !content.TryGetProperty("modules", out var modules))
        {
            throw new InvalidDataException("Wiki response is missing data.content.modules.");
        }

        var result = new List<Achievement>();
        foreach (var module in modules.EnumerateArray())
        {
            if (!module.TryGetProperty("components", out var components)) continue;
            foreach (var component in components.EnumerateArray())
            {
                if (!component.TryGetProperty("type", out var type) || type.GetString() != "filter-component" || !component.TryGetProperty("content", out var html)) continue;
                ParseHtmlTable(html.GetString() ?? string.Empty, result);
            }
        }
        return result;
    }

    private static void ParseHtmlTable(string html, ICollection<Achievement> result)
    {
        var rowIndex = 0;
        foreach (Match rowMatch in Regex.Matches(html, "<tr\\b(?<attrs>[^>]*)>(?<body>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = rowMatch.Groups["attrs"].Value;
            var row = rowMatch.Groups["body"].Value;
            var cells = Regex.Matches(row, "<td\\b[^>]*>(?<cell>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Select(match => StripHtml(match.Groups["cell"].Value)).ToArray();
            if (cells.Length < 5) continue;
            var name = cells[0].Replace("「隐藏成就」", string.Empty, StringComparison.Ordinal).Trim();
            var hidden = cells[0].Contains("隐藏成就", StringComparison.Ordinal);
            var second = cells.Length > 2 ? cells[2].Trim() : string.Empty;
            var rowDataIndex = Regex.Match(attrs, "(?:data-row-index|data-index)=\\\"(?<index>[^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups["index"].Value;
            var tableUid = Regex.Match(attrs, "(?:data-table-data-uid|data-uid)=\\\"(?<uid>[^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups["uid"].Value;
            if (string.IsNullOrWhiteSpace(rowDataIndex)) rowDataIndex = rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(tableUid)) tableUid = "unknown-table";
            var sourceRef = $"{EntryId}/{tableUid}/{rowDataIndex}";
            rowIndex++;
            result.Add(new Achievement(
                AchievementId.FromWikiSource(sourceRef),
                $"wiki-{rowIndex:000000}",
                rowIndex,
                cells[1].Trim(),
                string.Empty,
                second,
                name,
                cells[3].Trim(),
                cells[4].Trim(),
                hidden,
                WikiSourceRef: sourceRef,
                MutualExclusionCodes: Array.Empty<string>()));
        }
    }

    private static string StripHtml(string value)
    {
        var withoutTags = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
