namespace Wuwa.Core;

public sealed partial class AchievementWorkspace
{
    private readonly IAppDataStore _store;
    private readonly IAchievementLibrarySource _librarySource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WorkspaceState? _state;

    public AchievementWorkspace(IAppDataStore store, IAchievementLibrarySource librarySource)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _librarySource = librarySource ?? throw new ArgumentNullException(nameof(librarySource));
    }

    public Task<WorkspaceCommandResult> OpenAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(createEmptyIfMissing: true, cancellationToken);

    public async Task<WorkspaceCommandResult> OpenAsync(bool createEmptyIfMissing, CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                WorkspaceErrorCode.Cancelled,
                "Opening the achievement workspace was cancelled.",
                _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }

        try
        {
            try
            {
                var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (state is null)
                {
                    var library = await _librarySource.LoadAsync(cancellationToken).ConfigureAwait(false);
                    var statuses = library.Achievements.ToDictionary(
                        achievement => achievement.Id,
                        _ => ProgressStatus.Incomplete);
                    state = new WorkspaceState(1, library.Achievements, statuses, library.Categories);
                    if (createEmptyIfMissing)
                    {
                        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
                    }
                }

                ValidateState(state);
                _state = state;
                return Success(CreateSnapshot(state));
            }
            catch (OperationCanceledException)
            {
                return Failure(
                    WorkspaceErrorCode.Cancelled,
                    "Opening the achievement workspace was cancelled.",
                    _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
            }
            catch (Exception exception)
            {
                return Failure(
                    WorkspaceErrorCode.LoadFailed,
                    $"Unable to open the achievement workspace: {exception.Message}",
                    _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LegacyDiscoveryResult> DiscoverLegacyProfilesAsync(
        ILegacyProfileSource legacySource,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legacySource);
        try
        {
            return await legacySource.DiscoverAsync(configPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new LegacyDiscoveryResult(
                LegacyDiscoveryStatus.Invalid,
                Array.Empty<LegacyProfileCandidate>(),
                Error: new WorkspaceError(WorkspaceErrorCode.Cancelled, "Legacy profile discovery was cancelled."));
        }
        catch (Exception exception)
        {
            return new LegacyDiscoveryResult(
                LegacyDiscoveryStatus.Invalid,
                Array.Empty<LegacyProfileCandidate>(),
                Error: new WorkspaceError(WorkspaceErrorCode.LegacyDiscoveryFailed, exception.Message));
        }
    }

    public async Task<WorkspaceImportResult> ImportLegacyProfileAsync(
        ILegacyProfileSource legacySource,
        LegacyImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legacySource);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ImportFailure(WorkspaceErrorCode.Cancelled, "Legacy import was cancelled.", _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }

        try
        {
            if (_state is null)
            {
                return ImportFailure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.", WorkspaceSnapshot.Empty);
            }

            if (options.SelectedCandidate is null)
            {
                return ImportFailure(WorkspaceErrorCode.LegacyProfileNotFound, "A legacy profile must be selected before import.", CreateSnapshot(_state));
            }

            if (!options.ConfirmReplace)
            {
                return ImportFailure(WorkspaceErrorCode.LegacyImportRequiresConfirmation, "Legacy import replaces native progress and requires confirmation.", CreateSnapshot(_state));
            }

            LegacyProfileProgress progress;
            try
            {
                progress = await legacySource.ReadProgressAsync(options.SelectedCandidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ImportFailure(WorkspaceErrorCode.Cancelled, "Legacy import was cancelled.", CreateSnapshot(_state));
            }
            catch (Exception exception)
            {
                return ImportFailure(WorkspaceErrorCode.LegacyImportFailed, exception.Message, CreateSnapshot(_state));
            }

            var statuses = _state.Achievements.ToDictionary(item => item.Id, _ => ProgressStatus.Incomplete);
            var byCode = _state.Achievements.ToDictionary(item => item.LegacyCode, StringComparer.Ordinal);
            foreach (var item in progress.Statuses)
            {
                if (!byCode.TryGetValue(item.Key, out var achievement))
                {
                    continue;
                }

                statuses[achievement.Id] = item.Value;
            }

            var candidate = new WorkspaceState(
                _state.Revision + 1,
                _state.Achievements,
                statuses,
                _state.Categories,
                new WorkspaceMetadata(
                    options.SelectedCandidate.Nickname,
                    options.SelectedCandidate.Uid,
                    options.SelectedCandidate.ProgressPath,
                    DateTimeOffset.UtcNow));

            try
            {
                await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ImportFailure(WorkspaceErrorCode.Cancelled, "Legacy import was cancelled while saving.", CreateSnapshot(_state));
            }
            catch (Exception exception)
            {
                return ImportFailure(WorkspaceErrorCode.LegacyImportFailed, exception.Message, CreateSnapshot(_state));
            }

            _state = candidate;
            return new WorkspaceImportResult(true, CreateSnapshot(candidate));
        }
        finally
        {
            _gate.Release();
        }
    }

    public WorkspaceView Query(AchievementQuery? query = null)
    {
        var state = _state ?? throw new InvalidOperationException("OpenAsync must complete successfully before querying the workspace.");
        query ??= new AchievementQuery();

        IEnumerable<AchievementRow> rows = CreateRows(state);
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var searchText = query.SearchText.Trim();
            rows = rows.Where(row =>
                row.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                row.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Version))
        {
            rows = rows.Where(row => string.Equals(row.Version, query.Version, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.FirstCategory))
        {
            rows = rows.Where(row => string.Equals(row.FirstCategory, query.FirstCategory, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.SecondCategory))
        {
            rows = rows.Where(row => string.Equals(row.SecondCategory, query.SecondCategory, StringComparison.Ordinal));
        }

        rows = query.Hidden switch
        {
            HiddenFilter.VisibleOnly => rows.Where(row => !row.IsHidden),
            HiddenFilter.HiddenOnly => rows.Where(row => row.IsHidden),
            _ => rows
        };

        rows = query.Obtainability switch
        {
            ObtainabilityFilter.ObtainableOnly => rows.Where(row => row.Status is ProgressStatus.Incomplete or ProgressStatus.Completed),
            ObtainabilityFilter.UnavailableOnly => rows.Where(row => row.Status is ProgressStatus.Unavailable or ProgressStatus.Occupied),
            _ => rows
        };

        rows = query.Completion switch
        {
            CompletionFilter.CompletedOnly => rows.Where(row => row.Status == ProgressStatus.Completed),
            CompletionFilter.IncompleteOnly => rows.Where(row => row.Status != ProgressStatus.Completed),
            _ => rows
        };

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.GroupsOnly)
        {
            rows = rows.Where(row => !string.IsNullOrWhiteSpace(row.GroupId));
        }

        rows = query.Sort switch
        {
            AchievementSort.IncompleteFirst => rows
                .OrderBy(row => row.Status == ProgressStatus.Completed ? 1 : 0)
                .ThenBy(row => row.AbsoluteOrder),
            _ => rows.OrderBy(row => row.AbsoluteOrder)
        };

        var materialized = Array.AsReadOnly(rows.ToArray());
        return new WorkspaceView(
            state.Revision,
            materialized,
            CalculateStatistics(state.Revision, materialized),
            Array.AsReadOnly(state.Achievements.Select(item => item.Version).Distinct().OrderBy(VersionSortKey).ToArray()),
            Array.AsReadOnly(GetOrderedFirstCategories(state).ToArray()),
            Array.AsReadOnly(GetOrderedSecondCategories(state, query.FirstCategory).ToArray()));
    }

    public async Task<WorkspaceCommandResult> SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return Failure(WorkspaceErrorCode.ExchangeInvalid, "Setting key cannot be blank.", _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(WorkspaceErrorCode.Cancelled, "Setting update was cancelled.", _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }

        try
        {
            if (_state is null) return Failure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.", WorkspaceSnapshot.Empty);
            var settings = new Dictionary<string, string>(_state.Metadata.EffectiveSettings, StringComparer.Ordinal) { [key.Trim()] = value ?? string.Empty };
            var candidate = new WorkspaceState(_state.Revision + 1, _state.Achievements, _state.Statuses, _state.Categories, _state.Metadata with { Settings = settings });
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            _state = candidate;
            return Success(CreateSnapshot(candidate));
        }
        catch (OperationCanceledException)
        {
            return Failure(WorkspaceErrorCode.Cancelled, "Setting update was cancelled.", _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }
        catch (Exception exception)
        {
            return Failure(WorkspaceErrorCode.SaveFailed, exception.Message, _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceCommandResult> ExportAsync(
        IAchievementExportSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(WorkspaceErrorCode.Cancelled, "Export was cancelled.", WorkspaceSnapshot.Empty);
        }

        try
        {
            if (_state is null) return Failure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.", WorkspaceSnapshot.Empty);
            await sink.WriteAsync(_state, cancellationToken).ConfigureAwait(false);
            return Success(CreateSnapshot(_state));
        }
        catch (OperationCanceledException)
        {
            return Failure(WorkspaceErrorCode.Cancelled, "Export was cancelled.", _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }
        catch (Exception exception)
        {
            return Failure(WorkspaceErrorCode.ExchangeInvalid, exception.Message, _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExchangeImportResult> ImportExchangeAsync(
        IAchievementImportSource source,
        bool replace,
        bool confirmReplace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ExchangeFailure(WorkspaceErrorCode.Cancelled, "Exchange import was cancelled.");
        }

        try
        {
            if (_state is null) return ExchangeFailure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.");
            if (replace && !confirmReplace) return ExchangeFailure(WorkspaceErrorCode.LegacyImportRequiresConfirmation, "Replacing workspace data requires confirmation.");
            ExchangePayload payload;
            try
            {
                payload = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return ExchangeFailure(WorkspaceErrorCode.ExchangeInvalid, exception.Message);
            }

            var rows = replace && payload.Achievements.Count > 0 ? payload.Achievements : _state.Achievements;
            var statuses = rows.ToDictionary(item => item.Id, item => ProgressStatus.Incomplete);
            var byCode = rows.ToDictionary(item => item.LegacyCode, StringComparer.Ordinal);
            foreach (var item in payload.Progress)
            {
                if (byCode.TryGetValue(item.Key, out var achievement)) statuses[achievement.Id] = item.Value;
            }
            foreach (var item in payload.Achievements)
            {
                if (payload.Progress.TryGetValue(item.LegacyCode, out var status)) statuses[item.Id] = status;
            }

            var candidate = new WorkspaceState(_state.Revision + 1, rows, statuses, _state.Categories, _state.Metadata);
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            _state = candidate;
            return new ExchangeImportResult(true, CreateSnapshot(candidate), payload.Kind);
        }
        catch (OperationCanceledException)
        {
            return ExchangeFailure(WorkspaceErrorCode.Cancelled, "Exchange import was cancelled.");
        }
        catch (Exception exception)
        {
            return ExchangeFailure(WorkspaceErrorCode.ExchangeInvalid, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceCommandResult> ChangeStatusAsync(
        AchievementId achievementId,
        ProgressStatus status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                WorkspaceErrorCode.Cancelled,
                "Changing the achievement status was cancelled.",
                _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state));
        }

        try
        {
            if (_state is null)
            {
                return Failure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.", WorkspaceSnapshot.Empty);
            }

            var previousSnapshot = CreateSnapshot(_state);
            if (!Enum.IsDefined(status))
            {
                return Failure(WorkspaceErrorCode.InvalidStatus, "The requested progress status is not recognized.", previousSnapshot);
            }

            var selected = _state.Achievements.SingleOrDefault(item => item.Id == achievementId);
            if (selected is null)
            {
                return Failure(WorkspaceErrorCode.AchievementNotFound, "The selected achievement does not exist.", previousSnapshot);
            }

            var statuses = new Dictionary<AchievementId, ProgressStatus>(_state.Statuses);
            ApplyStatusTransition(_state.Achievements, statuses, selected, status);
            var candidate = new WorkspaceState(
                _state.Revision + 1,
                _state.Achievements,
                statuses,
                _state.Categories);

            try
            {
                await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Failure(
                    WorkspaceErrorCode.Cancelled,
                    "Saving the status change was cancelled.",
                    previousSnapshot);
            }
            catch (Exception exception)
            {
                return Failure(
                    WorkspaceErrorCode.SaveFailed,
                    $"Unable to save the status change: {exception.Message}",
                    previousSnapshot);
            }

            _state = candidate;
            return Success(CreateSnapshot(candidate));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ApplyStatusTransition(
        IReadOnlyList<Achievement> achievements,
        IDictionary<AchievementId, ProgressStatus> statuses,
        Achievement selected,
        ProgressStatus requestedStatus)
    {
        if (string.IsNullOrWhiteSpace(selected.GroupId))
        {
            statuses[selected.Id] = requestedStatus;
            return;
        }

        var group = achievements
            .Where(item => string.Equals(item.GroupId, selected.GroupId, StringComparison.Ordinal))
            .ToArray();

        if (requestedStatus == ProgressStatus.Completed)
        {
            foreach (var member in group)
            {
                statuses[member.Id] = member.Id == selected.Id
                    ? ProgressStatus.Completed
                    : ProgressStatus.Occupied;
            }

            return;
        }

        if (requestedStatus == ProgressStatus.Incomplete &&
            statuses[selected.Id] is ProgressStatus.Completed or ProgressStatus.Occupied)
        {
            foreach (var member in group)
            {
                statuses[member.Id] = ProgressStatus.Incomplete;
            }

            return;
        }

        statuses[selected.Id] = requestedStatus;
    }

    private static WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        var rows = Array.AsReadOnly(CreateRows(state).ToArray());
        return new WorkspaceSnapshot(
            state.Revision,
            rows,
            CalculateStatistics(state.Revision, rows),
            state.Categories,
            state.Metadata);
    }

    private static IEnumerable<AchievementRow> CreateRows(WorkspaceState state) =>
        state.Achievements.Select(achievement => new AchievementRow(
            achievement.Id,
            achievement.LegacyCode,
            achievement.AbsoluteOrder,
            achievement.Version,
            achievement.FirstCategory,
            achievement.SecondCategory,
            achievement.Name,
            achievement.Description,
            achievement.Reward,
            achievement.IsHidden,
            achievement.GroupId,
            state.Statuses[achievement.Id],
            achievement.WikiSourceRef,
            achievement.IsTombstone,
            achievement.EffectiveMutualExclusionCodes));

    private static WorkspaceStatistics CalculateStatistics(long revision, IReadOnlyList<AchievementRow> rows)
    {
        var logicalRows = rows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.GroupId) ? $"achievement:{row.Id}" : $"group:{row.GroupId}")
            .Select(group =>
            {
                var members = group.OrderBy(row => row.AbsoluteOrder).ToArray();
                var representative = members[0];
                var status = members.Any(row => row.Status == ProgressStatus.Completed)
                    ? ProgressStatus.Completed
                    : members.All(row => row.Status == ProgressStatus.Unavailable)
                        ? ProgressStatus.Unavailable
                        : ProgressStatus.Incomplete;
                return new LogicalAchievement(
                    representative.Version,
                    representative.FirstCategory,
                    representative.SecondCategory,
                    members.Any(row => row.IsHidden),
                    status,
                    !string.IsNullOrWhiteSpace(representative.GroupId));
            })
            .ToArray();

        return new WorkspaceStatistics(
            revision,
            logicalRows.Length,
            logicalRows.Count(item => item.Status == ProgressStatus.Completed),
            logicalRows.Count(item => item.Status == ProgressStatus.Incomplete),
            logicalRows.Count(item => item.Status == ProgressStatus.Unavailable),
            logicalRows.Count(item => item.IsHidden),
            logicalRows.Count(item => item.IsGrouped),
            Distribution(logicalRows.Select(item => item.FirstCategory)),
            Distribution(logicalRows.Select(item => item.SecondCategory)),
            Distribution(logicalRows.Select(item => item.Version)));
    }

    private static IReadOnlyDictionary<string, int> Distribution(IEnumerable<string> values) =>
        values.GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static IEnumerable<string> GetOrderedFirstCategories(WorkspaceState state)
    {
        var configured = state.Categories.FirstCategories
            .OrderBy(pair => pair.Value)
            .Select(pair => pair.Key);
        var additional = state.Achievements.Select(item => item.FirstCategory)
            .Where(item => !state.Categories.FirstCategories.ContainsKey(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal);
        return configured.Concat(additional);
    }

    private static IEnumerable<string> GetOrderedSecondCategories(WorkspaceState state, string? firstCategory)
    {
        var achievements = string.IsNullOrWhiteSpace(firstCategory)
            ? state.Achievements
            : state.Achievements.Where(item => string.Equals(item.FirstCategory, firstCategory, StringComparison.Ordinal)).ToArray();

        if (!string.IsNullOrWhiteSpace(firstCategory) &&
            state.Categories.SecondCategories.TryGetValue(firstCategory, out var configured))
        {
            var additional = achievements.Select(item => item.SecondCategory)
                .Where(item => !configured.ContainsKey(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal);
            return configured.OrderBy(pair => pair.Value).Select(pair => pair.Key).Concat(additional);
        }

        return achievements.Select(item => item.SecondCategory)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal);
    }

    private static double VersionSortKey(string version) =>
        double.TryParse(version, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : double.MaxValue;

    private static void ValidateState(WorkspaceState state)
    {
        if (state.Revision < 1)
        {
            throw new InvalidDataException("Workspace revision must be positive.");
        }

        foreach (var achievement in state.Achievements)
        {
            if (!state.Statuses.TryGetValue(achievement.Id, out var status) || !Enum.IsDefined(status))
            {
                throw new InvalidDataException($"Achievement {achievement.LegacyCode} has no valid progress status.");
            }
        }
    }

    private ExchangeImportResult ExchangeFailure(WorkspaceErrorCode code, string message) =>
        new(false, _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state), Error: new WorkspaceError(code, message));

    private static WorkspaceImportResult ImportFailure(
        WorkspaceErrorCode code,
        string message,
        WorkspaceSnapshot snapshot) => new(false, snapshot, null, new WorkspaceError(code, message));

    private static WorkspaceCommandResult Success(WorkspaceSnapshot snapshot) => new(true, snapshot);

    private static WorkspaceCommandResult Failure(
        WorkspaceErrorCode code,
        string message,
        WorkspaceSnapshot snapshot) => new(false, snapshot, new WorkspaceError(code, message));

    private sealed record LogicalAchievement(
        string Version,
        string FirstCategory,
        string SecondCategory,
        bool IsHidden,
        ProgressStatus Status,
        bool IsGrouped);
}
