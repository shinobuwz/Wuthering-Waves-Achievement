using System.Text.Json;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

public sealed class JsonAchievementExchange : IAchievementImportSource, IAchievementExportSink
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["绝对编号"] = "absoluteOrder", ["版本"] = "version", ["第一分类"] = "firstCategory", ["第二分类"] = "secondCategory",
        ["编号"] = "legacyCode", ["名称"] = "name", ["描述"] = "description", ["奖励"] = "reward", ["是否隐藏"] = "isHidden",
        ["获取状态"] = "status", ["成就组ID"] = "groupId", ["互斥成就"] = "mutualExclusionCodes"
    };
    private readonly string _path;

    public JsonAchievementExchange(string path)
    {
        _path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("A JSON path is required.", nameof(path)) : Path.GetFullPath(path);
    }

    public async Task<ExchangePayload> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            var progress = new Dictionary<string, ProgressStatus>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object || !TryGetStatus(item.Value, out var status))
                    throw new InvalidDataException($"Progress JSON entry '{item.Name}' has an invalid 获取状态/status.");
                progress[item.Name] = status;
            }
            return new ExchangePayload(ExchangeDocumentKind.ProgressJson, Array.Empty<Achievement>(), progress);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Supported full JSON exchange must be an object or array.");

        var achievements = new List<Achievement>();
        var statuses = new Dictionary<string, ProgressStatus>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Full JSON rows must be objects.");
            var code = Required(item, "编号");
            var statusText = Optional(item, "获取状态");
            if (!string.IsNullOrWhiteSpace(statusText)) statuses[code] = ParseStatus(statusText, code);
            achievements.Add(new Achievement(
                AchievementId.FromLegacyCode(code), code,
                int.TryParse(Optional(item, "绝对编号"), out var order) ? order : achievements.Count + 1,
                Required(item, "版本"), Required(item, "第一分类"), Required(item, "第二分类"), Required(item, "名称"), Required(item, "描述"),
                Optional(item, "奖励"), ParseHidden(item), NullIfBlank(Optional(item, "成就组ID")),
                MutualExclusionCodes: ParseMutualCodes(item, "互斥成就")));
        }
        return new ExchangePayload(ExchangeDocumentKind.FullJson, achievements, statuses);
    }

    public async Task WriteAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var rows = state.Achievements.Select(item => new Dictionary<string, object?>
        {
            ["绝对编号"] = item.AbsoluteOrder, ["版本"] = item.Version, ["第一分类"] = item.FirstCategory, ["第二分类"] = item.SecondCategory,
            ["编号"] = item.LegacyCode, ["名称"] = item.Name, ["描述"] = item.Description, ["奖励"] = item.Reward,
            ["是否隐藏"] = item.IsHidden ? "隐藏" : "", ["获取状态"] = state.Statuses[item.Id].ToChinese(), ["成就组ID"] = item.GroupId ?? "",
            ["互斥成就"] = string.Join(",", item.EffectiveMutualExclusionCodes)
        }).ToArray();
        await using var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, rows, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryGetStatus(JsonElement item, out ProgressStatus status)
    {
        status = default;
        var value = GetAliased(item, "获取状态", required: false);
        return value is not null && ProgressStatusText.TryParseChinese(ToText(value.Value), out status);
    }

    private static string Required(JsonElement item, string chineseName) => NullIfBlank(Optional(item, chineseName)) ?? throw new InvalidDataException($"Required field '{chineseName}/{Aliases[chineseName]}' is missing.");
    private static string Optional(JsonElement item, string chineseName) => GetAliased(item, chineseName, required: false) is { } value ? ToText(value) : string.Empty;

    private static JsonElement? GetAliased(JsonElement item, string chineseName, bool required)
    {
        var hasChinese = item.TryGetProperty(chineseName, out var chinese);
        var englishName = Aliases[chineseName];
        var hasEnglish = item.TryGetProperty(englishName, out var english);
        if (hasChinese && hasEnglish && !JsonValuesEqual(chinese, english)) throw new InvalidDataException($"Fields '{chineseName}' and '{englishName}' conflict.");
        if (hasChinese) return chinese;
        if (hasEnglish) return english;
        if (required) throw new InvalidDataException($"Required field '{chineseName}/{englishName}' is missing.");
        return null;
    }

    private static bool JsonValuesEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == right.ValueKind) return left.ToString().Trim() == right.ToString().Trim();
        return string.Equals(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ParseHidden(JsonElement item)
    {
        var value = GetAliased(item, "是否隐藏", required: false);
        if (value is null) return false;
        return value.Value.ValueKind == JsonValueKind.True || ToText(value.Value) is "隐藏" or "是" or "true" or "True" or "1";
    }

    private static IReadOnlyList<string> ParseMutualCodes(JsonElement item, string chineseName)
    {
        var value = GetAliased(item, chineseName, required: false);
        if (value is null) return Array.Empty<string>();
        if (value.Value.ValueKind == JsonValueKind.Array)
            return value.Value.EnumerateArray().Select(ToText).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        return ToText(value.Value).Split([',', ';', '，', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ToText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : value.ToString().Trim();
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static ProgressStatus ParseStatus(string value, string code) => ProgressStatusText.TryParseChinese(value, out var status) ? status : throw new InvalidDataException($"Achievement '{code}' has an invalid status.");
}
