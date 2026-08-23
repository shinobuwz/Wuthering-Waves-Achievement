using System.Text.Json;

namespace Wuwa.Core;

public sealed record OcrScannedCategory(
    string PrimaryName,
    string SecondaryName,
    DateTimeOffset ScannedAtUtc,
    int Pages = 0);

public sealed record OcrScanHistory(
    bool SkipPreviouslyScanned = true,
    IReadOnlyList<OcrScannedCategory>? Categories = null)
{
    public const string SettingKey = "ocr.scanHistory";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<OcrScannedCategory> EffectiveCategories => Categories ?? Array.Empty<OcrScannedCategory>();

    public static OcrScanHistory FromSettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryGetValue(SettingKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return new OcrScanHistory();
        }

        try
        {
            return (JsonSerializer.Deserialize<OcrScanHistory>(value, JsonOptions) ?? new OcrScanHistory()).Normalize();
        }
        catch (JsonException)
        {
            return new OcrScanHistory();
        }
    }

    public string ToSettingValue() => JsonSerializer.Serialize(Normalize(), JsonOptions);

    public OcrScanHistory Normalize()
    {
        var unique = new Dictionary<string, OcrScannedCategory>(StringComparer.Ordinal);
        foreach (var category in EffectiveCategories)
        {
            if (string.IsNullOrWhiteSpace(category.PrimaryName) || string.IsNullOrWhiteSpace(category.SecondaryName)) continue;
            var normalized = category with
            {
                PrimaryName = category.PrimaryName.Trim(),
                SecondaryName = category.SecondaryName.Trim(),
                Pages = Math.Max(0, category.Pages)
            };
            var key = BuildKey(normalized.PrimaryName, normalized.SecondaryName);
            if (!unique.TryGetValue(key, out var existing) || normalized.ScannedAtUtc >= existing.ScannedAtUtc)
            {
                unique[key] = normalized;
            }
        }

        return this with
        {
            Categories = unique.Values
                .OrderBy(item => item.PrimaryName, StringComparer.Ordinal)
                .ThenBy(item => item.SecondaryName, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public bool Contains(string primaryName, string secondaryName) =>
        EffectiveCategories.Any(item => string.Equals(
            BuildKey(item.PrimaryName, item.SecondaryName),
            BuildKey(primaryName, secondaryName),
            StringComparison.Ordinal));

    public OcrScanHistory Record(
        string primaryName,
        string secondaryName,
        int pages,
        DateTimeOffset? scannedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(primaryName)) throw new ArgumentException("Primary OCR category cannot be blank.", nameof(primaryName));
        if (string.IsNullOrWhiteSpace(secondaryName)) throw new ArgumentException("Secondary OCR category cannot be blank.", nameof(secondaryName));
        var key = BuildKey(primaryName, secondaryName);
        var categories = EffectiveCategories
            .Where(item => !string.Equals(BuildKey(item.PrimaryName, item.SecondaryName), key, StringComparison.Ordinal))
            .Append(new OcrScannedCategory(primaryName.Trim(), secondaryName.Trim(), scannedAtUtc ?? DateTimeOffset.UtcNow, Math.Max(0, pages)))
            .ToArray();
        return (this with { Categories = categories }).Normalize();
    }

    public OcrScanHistory Clear() => this with { Categories = Array.Empty<OcrScannedCategory>() };

    public int PrimaryCategoryCount => EffectiveCategories
        .Select(item => AchievementOcrMatcher.NormalizeName(item.PrimaryName))
        .Distinct(StringComparer.Ordinal)
        .Count();

    private static string BuildKey(string primaryName, string secondaryName) =>
        $"{AchievementOcrMatcher.NormalizeName(primaryName)}\u001f{AchievementOcrMatcher.NormalizeName(secondaryName)}";
}
