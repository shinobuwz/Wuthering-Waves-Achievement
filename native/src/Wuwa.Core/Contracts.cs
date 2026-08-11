using System.Collections.ObjectModel;

namespace Wuwa.Core;

public sealed class CategoryCatalog
{
    public static CategoryCatalog Empty { get; } = new(
        new Dictionary<string, int>(),
        new Dictionary<string, IReadOnlyDictionary<string, int>>());

    public CategoryCatalog(
        IReadOnlyDictionary<string, int> firstCategories,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> secondCategories)
    {
        FirstCategories = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(firstCategories, StringComparer.Ordinal));
        SecondCategories = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(
            secondCategories.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, int>)new ReadOnlyDictionary<string, int>(
                    new Dictionary<string, int>(pair.Value, StringComparer.Ordinal)),
                StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, int> FirstCategories { get; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> SecondCategories { get; }
}

public sealed class AchievementLibrary
{
    public AchievementLibrary(IEnumerable<Achievement> achievements, CategoryCatalog categories)
    {
        var rows = achievements.OrderBy(item => item.AbsoluteOrder).ToArray();
        if (rows.Select(item => item.Id).Distinct().Count() != rows.Length)
        {
            throw new InvalidDataException("Achievement library contains duplicate native identities.");
        }

        if (rows.Select(item => item.LegacyCode).Distinct(StringComparer.Ordinal).Count() != rows.Length)
        {
            throw new InvalidDataException("Achievement library contains duplicate legacy codes.");
        }

        Achievements = Array.AsReadOnly(rows);
        Categories = categories;
    }

    public IReadOnlyList<Achievement> Achievements { get; }

    public CategoryCatalog Categories { get; }
}

public sealed class WorkspaceState
{
    public WorkspaceState(
        long revision,
        IEnumerable<Achievement> achievements,
        IReadOnlyDictionary<AchievementId, ProgressStatus> statuses,
        CategoryCatalog categories)
    {
        Revision = revision;
        Achievements = Array.AsReadOnly(achievements.OrderBy(item => item.AbsoluteOrder).ToArray());
        Statuses = new ReadOnlyDictionary<AchievementId, ProgressStatus>(
            new Dictionary<AchievementId, ProgressStatus>(statuses));
        Categories = categories;
    }

    public long Revision { get; }

    public IReadOnlyList<Achievement> Achievements { get; }

    public IReadOnlyDictionary<AchievementId, ProgressStatus> Statuses { get; }

    public CategoryCatalog Categories { get; }
}

public interface IAppDataStore
{
    Task<WorkspaceState?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default);
}

public interface IAchievementLibrarySource
{
    Task<AchievementLibrary> LoadAsync(CancellationToken cancellationToken = default);
}

public enum WorkspaceErrorCode
{
    NotOpen,
    LoadFailed,
    InvalidStatus,
    AchievementNotFound,
    SaveFailed
}

public sealed record WorkspaceError(WorkspaceErrorCode Code, string Message);

public sealed class WorkspaceStatistics
{
    public WorkspaceStatistics(
        long revision,
        int total,
        int completed,
        int incomplete,
        int unavailable,
        int hidden,
        int groupedChoiceCount,
        IReadOnlyDictionary<string, int> byFirstCategory,
        IReadOnlyDictionary<string, int> bySecondCategory,
        IReadOnlyDictionary<string, int> byVersion)
    {
        Revision = revision;
        Total = total;
        Completed = completed;
        Incomplete = incomplete;
        Unavailable = unavailable;
        Hidden = hidden;
        GroupedChoiceCount = groupedChoiceCount;
        CompletionRatePercent = total == 0 ? 0 : completed * 100d / total;
        ByFirstCategory = AsReadOnly(byFirstCategory);
        BySecondCategory = AsReadOnly(bySecondCategory);
        ByVersion = AsReadOnly(byVersion);
    }

    public long Revision { get; }
    public int Total { get; }
    public int Completed { get; }
    public int Incomplete { get; }
    public int Unavailable { get; }
    public int Hidden { get; }
    public int GroupedChoiceCount { get; }
    public double CompletionRatePercent { get; }
    public IReadOnlyDictionary<string, int> ByFirstCategory { get; }
    public IReadOnlyDictionary<string, int> BySecondCategory { get; }
    public IReadOnlyDictionary<string, int> ByVersion { get; }

    private static IReadOnlyDictionary<string, int> AsReadOnly(IReadOnlyDictionary<string, int> source) =>
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(source, StringComparer.Ordinal));
}

public sealed record WorkspaceView(
    long Revision,
    IReadOnlyList<AchievementRow> Rows,
    WorkspaceStatistics Statistics,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> FirstCategories,
    IReadOnlyList<string> SecondCategories);

public sealed record WorkspaceSnapshot(
    long Revision,
    IReadOnlyList<AchievementRow> Rows,
    WorkspaceStatistics Statistics,
    CategoryCatalog Categories)
{
    public static WorkspaceSnapshot Empty { get; } = new(
        0,
        Array.Empty<AchievementRow>(),
        new WorkspaceStatistics(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>()),
        CategoryCatalog.Empty);
}

public sealed record WorkspaceCommandResult(
    bool IsSuccess,
    WorkspaceSnapshot Snapshot,
    WorkspaceError? Error = null);
