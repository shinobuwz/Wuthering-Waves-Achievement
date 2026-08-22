using System.Security.Cryptography;
using System.Text;

namespace Wuwa.Core;

public readonly record struct AchievementId(Guid Value)
{
    private static readonly Guid LegacyNamespace = new("fdc3d099-a7ed-54f6-9f0d-555134ac927a");
    private static readonly Guid WikiNamespace = new("b147fb71-9ad5-5d57-ae69-744f2c7ba36b");

    public static AchievementId FromLegacyCode(string legacyCode)
    {
        if (string.IsNullOrWhiteSpace(legacyCode))
        {
            throw new ArgumentException("Legacy code cannot be blank.", nameof(legacyCode));
        }

        Span<byte> namespaceBytes = stackalloc byte[16];
        LegacyNamespace.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(legacyCode.Trim());
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input.AsSpan(namespaceBytes.Length));

        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new AchievementId(new Guid(hash.AsSpan(0, 16), bigEndian: true));
    }

    public static AchievementId FromWikiSource(string wikiSourceRef)
    {
        if (string.IsNullOrWhiteSpace(wikiSourceRef))
        {
            throw new ArgumentException("Wiki source reference cannot be blank.", nameof(wikiSourceRef));
        }

        return new AchievementId(CreateUuidV5(WikiNamespace, wikiSourceRef.Trim()));
    }

    private static Guid CreateUuidV5(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input.AsSpan(namespaceBytes.Length));
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    public override string ToString() => Value.ToString("D");
}

public enum ProgressStatus
{
    Incomplete,
    Completed,
    Unavailable,
    Occupied
}

public static class ProgressStatusText
{
    public static string ToChinese(this ProgressStatus status) => status switch
    {
        ProgressStatus.Incomplete => "未完成",
        ProgressStatus.Completed => "已完成",
        ProgressStatus.Unavailable => "暂不可获取",
        ProgressStatus.Occupied => "已占用",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown progress status.")
    };

    public static bool TryParseChinese(string? value, out ProgressStatus status)
    {
        status = value switch
        {
            "未完成" => ProgressStatus.Incomplete,
            "已完成" => ProgressStatus.Completed,
            "暂不可获取" => ProgressStatus.Unavailable,
            "已占用" => ProgressStatus.Occupied,
            _ => default
        };

        return value is "未完成" or "已完成" or "暂不可获取" or "已占用";
    }
}

public sealed record Achievement(
    AchievementId Id,
    string LegacyCode,
    int AbsoluteOrder,
    string Version,
    string FirstCategory,
    string SecondCategory,
    string Name,
    string Description,
    string Reward,
    bool IsHidden,
    string? GroupId = null,
    string? WikiSourceRef = null,
    bool IsTombstone = false,
    IReadOnlyList<string>? MutualExclusionCodes = null)
{
    public IReadOnlyList<string> EffectiveMutualExclusionCodes => MutualExclusionCodes ?? Array.Empty<string>();
}

public sealed record AchievementRow(
    AchievementId Id,
    string LegacyCode,
    int AbsoluteOrder,
    string Version,
    string FirstCategory,
    string SecondCategory,
    string Name,
    string Description,
    string Reward,
    bool IsHidden,
    string? GroupId,
    ProgressStatus Status,
    string? WikiSourceRef = null,
    bool IsTombstone = false,
    IReadOnlyList<string>? MutualExclusionCodes = null)
{
    public string StatusText => Status.ToChinese();
    public string HiddenText => IsHidden ? "隐藏" : string.Empty;
    // The raw group ID is an internal Wiki/source identifier and is not useful in the table.
    public string GroupText => string.IsNullOrWhiteSpace(GroupId)
        ? string.Empty
        : GroupId.StartsWith("progression-", StringComparison.Ordinal) ? "多合一" : "二选一";
}

public enum HiddenFilter
{
    All,
    VisibleOnly,
    HiddenOnly
}

public enum ObtainabilityFilter
{
    All,
    ObtainableOnly,
    UnavailableOnly
}

public enum CompletionFilter
{
    All,
    IncompleteOnly,
    CompletedOnly
}

public enum AchievementSort
{
    Default,
    IncompleteFirst
}

public sealed record AchievementQuery(
    string? SearchText = null,
    string? Version = null,
    string? FirstCategory = null,
    string? SecondCategory = null,
    HiddenFilter Hidden = HiddenFilter.All,
    ObtainabilityFilter Obtainability = ObtainabilityFilter.All,
    CompletionFilter Completion = CompletionFilter.All,
    ProgressStatus? Status = null,
    bool GroupsOnly = false,
    AchievementSort Sort = AchievementSort.Default);
