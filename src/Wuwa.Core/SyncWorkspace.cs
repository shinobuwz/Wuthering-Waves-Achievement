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
            var allowLegacyFallback = options.AllowLegacyNameDescriptionFallback && active.Values.All(item => string.IsNullOrWhiteSpace(item.WikiSourceRef));
            var matched = new HashSet<AchievementId>();
            var quarantined = new List<string>();
            var nextRows = new List<Achievement>();
            var added = 0;

            foreach (var remoteRow in remote.Achievements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Achievement? existing = null;
                var ambiguous = false;
                if (!string.IsNullOrWhiteSpace(remoteRow.WikiSourceRef))
                {
                    var sourceMatch = Match(bySource[remoteRow.WikiSourceRef!]);
                    existing = sourceMatch.Achievement;
                    ambiguous = sourceMatch.IsAmbiguous;
                }
                if (existing is null && !ambiguous)
                {
                    var signatureMatch = Match(byFullSignature[Signature(remoteRow)]);
                    existing = signatureMatch.Achievement;
                    ambiguous = signatureMatch.IsAmbiguous;
                }
                if (existing is null && !ambiguous && allowLegacyFallback)
                {
                    var fallbackMatch = Match(byNameDescription[NameDescriptionSignature(remoteRow)]);
                    existing = fallbackMatch.Achievement;
                    ambiguous = fallbackMatch.IsAmbiguous;
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
                else if (ambiguous || string.IsNullOrWhiteSpace(remoteRow.WikiSourceRef))
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
                return new WorkspaceSyncResult(false, CreateSnapshot(_state), matched.Count, added, 0, quarantined, new WorkspaceError(WorkspaceErrorCode.WikiRejected, "One or more Wiki rows were ambiguous and were quarantined."));
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
            var categories = BuildCategoryCatalog(_state.Categories, remote.Achievements);
            if (EquivalentContent(_state, nextRows, statuses, categories))
            {
                return new WorkspaceSyncResult(true, CreateSnapshot(_state), matched.Count, 0, 0, Array.Empty<string>());
            }

            var candidate = new WorkspaceState(_state.Revision + 1, nextRows, statuses, categories, _state.Metadata);
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

    private static MatchResult Match(IEnumerable<Achievement> candidates)
    {
        using var enumerator = candidates.GetEnumerator();
        if (!enumerator.MoveNext()) return new MatchResult(null, false);
        var first = enumerator.Current;
        return enumerator.MoveNext() ? new MatchResult(null, true) : new MatchResult(first, false);
    }

    private static string Signature(Achievement item) => string.Join("\u001f", Normalize(item.Name), Normalize(item.Description), Normalize(item.FirstCategory), Normalize(item.SecondCategory));
    private static string NameDescriptionSignature(Achievement item) => string.Join("\u001f", Normalize(item.Name), Normalize(item.Description));
    private static string Normalize(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormKC)
            .Replace('・', '·')
            .Replace('•', '·')
            .Trim();
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record MatchResult(Achievement? Achievement, bool IsAmbiguous);

    private static CategoryCatalog BuildCategoryCatalog(
        CategoryCatalog current,
        IReadOnlyList<Achievement> remoteRows)
    {
        var firstNames = remoteRows
            .Select(item => item.FirstCategory)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var firstCategories = new Dictionary<string, int>(StringComparer.Ordinal);
        if (current.FirstCategories.Count > 0)
        {
            foreach (var old in current.FirstCategories.OrderBy(item => item.Value))
            {
                if (firstNames.Contains(old.Key, StringComparer.Ordinal)) firstCategories[old.Key] = old.Value;
            }
        }
        var nextFirstOrder = firstCategories.Values.DefaultIfEmpty(0).Max() + 1;
        foreach (var first in firstNames)
        {
            if (!firstCategories.ContainsKey(first)) firstCategories[first] = nextFirstOrder++;
        }

        var secondCategories = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var first in firstNames)
        {
            var remoteNames = remoteRows
                .Where(item => string.Equals(item.FirstCategory, first, StringComparison.Ordinal))
                .Select(item => item.SecondCategory)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var orders = new Dictionary<string, int>(StringComparer.Ordinal);

            if (current.SecondCategories.TryGetValue(first, out var oldOrders))
            {
                foreach (var old in oldOrders.OrderBy(item => item.Value))
                {
                    if (remoteNames.Contains(old.Key, StringComparer.Ordinal) || IsCompatibilityCategoryAlias(old.Key, remoteNames))
                    {
                        orders[old.Key] = old.Value;
                    }
                }
            }

            var nextSecondOrder = orders.Values.DefaultIfEmpty(0).Max() + 10;
            foreach (var name in remoteNames)
            {
                if (!orders.ContainsKey(name)) orders[name] = nextSecondOrder;
                nextSecondOrder += 10;
            }

            secondCategories[first] = orders;
        }

        return new CategoryCatalog(firstCategories, secondCategories);
    }

    private static bool IsCompatibilityCategoryAlias(string name, IReadOnlyCollection<string> remoteNames)
    {
        var separator = name.LastIndexOf('·');
        if (separator <= 0 || separator == name.Length - 1 || name[(separator + 1)..] != "一") return false;
        var canonical = name[..separator];
        return remoteNames.Contains(canonical, StringComparer.Ordinal);
    }

    private static bool EquivalentContent(
        WorkspaceState current,
        IReadOnlyList<Achievement> rows,
        IReadOnlyDictionary<AchievementId, ProgressStatus> statuses,
        CategoryCatalog categories)
    {
        var ordered = rows.OrderBy(item => item.AbsoluteOrder).ToArray();
        if (ordered.Length != current.Achievements.Count || statuses.Count != current.Statuses.Count || !CategoryCatalogEquivalent(current.Categories, categories)) return false;
        for (var index = 0; index < ordered.Length; index++)
        {
            var left = current.Achievements[index];
            var right = ordered[index];
            if (left.Id != right.Id || left.LegacyCode != right.LegacyCode || left.AbsoluteOrder != right.AbsoluteOrder || left.Version != right.Version ||
                left.FirstCategory != right.FirstCategory || left.SecondCategory != right.SecondCategory || left.Name != right.Name ||
                left.Description != right.Description || left.Reward != right.Reward || left.IsHidden != right.IsHidden || left.GroupId != right.GroupId ||
                left.WikiSourceRef != right.WikiSourceRef || left.IsTombstone != right.IsTombstone ||
                !left.EffectiveMutualExclusionCodes.SequenceEqual(right.EffectiveMutualExclusionCodes, StringComparer.Ordinal)) return false;
        }
        return statuses.All(pair => current.Statuses.TryGetValue(pair.Key, out var currentStatus) && currentStatus == pair.Value);
    }

    private static string NextLegacyCode(IEnumerable<Achievement> rows)
    {
        var numeric = rows.Select(item => item.LegacyCode).Select(item => long.TryParse(item, out var value) ? value : 0).DefaultIfEmpty(0).Max();
        return (numeric + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
