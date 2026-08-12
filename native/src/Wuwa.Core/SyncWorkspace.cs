namespace Wuwa.Core;

public sealed partial class AchievementWorkspace
{
    public async Task<WorkspaceSyncResult> SyncWikiAsync(
        IWikiAchievementSource wikiSource,
        WikiSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wikiSource);
        options ??= new WikiSyncOptions();
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SyncFailure(WorkspaceErrorCode.Cancelled, "Wiki synchronization was cancelled.");
        }

        try
        {
            if (_state is null)
            {
                return SyncFailure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.");
            }

            WikiFetchResult remote;
            try
            {
                remote = await wikiSource.FetchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SyncFailure(WorkspaceErrorCode.Cancelled, "Wiki synchronization was cancelled.");
            }
            catch (Exception exception)
            {
                return SyncFailure(WorkspaceErrorCode.LoadFailed, exception.Message);
            }

            var minimumRows = Math.Max(options.MinimumPlausibleRowCount, Math.Max(1, _state.Achievements.Count / 2));
            if (!remote.IsSuccess || remote.HttpStatusCode is < 200 or >= 300 || remote.Achievements.Count < minimumRows)
            {
                return SyncFailure(WorkspaceErrorCode.WikiRejected, remote.Error ?? "The Wiki response failed validation.");
            }

            if (remote.Achievements.Any(item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Description)))
            {
                return SyncFailure(WorkspaceErrorCode.WikiRejected, "The Wiki response contains rows with missing required fields.");
            }

            var duplicateSources = remote.Achievements
                .Where(item => !string.IsNullOrWhiteSpace(item.WikiSourceRef))
                .GroupBy(item => item.WikiSourceRef!, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSources is not null)
            {
                return SyncFailure(WorkspaceErrorCode.WikiRejected, $"The Wiki response contains duplicate source reference '{duplicateSources.Key}'.");
            }

            var active = _state.Achievements.ToDictionary(item => item.Id);
            var bySource = active.Values.Where(item => !string.IsNullOrWhiteSpace(item.WikiSourceRef)).ToLookup(item => item.WikiSourceRef!, StringComparer.Ordinal);
            var byFullSignature = active.Values.ToLookup(Signature, StringComparer.Ordinal);
            var byNameDescription = active.Values.ToLookup(NameDescriptionSignature, StringComparer.Ordinal);
            var matched = new HashSet<AchievementId>();
            var quarantined = new List<string>();
            var nextRows = new List<Achievement>();
            var added = 0;

            foreach (var remoteRow in remote.Achievements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Achievement? existing = null;
                if (!string.IsNullOrWhiteSpace(remoteRow.WikiSourceRef))
                {
                    existing = bySource[remoteRow.WikiSourceRef!].SingleOrDefault();
                }
                if (existing is null)
                {
                    existing = UniqueMatch(byFullSignature[Signature(remoteRow)]);
                }
                if (existing is null && options.AllowLegacyNameDescriptionFallback)
                {
                    existing = UniqueMatch(byNameDescription[NameDescriptionSignature(remoteRow)]);
                }

                if (existing is not null)
                {
                    matched.Add(existing.Id);
                    nextRows.Add(remoteRow with
                    {
                        Id = existing.Id,
                        LegacyCode = existing.LegacyCode,
                        WikiSourceRef = remoteRow.WikiSourceRef ?? existing.WikiSourceRef,
                        IsTombstone = false
                    });
                }
                else if (string.IsNullOrWhiteSpace(remoteRow.WikiSourceRef))
                {
                    quarantined.Add(remoteRow.Name);
                }
                else
                {
                    var id = AchievementId.FromWikiSource(remoteRow.WikiSourceRef!);
                    var code = NextLegacyCode(active.Values.Concat(nextRows));
                    nextRows.Add(remoteRow with { Id = id, LegacyCode = code });
                    added++;
                }
            }

            if (quarantined.Count > 0)
            {
                return new WorkspaceSyncResult(false, CreateSnapshot(_state), matched.Count, added, 0, quarantined, new WorkspaceError(WorkspaceErrorCode.LoadFailed, "One or more Wiki rows were ambiguous and were quarantined."));
            }

            var tombstoned = 0;
            foreach (var old in active.Values.Where(item => !matched.Contains(item.Id)))
            {
                nextRows.Add(old with { IsTombstone = true });
                tombstoned++;
            }

            var statuses = _state.Statuses.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var row in nextRows)
            {
                statuses.TryAdd(row.Id, ProgressStatus.Incomplete);
            }
            var candidate = new WorkspaceState(_state.Revision + 1, nextRows, statuses, _state.Categories, _state.Metadata);
            try
            {
                await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return SyncFailure(WorkspaceErrorCode.SaveFailed, exception.Message);
            }

            _state = candidate;
            return new WorkspaceSyncResult(true, CreateSnapshot(candidate), matched.Count, added, tombstoned, Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            return SyncFailure(WorkspaceErrorCode.Cancelled, "Wiki synchronization was cancelled.");
        }
        catch (Exception exception)
        {
            return SyncFailure(WorkspaceErrorCode.WikiRejected, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private WorkspaceSyncResult SyncFailure(WorkspaceErrorCode code, string message) =>
        new(false, _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state), Error: new WorkspaceError(code, message));

    private static Achievement? UniqueMatch(IEnumerable<Achievement> candidates)
    {
        using var enumerator = candidates.GetEnumerator();
        if (!enumerator.MoveNext()) return null;
        var first = enumerator.Current;
        return enumerator.MoveNext() ? null : first;
    }

    private static string Signature(Achievement item) => string.Join("\u001f", item.Name.Trim(), item.Description.Trim(), item.FirstCategory.Trim(), item.SecondCategory.Trim());
    private static string NameDescriptionSignature(Achievement item) => string.Join("\u001f", item.Name.Trim(), item.Description.Trim());

    private static string NextLegacyCode(IEnumerable<Achievement> rows)
    {
        var numeric = rows.Select(item => item.LegacyCode).Select(item => long.TryParse(item, out var value) ? value : 0).DefaultIfEmpty(0).Max();
        return (numeric + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
