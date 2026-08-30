using System.Text.Json;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

public sealed class HekiliRotationProfileImporter
{
    public async Task<RotationImportResult> ImportFileAsync(
        string sourcePath,
        string? nativeResourceRoot = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Source path is required.", nameof(sourcePath));
        var fullPath = Path.GetFullPath(sourcePath);
        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Import(document.RootElement, Path.GetFileNameWithoutExtension(fullPath), nativeResourceRoot);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Failure(new("import.read", $"无法读取 Hekili JSON：{exception.Message}"));
        }
    }

    public RotationImportResult Import(JsonElement root, string sourceName, string? nativeResourceRoot = null)
    {
        var issues = new List<RotationIssue>();
        if (root.ValueKind != JsonValueKind.Object)
            return Failure(new("import.root", "Hekili 文件根节点必须是对象。"));

        var name = ReadString(root, "name") ?? sourceName;
        if (string.IsNullOrWhiteSpace(name)) issues.Add(new("import.name", "流程名称不能为空。"));

        var team = ReadTeam(root, issues);
        var aliases = ReadAliases(root, issues);
        team = team.Select(slot => aliases.TryGetValue(slot.Slot, out var alias) ? slot with { Alias = alias } : slot).ToArray();
        var initialSlot = ReadRequiredInt(root, "initial_char_index", issues);
        var opener = ReadSteps(root, "opener_script", nativeResourceRoot, issues);
        var loop = ReadSteps(root, "loop_script", nativeResourceRoot, issues);
        if (issues.Any(issue => issue.Severity == RotationIssueSeverity.Error)) return new(false, null, issues.AsReadOnly());

        var profile = new RotationProfile(RotationProfileId.New(), name!.Trim(), team, initialSlot, opener, loop);
        issues.AddRange(RotationProfileValidator.Validate(profile).Issues);
        return new(!issues.Any(issue => issue.Severity == RotationIssueSeverity.Error), profile, issues.AsReadOnly());
    }

    private static RotationCharacterSlot[] ReadTeam(JsonElement root, ICollection<RotationIssue> issues)
    {
        if (!root.TryGetProperty("team_config", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("import.team", "team_config 必须是对象。"));
            return Array.Empty<RotationCharacterSlot>();
        }
        var result = new List<RotationCharacterSlot>();
        foreach (var property in element.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var slot) || slot is < 1 or > 3 ||
                property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                issues.Add(new("import.team.slot", $"team_config 槽位 '{property.Name}' 无效。"));
                continue;
            }
            result.Add(new(slot, property.Value.GetString()!.Trim()));
        }
        return result.OrderBy(item => item.Slot).ToArray();
    }

    private static Dictionary<int, string?> ReadAliases(JsonElement root, ICollection<RotationIssue> issues)
    {
        var result = new Dictionary<int, string?>();
        if (!root.TryGetProperty("team_aliases", out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return result;
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("import.aliases", "team_aliases 必须是对象。"));
            return result;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var slot) || slot is < 1 or > 3 || property.Value.ValueKind != JsonValueKind.String)
            {
                issues.Add(new("import.alias.slot", $"team_aliases 槽位 '{property.Name}' 无效。"));
                continue;
            }
            result[slot] = string.IsNullOrWhiteSpace(property.Value.GetString()) ? null : property.Value.GetString()!.Trim();
        }
        return result;
    }

    private static RotationStep[] ReadSteps(JsonElement root, string propertyName, string? resourceRoot, ICollection<RotationIssue> issues)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("import.steps", $"{propertyName} 必须是数组。"));
            return Array.Empty<RotationStep>();
        }
        var result = new List<RotationStep>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new("import.step.object", $"{propertyName}[{index}] 必须是对象。"));
                index++;
                continue;
            }
            var token = ReadString(item, "type")?.Trim().ToLowerInvariant();
            if (!TryMapAction(token, out var action))
            {
                issues.Add(new("import.step.action", $"{propertyName}[{index}] 包含未知动作 '{token}'。"));
                index++;
                continue;
            }
            var description = ReadString(item, "desc");
            if (string.IsNullOrWhiteSpace(description))
                issues.Add(new("import.step.description", $"{propertyName}[{index}] 的 desc 不能为空。"));
            int? target = null;
            if (action == RotationActionKind.Intro)
            {
                if (!item.TryGetProperty("next_char", out var targetElement) || !targetElement.TryGetInt32(out var parsed) || parsed is < 1 or > 3)
                    issues.Add(new("import.step.intro", $"{propertyName}[{index}] 的 next_char 无效。"));
                else target = parsed;
            }
            var icon = NormalizeIcon(ReadString(item, "custom_icon"), resourceRoot, propertyName, index, issues);
            result.Add(new(action, description?.Trim() ?? string.Empty, ReadString(item, "variant"), target, icon));
            index++;
        }
        return result.ToArray();
    }

    private static string? NormalizeIcon(string? icon, string? resourceRoot, string phase, int index, ICollection<RotationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var value = icon.Trim();
        var windowsRooted = value.StartsWith("\\\\", StringComparison.Ordinal) ||
                            (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'));
        if (windowsRooted || Path.IsPathRooted(value) || string.IsNullOrWhiteSpace(resourceRoot))
        {
            issues.Add(new("import.icon.stripped", $"{phase}[{index}] 的绝对或不可验证图标路径已移除。", RotationIssueSeverity.Warning));
            return null;
        }
        try
        {
            var root = Path.GetFullPath(resourceRoot);
            var candidate = Path.GetFullPath(Path.Combine(root, value.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate) || !IsImage(candidate))
            {
                issues.Add(new("import.icon.stripped", $"{phase}[{index}] 的越界、不存在或非图像路径已移除。", RotationIssueSeverity.Warning));
                return null;
            }
            return Path.GetRelativePath(root, candidate).Replace('\\', '/');
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            issues.Add(new("import.icon.stripped", $"{phase}[{index}] 的图标路径无法安全归一化，已移除。", RotationIssueSeverity.Warning));
            return null;
        }
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";

    private static bool TryMapAction(string? token, out RotationActionKind action)
    {
        action = token switch
        {
            "basic" => RotationActionKind.Basic,
            "heavy" => RotationActionKind.Heavy,
            "skill" => RotationActionKind.Skill,
            "ult" => RotationActionKind.Liberation,
            "echo" => RotationActionKind.Echo,
            "jump" => RotationActionKind.Jump,
            "dodge" => RotationActionKind.Dodge,
            "execution" => RotationActionKind.Execution,
            "intro" => RotationActionKind.Intro,
            _ => default
        };
        return token is "basic" or "heavy" or "skill" or "ult" or "echo" or "jump" or "dodge" or "execution" or "intro";
    }

    private static int ReadRequiredInt(JsonElement root, string property, ICollection<RotationIssue> issues)
    {
        if (root.TryGetProperty(property, out var element) && element.TryGetInt32(out var value)) return value;
        issues.Add(new("import.integer", $"{property} 必须是整数。"));
        return 0;
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static RotationImportResult Failure(RotationIssue issue) => new(false, null, new[] { issue });
}

public sealed class RotationProfileImportService
{
    private readonly HekiliRotationProfileImporter _importer;
    private readonly IRotationProfileStore _store;
    private readonly string? _nativeResourceRoot;

    public RotationProfileImportService(IRotationProfileStore store, string? nativeResourceRoot = null, HekiliRotationProfileImporter? importer = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _nativeResourceRoot = nativeResourceRoot;
        _importer = importer ?? new HekiliRotationProfileImporter();
    }

    public async Task<RotationImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var result = await _importer.ImportFileAsync(sourcePath, _nativeResourceRoot, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Profile is null) return result;
        await _store.SaveAsync(result.Profile, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
