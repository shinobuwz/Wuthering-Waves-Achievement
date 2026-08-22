namespace Wuwa.Core;

/// <summary>
/// Small, local compatibility rules for achievements whose in-game OCR text
/// differs from the Wiki representation.
/// </summary>
public static class BuiltInAchievementRules
{
    public const string WangRiJinzhouOcrName = "往日之音·今州";
    public const string WangRiJinzhouGroupId = "progression-wangriyin-jinzhou";

    private static readonly HashSet<string> WangRiJinzhouLegacyCodes =
        ["10100001", "10100002", "10100003"];

    public static bool IsWangRiJinzhou(Achievement achievement) =>
        achievement is not null && WangRiJinzhouLegacyCodes.Contains(achievement.LegacyCode);

    public static bool IsWangRiJinzhou(AchievementRow achievement) =>
        achievement is not null && WangRiJinzhouLegacyCodes.Contains(achievement.LegacyCode);

    public static Achievement Apply(Achievement achievement) =>
        IsWangRiJinzhou(achievement)
            ? achievement with { GroupId = WangRiJinzhouGroupId }
            : achievement;

    public static AchievementLibrary Apply(AchievementLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new AchievementLibrary(library.Achievements.Select(Apply), library.Categories);
    }

    public static WikiFetchResult Apply(WikiFetchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result with { Achievements = result.Achievements.Select(Apply).ToArray() };
    }

    public static string GetOcrName(Achievement achievement) =>
        IsWangRiJinzhou(achievement) ? WangRiJinzhouOcrName : achievement.Name;

    public static string GetOcrName(AchievementRow achievement) =>
        IsWangRiJinzhou(achievement) ? WangRiJinzhouOcrName : achievement.Name;

    public static bool IsWangRiJinzhouOcrName(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        AchievementOcrMatcher.MatchKnownText(text, [WangRiJinzhouOcrName], out _) is not null;
}
