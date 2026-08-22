using System.Globalization;
using System.Text.Json;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>
/// Reads the immutable achievement and category resources shipped with the native application.
/// File access is deliberately kept in this adapter; the WPF layer only sees the workspace seam.
/// </summary>
public sealed class ShippedJsonAchievementLibrarySource : IAchievementLibrarySource
{
    private static readonly string[] AbsoluteOrderNames = ["绝对编号", "absoluteOrder", "AbsoluteOrder"];
    private static readonly string[] VersionNames = ["版本", "version", "Version"];
    private static readonly string[] FirstCategoryNames = ["第一分类", "firstCategory", "FirstCategory"];
    private static readonly string[] SecondCategoryNames = ["第二分类", "secondCategory", "SecondCategory"];
    private static readonly string[] LegacyCodeNames = ["编号", "legacyCode", "LegacyCode"];
    private static readonly string[] NameNames = ["名称", "name", "Name"];
    private static readonly string[] DescriptionNames = ["描述", "description", "Description"];
    private static readonly string[] RewardNames = ["奖励", "reward", "Reward"];
    private static readonly string[] HiddenNames = ["是否隐藏", "isHidden", "IsHidden"];
    private static readonly string[] GroupNames = ["成就组ID", "groupId", "GroupId"];

    private readonly string _achievementsPath;
    private readonly string _categoryConfigPath;

    public ShippedJsonAchievementLibrarySource(string achievementsPath, string categoryConfigPath)
    {
        _achievementsPath = RequirePath(achievementsPath, nameof(achievementsPath));
        _categoryConfigPath = RequirePath(categoryConfigPath, nameof(categoryConfigPath));
    }

    public static ShippedJsonAchievementLibrarySource FromApplicationDirectory(string? applicationDirectory = null)
    {
        var start = new DirectoryInfo(applicationDirectory ?? AppContext.BaseDirectory);
        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            var resources = Path.Combine(directory.FullName, "resources");
            var achievements = Path.Combine(resources, "base_achievements.json");
            var categories = Path.Combine(resources, "category_config.json");
            if (File.Exists(achievements) && File.Exists(categories))
            {
                return new ShippedJsonAchievementLibrarySource(achievements, categories);
            }
        }

        throw new FileNotFoundException(
            "The shipped achievement resources could not be found near the application directory.",
            Path.Combine(start.FullName, "resources"));
    }

    public async Task<AchievementLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var achievementsStream = new FileStream(
            _achievementsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var categoryStream = new FileStream(
            _categoryConfigPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var achievementsDocument = await JsonDocument.ParseAsync(achievementsStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var categoryDocument = await JsonDocument.ParseAsync(categoryStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (achievementsDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The shipped achievement resource must contain a JSON array.");
        }

        var achievements = new List<Achievement>(achievementsDocument.RootElement.GetArrayLength());
        foreach (var element in achievementsDocument.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            achievements.Add(ParseAchievement(element));
        }

        return new AchievementLibrary(achievements, ParseCategories(categoryDocument.RootElement));
    }

    private static Achievement ParseAchievement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Every shipped achievement row must be a JSON object.");
        }

        var legacyCode = RequiredString(element, LegacyCodeNames);
        var absoluteOrder = RequiredInt(element, AbsoluteOrderNames);
        var version = RequiredString(element, VersionNames);
        var firstCategory = RequiredString(element, FirstCategoryNames);
        var secondCategory = RequiredString(element, SecondCategoryNames);
        var name = RequiredString(element, NameNames);
        var description = RequiredString(element, DescriptionNames);
        var reward = RequiredString(element, RewardNames);
        var isHidden = ReadHidden(element, HiddenNames);
        var groupId = OptionalString(element, GroupNames);
        var mutualCodes = OptionalString(element, ["互斥成就", "mutualExclusionCodes", "MutualExclusionCodes"])?
            .Split([',', ';', '，', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();

        return new Achievement(
            AchievementId.FromLegacyCode(legacyCode),
            legacyCode,
            absoluteOrder,
            version,
            firstCategory,
            secondCategory,
            name,
            description,
            reward,
            isHidden,
            string.IsNullOrWhiteSpace(groupId) ? null : groupId,
            MutualExclusionCodes: mutualCodes);
    }

    private static CategoryCatalog ParseCategories(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The shipped category resource must contain a JSON object.");
        }

        var firstCategories = new Dictionary<string, int>(StringComparer.Ordinal);
        if (TryGetProperty(root, ["first_categories", "firstCategories", "FirstCategories"], out var first) &&
            first.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in first.EnumerateObject())
            {
                firstCategories[RequireNonBlank(property.Name, "first category name")] = ParseInt(property.Value, $"first category '{property.Name}' order");
            }
        }

        var secondCategories = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        if (TryGetProperty(root, ["second_categories", "secondCategories", "SecondCategories"], out var second) &&
            second.ValueKind == JsonValueKind.Object)
        {
            foreach (var firstProperty in second.EnumerateObject())
            {
                if (firstProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"Second category configuration for '{firstProperty.Name}' must be an object.");
                }

                var orders = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var secondProperty in firstProperty.Value.EnumerateObject())
                {
                    orders[RequireNonBlank(secondProperty.Name, "second category name")] = ParseInt(
                        secondProperty.Value,
                        $"second category '{secondProperty.Name}' order");
                }

                secondCategories[RequireNonBlank(firstProperty.Name, "first category name")] = orders;
            }
        }

        return new CategoryCatalog(firstCategories, secondCategories);
    }

    private static bool ReadHidden(JsonElement element, IReadOnlyList<string> names)
    {
        if (!TryGetProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        var text = value.ToString().Trim();
        return text is "1" or "true" or "True" or "TRUE" or "是" or "隐藏";
    }

    private static int RequiredInt(JsonElement element, IReadOnlyList<string> names)
    {
        if (!TryGetProperty(element, names, out var value))
        {
            throw new InvalidDataException($"Achievement row is missing '{names[0]}'.");
        }

        return ParseInt(value, names[0]);
    }

    private static int ParseInt(JsonElement value, string fieldName)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        var text = value.ToString().Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidDataException($"Field '{fieldName}' must be an integer.");
    }

    private static string RequiredString(JsonElement element, IReadOnlyList<string> names)
    {
        var value = OptionalString(element, names);
        return RequireNonBlank(value, names[0]);
    }

    private static string? OptionalString(JsonElement element, IReadOnlyList<string> names)
    {
        if (!TryGetProperty(element, names, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString().Trim();
    }

    private static string RequireNonBlank(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Field '{fieldName}' must not be blank.")
            : value.Trim();

    private static bool TryGetProperty(JsonElement element, IReadOnlyList<string> names, out JsonElement value)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string RequirePath(string path, string parameterName) =>
        string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A resource path is required.", parameterName)
            : path;
}

public sealed class FixedAchievementLibrarySource : IAchievementLibrarySource
{
    private readonly AchievementLibrary _library;

    public FixedAchievementLibrarySource(AchievementLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public Task<AchievementLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_library);
    }
}

public sealed class InMemoryAppDataStore : IAppDataStore
{
    private WorkspaceState? _state;

    public Task<WorkspaceState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_state);
    }

    public Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        _state = state;
        return Task.CompletedTask;
    }
}
