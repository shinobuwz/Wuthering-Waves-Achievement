namespace Wuwa.Core;

public interface IWikiAchievementSource
{
    Task<WikiFetchResult> FetchAsync(CancellationToken cancellationToken = default);
}

public sealed record WikiFetchResult(
    bool IsSuccess,
    IReadOnlyList<Achievement> Achievements,
    string? LastUpdateTime = null,
    string? Error = null,
    int HttpStatusCode = 200);

public sealed record WikiSyncOptions(
    bool AllowLegacyNameDescriptionFallback = true,
    int MinimumPlausibleRowCount = 1);

public sealed record WorkspaceSyncResult(
    bool IsSuccess,
    WorkspaceSnapshot Snapshot,
    int MatchedCount = 0,
    int AddedCount = 0,
    int TombstonedCount = 0,
    IReadOnlyList<string>? Quarantined = null,
    WorkspaceError? Error = null)
{
    public IReadOnlyList<string> EffectiveQuarantined => Quarantined ?? Array.Empty<string>();
}
