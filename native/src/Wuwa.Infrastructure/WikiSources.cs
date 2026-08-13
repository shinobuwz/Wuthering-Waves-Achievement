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
            var root = document.RootElement;
            var success = root.TryGetProperty("success", out var successValue) && successValue.ValueKind == JsonValueKind.True;
            var code = root.TryGetProperty("code", out var codeValue) && codeValue.TryGetInt32(out var parsedCode) ? parsedCode : 0;
            var message = root.TryGetProperty("msg", out var messageValue) ? messageValue.ToString() : string.Empty;
            if (!success || code != 200)
            {
                return new WikiFetchResult(false, Array.Empty<Achievement>(), Error: $"Wiki business response rejected (code {code}): {message}".Trim(), HttpStatusCode: (int)response.StatusCode);
            }

            var rows = ParseRows(root);
            var lastUpdate = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("lastUpdateTime", out var time) ? time.ToString() : null;
            return new WikiFetchResult(rows.Count > 0, rows, lastUpdate, rows.Count == 0 ? "Wiki response contained no achievement rows." : null, (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return new WikiFetchResult(false, Array.Empty<Achievement>(), Error: $"Wiki response could not be parsed: {exception.Message}", HttpStatusCode: (int)response.StatusCode);
        }
    }

    internal static IReadOnlyList<Achievement> ParseRows(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Wiki response is missing data.content.modules.");
        }

        var result = new List<Achievement>();
        foreach (var module in modules.EnumerateArray())
        {
            if (!module.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array) continue;
            foreach (var component in components.EnumerateArray())
            {
                if (!component.TryGetProperty("type", out var type) || type.GetString() != "filter-component" ||
                    !component.TryGetProperty("content", out var html) || html.ValueKind != JsonValueKind.String) continue;
                ParseHtml(html.GetString() ?? string.Empty, result);
            }
        }

        var duplicate = result.GroupBy(item => item.WikiSourceRef, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Wiki content contains duplicate source reference '{duplicate.Key}'.");
        return result;
    }

    private static void ParseHtml(string html, ICollection<Achievement> result)
    {
        foreach (Match detailsMatch in Regex.Matches(html, "<details\\b[^>]*>(?<body>.*?)</details>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var detailsBody = detailsMatch.Groups["body"].Value;
            var summary = Regex.Match(detailsBody, "<summary\\b[^>]*>(?<body>.*?)</summary>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var firstCategory = summary.Success ? StripHtml(summary.Groups["body"].Value) : string.Empty;
            if (string.IsNullOrWhiteSpace(firstCategory)) throw new InvalidDataException("Wiki details section has no first-category summary.");

            foreach (Match tableMatch in Regex.Matches(detailsBody, "<table\\b(?<attrs>[^>]*)>(?<body>.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tableUid = Attribute(tableMatch.Groups["attrs"].Value, "data-uid");
                if (string.IsNullOrWhiteSpace(tableUid)) throw new InvalidDataException("Wiki achievement table has no stable data-uid.");
                ParseTable(tableMatch.Groups["body"].Value, tableUid, firstCategory, result);
            }
        }
    }

    private static void ParseTable(string tableBody, string tableUid, string firstCategory, ICollection<Achievement> result)
    {
        foreach (Match rowMatch in Regex.Matches(tableBody, "<tr\\b(?<attrs>[^>]*)>(?<body>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = rowMatch.Groups["attrs"].Value;
            if (attrs.Contains("data-freeze", StringComparison.OrdinalIgnoreCase)) continue;
            var rawCells = Regex.Matches(rowMatch.Groups["body"].Value, "<td\\b[^>]*>(?<cell>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(match => match.Groups["cell"].Value).ToArray();
            if (rawCells.Length == 0) continue;
            if (rawCells.Length < 5) throw new InvalidDataException("Wiki achievement row does not contain the required columns.");
            var cells = rawCells.Select(StripHtml).ToArray();

            var rowDataIndex = Attribute(attrs, "data-index");
            if (string.IsNullOrWhiteSpace(rowDataIndex)) throw new InvalidDataException("Wiki achievement row has no stable data-index.");
            var sourceRef = $"{EntryId}/{tableUid}/{rowDataIndex}";
            var secondCategory = FilterTag(attrs, "合集") ?? cells[2].Trim();
            if (string.IsNullOrWhiteSpace(cells[1]) || string.IsNullOrWhiteSpace(secondCategory))
            {
                throw new InvalidDataException($"Wiki achievement row '{sourceRef}' has missing required fields.");
            }

            if (HasFilterTag(attrs, "特殊", "二选一"))
            {
                AddChoiceAchievements(rawCells, cells, sourceRef, firstCategory, secondCategory, result);
                continue;
            }

            var name = cells[0].Replace("「隐藏成就」", string.Empty, StringComparison.Ordinal).Trim();
            var hidden = cells[0].Contains("隐藏成就", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cells[3]))
            {
                throw new InvalidDataException($"Wiki achievement row '{sourceRef}' has missing required fields.");
            }

            AddAchievement(
                result,
                sourceRef,
                cells[1].Trim(),
                firstCategory,
                secondCategory,
                name,
                cells[3].Trim(),
                cells[4].Trim(),
                hidden);
        }
    }

    private static void AddChoiceAchievements(
        IReadOnlyList<string> rawCells,
        IReadOnlyList<string> cells,
        string sourceRef,
        string firstCategory,
        string secondCategory,
        ICollection<Achievement> result)
    {
        var names = CellParagraphs(rawCells[0])
            .Where(value => !string.Equals(value, "或", StringComparison.Ordinal))
            .Select(value => value.Replace("「隐藏成就」", string.Empty, StringComparison.Ordinal).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var descriptions = CellParagraphs(rawCells[3])
            .Where(value => !string.Equals(value, "或", StringComparison.Ordinal))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (names.Length < 2 || descriptions.Length != names.Length)
        {
            throw new InvalidDataException($"Wiki choice row '{sourceRef}' must contain matching achievement names and descriptions.");
        }

        var groupId = $"wiki-choice:{sourceRef}";
        var hidden = cells[0].Contains("隐藏成就", StringComparison.Ordinal);
        for (var index = 0; index < names.Length; index++)
        {
            AddAchievement(
                result,
                $"{sourceRef}/choice-{index + 1}",
                cells[1].Trim(),
                firstCategory,
                secondCategory,
                names[index],
                descriptions[index],
                cells[4].Trim(),
                hidden,
                groupId);
        }
    }

    private static void AddAchievement(
        ICollection<Achievement> result,
        string sourceRef,
        string version,
        string firstCategory,
        string secondCategory,
        string name,
        string description,
        string reward,
        bool hidden,
        string? groupId = null)
    {
        result.Add(new Achievement(
            AchievementId.FromWikiSource(sourceRef),
            $"wiki-{result.Count + 1:000000}",
            result.Count + 1,
            version,
            firstCategory,
            secondCategory,
            name,
            description,
            reward,
            hidden,
            groupId,
            WikiSourceRef: sourceRef,
            MutualExclusionCodes: Array.Empty<string>()));
    }

    private static IReadOnlyList<string> CellParagraphs(string value)
    {
        var paragraphs = Regex.Matches(value, "<p\\b[^>]*>(?<body>.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => StripHtml(match.Groups["body"].Value))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        return paragraphs.Length > 0 ? paragraphs : [StripHtml(value)];
    }

    private static bool HasFilterTag(string attributes, string prefix, string value)
    {
        var tags = Attribute(attributes, "data-filter-tag");
        if (string.IsNullOrWhiteSpace(tags)) return false;
        foreach (var tag in System.Net.WebUtility.HtmlDecode(tags).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = tag.IndexOf('-');
            if (separator > 0 &&
                string.Equals(tag[..separator].Trim(), prefix, StringComparison.Ordinal) &&
                string.Equals(tag[(separator + 1)..].Trim(), value, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string? FilterTag(string attributes, string prefix)
    {
        var tags = Attribute(attributes, "data-filter-tag");
        if (string.IsNullOrWhiteSpace(tags)) return null;
        foreach (var tag in System.Net.WebUtility.HtmlDecode(tags).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = tag.IndexOf('-');
            if (separator > 0 && string.Equals(tag[..separator].Trim(), prefix, StringComparison.Ordinal)) return tag[(separator + 1)..].Trim();
        }
        return null;
    }

    private static string Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"(?:^|\\s){Regex.Escape(name)}\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)')", RegexOptions.IgnoreCase);
        return System.Net.WebUtility.HtmlDecode(match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["single"].Value).Trim();
    }

    private static string StripHtml(string value)
    {
        var withoutTags = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
        return System.Net.WebUtility.HtmlDecode(withoutTags).Replace('\u00a0', ' ').Trim();
    }
}
