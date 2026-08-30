using System.Collections.ObjectModel;

namespace Wuwa.Core;

public readonly record struct RotationProfileId(Guid Value)
{
    public static RotationProfileId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public enum RotationActionKind
{
    Basic,
    Heavy,
    Skill,
    Liberation,
    Echo,
    Jump,
    Dodge,
    Execution,
    Intro
}

public sealed record RotationCharacterSlot(int Slot, string CharacterName, string? Alias = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? CharacterName : Alias;
}

public sealed record RotationStep(
    RotationActionKind Action,
    string Description,
    string? Variant = null,
    int? TargetSlot = null,
    string? IconReference = null);

public sealed class RotationProfile
{
    public RotationProfile(
        RotationProfileId id,
        string name,
        IEnumerable<RotationCharacterSlot> team,
        int initialSlot,
        IEnumerable<RotationStep> opener,
        IEnumerable<RotationStep> loop)
    {
        Id = id;
        Name = name;
        Team = Array.AsReadOnly(team.ToArray());
        InitialSlot = initialSlot;
        Opener = Array.AsReadOnly(opener.ToArray());
        Loop = Array.AsReadOnly(loop.ToArray());
    }

    public RotationProfileId Id { get; }
    public string Name { get; }
    public IReadOnlyList<RotationCharacterSlot> Team { get; }
    public int InitialSlot { get; }
    public IReadOnlyList<RotationStep> Opener { get; }
    public IReadOnlyList<RotationStep> Loop { get; }
}

public enum RotationIssueSeverity { Warning, Error }

public sealed record RotationIssue(string Code, string Message, RotationIssueSeverity Severity = RotationIssueSeverity.Error);

public sealed record RotationValidationResult(IReadOnlyList<RotationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != RotationIssueSeverity.Error);
    public IReadOnlyList<RotationIssue> Errors => Issues.Where(issue => issue.Severity == RotationIssueSeverity.Error).ToArray();
    public IReadOnlyList<RotationIssue> Warnings => Issues.Where(issue => issue.Severity == RotationIssueSeverity.Warning).ToArray();
    public static RotationValidationResult Success { get; } = new(Array.Empty<RotationIssue>());
}

public static class RotationProfileValidator
{
    public static RotationValidationResult Validate(RotationProfile? profile)
    {
        var issues = new List<RotationIssue>();
        if (profile is null)
        {
            issues.Add(new("profile.missing", "未选择连招流程。"));
            return new(issues);
        }

        if (profile.Id.Value == Guid.Empty) issues.Add(new("profile.id", "连招流程标识无效。"));
        if (string.IsNullOrWhiteSpace(profile.Name)) issues.Add(new("profile.name", "连招流程名称不能为空。"));
        if (profile.Team.Count is < 1 or > 3) issues.Add(new("profile.team.count", "队伍必须包含 1 至 3 个角色槽位。"));

        var slots = new HashSet<int>();
        foreach (var character in profile.Team)
        {
            if (character.Slot is < 1 or > 3 || !slots.Add(character.Slot))
                issues.Add(new("profile.team.slot", $"角色槽位 {character.Slot} 非法或重复。"));
            if (string.IsNullOrWhiteSpace(character.CharacterName))
                issues.Add(new("profile.team.name", $"角色槽位 {character.Slot} 的名称不能为空。"));
        }

        if (!slots.Contains(profile.InitialSlot))
            issues.Add(new("profile.initialSlot", "初始角色槽位不在队伍配置中。"));
        if (profile.Opener.Count == 0 && profile.Loop.Count == 0)
            issues.Add(new("profile.steps.empty", "Opener 和 Loop 不能同时为空。"));

        ValidateSteps(profile.Opener, "opener", slots, issues);
        ValidateSteps(profile.Loop, "loop", slots, issues);
        return new(issues.AsReadOnly());
    }

    private static void ValidateSteps(
        IReadOnlyList<RotationStep> steps,
        string phase,
        IReadOnlySet<int> slots,
        ICollection<RotationIssue> issues)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (!Enum.IsDefined(step.Action))
                issues.Add(new("profile.step.action", $"{phase}[{index}] 动作类型无效。"));
            if (string.IsNullOrWhiteSpace(step.Description))
                issues.Add(new("profile.step.description", $"{phase}[{index}] 描述不能为空。"));
            if (step.Action == RotationActionKind.Intro)
            {
                if (step.TargetSlot is null || !slots.Contains(step.TargetSlot.Value))
                    issues.Add(new("profile.step.intro", $"{phase}[{index}] 的 Intro 目标槽位无效。"));
            }
            else if (step.TargetSlot is not null)
            {
                issues.Add(new("profile.step.target", $"{phase}[{index}] 只有 Intro 动作可以指定目标槽位。"));
            }

            if (!string.IsNullOrWhiteSpace(step.IconReference) && IsUnsafeIconReference(step.IconReference))
            {
                issues.Add(new("profile.step.icon", $"{phase}[{index}] 的图标引用必须是安全相对路径。"));
            }
        }
    }

    private static bool IsUnsafeIconReference(string value) =>
        value.StartsWith("/", StringComparison.Ordinal) ||
        value.StartsWith("\\", StringComparison.Ordinal) ||
        (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':') ||
        value.Split('/', '\\').Any(segment => segment == "..");
}

public sealed record RotationProfileLoadResult(
    IReadOnlyList<RotationProfile> Profiles,
    IReadOnlyList<RotationIssue> Issues);

public interface IRotationProfileStore
{
    Task<RotationProfileLoadResult> ListAsync(CancellationToken cancellationToken = default);
    Task<RotationProfile?> GetAsync(RotationProfileId id, CancellationToken cancellationToken = default);
    Task SaveAsync(RotationProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(RotationProfileId id, CancellationToken cancellationToken = default);
}

public interface IRotationSettingsStore
{
    Task<RotationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RotationSettings settings, CancellationToken cancellationToken = default);
}

public sealed record RotationImportResult(
    bool IsSuccess,
    RotationProfile? Profile,
    IReadOnlyList<RotationIssue> Issues)
{
    public IReadOnlyList<RotationIssue> Errors => Issues.Where(issue => issue.Severity == RotationIssueSeverity.Error).ToArray();
    public IReadOnlyList<RotationIssue> Warnings => Issues.Where(issue => issue.Severity == RotationIssueSeverity.Warning).ToArray();
}
