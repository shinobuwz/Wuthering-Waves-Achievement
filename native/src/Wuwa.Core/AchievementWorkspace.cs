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
            var unmatchedCodes = progress.Statuses.Keys.Where(code => !byCode.ContainsKey(code)).OrderBy(code => code, StringComparer.Ordinal).ToArray();
            if (unmatchedCodes.Length > 0)
            {
                var preview = string.Join(", ", unmatchedCodes.Take(10));
                var suffix = unmatchedCodes.Length > 10 ? $" …（共 {unmatchedCodes.Length} 条）" : string.Empty;
                return ImportFailure(WorkspaceErrorCode.LegacyImportFailed, $"旧版进度包含当前成就库中不存在的编号：{preview}{suffix}", CreateSnapshot(_state));
            }
            foreach (var item in progress.Statuses)
            {
                statuses[byCode[item.Key].Id] = item.Value;
            }

            var candidate = new WorkspaceState(
                _state.Revision + 1,
                _state.Achievements,
                statuses,
                _state.Categories,
                _state.Metadata with
                {
                    ProfileNickname = options.SelectedCandidate.Nickname,
                    ProfileUid = options.SelectedCandidate.Uid,
                    LegacySourcePath = options.SelectedCandidate.ProgressPath,
                    ImportedAtUtc = DateTimeOffset.UtcNow
                });

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
            var diagnostics = ValidateExchangeCandidate(rows, payload.Progress, _state.Categories);
            if (diagnostics.Count > 0)
            {
                return new ExchangeImportResult(
                    false,
                    CreateSnapshot(_state),
                    payload.Kind,
                    diagnostics,
                    new WorkspaceError(WorkspaceErrorCode.ExchangeInvalid, "Exchange candidate failed validation."));
            }

            var statuses = replace
                ? rows.ToDictionary(item => item.Id, _ => ProgressStatus.Incomplete)
                : new Dictionary<AchievementId, ProgressStatus>(_state.Statuses);
            var byCode = rows.ToDictionary(item => item.LegacyCode, StringComparer.Ordinal);
            foreach (var item in payload.Progress)
            {
                if (byCode.TryGetValue(item.Key, out var achievement)) statuses[achievement.Id] = item.Value;
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

    public async Task<OcrApplyResult> ApplyOcrPreviewAsync(
        OcrScanPreview preview,
        bool confirm,
        bool preventCompletedDowngrade = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OcrApplyFailure(WorkspaceErrorCode.Cancelled, "Applying the OCR preview was cancelled.");
        }

        try
        {
            if (_state is null) return OcrApplyFailure(WorkspaceErrorCode.NotOpen, "The achievement workspace is not open.");
            var snapshot = CreateSnapshot(_state);
            if (!confirm) return new OcrApplyResult(false, snapshot, 0, 0, 0, new WorkspaceError(WorkspaceErrorCode.OcrApplyRequiresConfirmation, "OCR progress changes require explicit confirmation."));
            var duplicates = preview.Candidates.GroupBy(candidate => candidate.AchievementId).FirstOrDefault(group => group.Count() > 1);
            if (duplicates is not null) return new OcrApplyResult(false, snapshot, 0, 0, 0, new WorkspaceError(WorkspaceErrorCode.OcrPreviewInvalid, "OCR preview contains duplicate achievement candidates."));

            var achievements = _state.Achievements.ToDictionary(item => item.Id);
            var statuses = new Dictionary<AchievementId, ProgressStatus>(_state.Statuses);
            var updated = 0;
            var unchanged = 0;
            var prevented = 0;
            foreach (var candidate in preview.Candidates.OrderBy(candidate => candidate.ProposedStatus == ProgressStatus.Completed ? 1 : 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.IsAmbiguous || candidate.ProposedStatus is null || !achievements.TryGetValue(candidate.AchievementId, out var achievement))
                {
                    unchanged++;
                    continue;
                }
                var current = statuses[candidate.AchievementId];
                if (preventCompletedDowngrade && current == ProgressStatus.Completed && candidate.ProposedStatus != ProgressStatus.Completed)
                {
                    prevented++;
                    continue;
                }
                if (current == candidate.ProposedStatus)
                {
                    unchanged++;
                    continue;
                }
                ApplyStatusTransition(_state.Achievements, statuses, achievement, candidate.ProposedStatus.Value);
                updated++;
            }

            if (updated == 0) return new OcrApplyResult(true, snapshot, 0, unchanged, prevented);
            var settings = new Dictionary<string, string>(_state.Metadata.EffectiveSettings, StringComparer.Ordinal)
            {
                ["ocr.lastAppliedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["ocr.lastCandidateCount"] = preview.Candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var candidateState = new WorkspaceState(
                _state.Revision + 1,
                _state.Achievements,
                statuses,
                _state.Categories,
                _state.Metadata with { Settings = settings });
            await _store.SaveAsync(candidateState, cancellationToken).ConfigureAwait(false);
            _state = candidateState;
            return new OcrApplyResult(true, CreateSnapshot(candidateState), updated, unchanged, prevented);
        }
        catch (OperationCanceledException)
        {
            return OcrApplyFailure(WorkspaceErrorCode.Cancelled, "Applying the OCR preview was cancelled.");
        }
        catch (Exception exception)
        {
            return OcrApplyFailure(WorkspaceErrorCode.SaveFailed, exception.Message);
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
                _state.Categories,
                _state.Metadata);

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

        var ids = new HashSet<AchievementId>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var achievement in state.Achievements)
        {
            if (!ids.Add(achievement.Id) || string.IsNullOrWhiteSpace(achievement.LegacyCode) || !codes.Add(achievement.LegacyCode))
            {
                throw new InvalidDataException("Workspace contains blank or duplicate achievement identity.");
            }
            if (!state.Statuses.TryGetValue(achievement.Id, out var status) || !Enum.IsDefined(status))
            {
                throw new InvalidDataException($"Achievement {achievement.LegacyCode} has no valid progress status.");
            }
        }
        if (state.Statuses.Count != state.Achievements.Count)
        {
            throw new InvalidDataException("Workspace contains progress for an unknown achievement.");
        }
    }

    private static IReadOnlyList<ExchangeDiagnostic> ValidateExchangeCandidate(
        IReadOnlyList<Achievement> rows,
        IReadOnlyDictionary<string, ProgressStatus> progress,
        CategoryCatalog categories)
    {
        var diagnostics = new List<ExchangeDiagnostic>();
        var ids = new Dictionary<AchievementId, int>();
        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 1;
            var row = rows[index];
            void Required(string value, string field)
            {
                if (string.IsNullOrWhiteSpace(value)) diagnostics.Add(new ExchangeDiagnostic("required", $"Row {rowNumber} is missing required field '{field}'.", rowNumber, field));
            }
            Required(row.LegacyCode, "legacyCode");
            Required(row.Version, "version");
            Required(row.FirstCategory, "firstCategory");
            Required(row.SecondCategory, "secondCategory");
            Required(row.Name, "name");
            Required(row.Description, "description");
            if (!ids.TryAdd(row.Id, rowNumber)) diagnostics.Add(new ExchangeDiagnostic("duplicate-id", $"Row {rowNumber} duplicates achievement identity from row {ids[row.Id]}.", rowNumber, "id"));
            if (!string.IsNullOrWhiteSpace(row.LegacyCode) && !codes.TryAdd(row.LegacyCode, rowNumber)) diagnostics.Add(new ExchangeDiagnostic("duplicate-code", $"Row {rowNumber} duplicates legacy code from row {codes[row.LegacyCode]}.", rowNumber, "legacyCode"));

            if (categories.FirstCategories.Count > 0 && !categories.FirstCategories.ContainsKey(row.FirstCategory))
            {
                diagnostics.Add(new ExchangeDiagnostic("unknown-category", $"Row {rowNumber} uses unknown first category '{row.FirstCategory}'.", rowNumber, "firstCategory"));
            }
            if (categories.SecondCategories.TryGetValue(row.FirstCategory, out var seconds) && !seconds.ContainsKey(row.SecondCategory))
            {
                diagnostics.Add(new ExchangeDiagnostic("wrong-subcategory", $"Row {rowNumber} uses second category '{row.SecondCategory}' outside '{row.FirstCategory}'.", rowNumber, "secondCategory"));
            }
        }

        foreach (var pair in progress)
        {
            if (!Enum.IsDefined(pair.Value)) diagnostics.Add(new ExchangeDiagnostic("invalid-status", $"Progress entry '{pair.Key}' has an invalid status.", Field: "status"));
            if (rows.Count > 0 && !codes.ContainsKey(pair.Key)) diagnostics.Add(new ExchangeDiagnostic("unknown-progress-code", $"Progress entry '{pair.Key}' does not refer to an imported achievement.", Field: "legacyCode"));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowNumber = index + 1;
            var mutual = row.EffectiveMutualExclusionCodes;
            if (mutual.Count != mutual.Distinct(StringComparer.Ordinal).Count()) diagnostics.Add(new ExchangeDiagnostic("duplicate-group-reference", $"Row {rowNumber} repeats a mutual-exclusion reference.", rowNumber, "mutualExclusionCodes"));
            if (mutual.Contains(row.LegacyCode, StringComparer.Ordinal)) diagnostics.Add(new ExchangeDiagnostic("self-reference", $"Row {rowNumber} references itself.", rowNumber, "mutualExclusionCodes"));
            foreach (var code in mutual)
            {
                var target = rows.SingleOrDefault(item => string.Equals(item.LegacyCode, code, StringComparison.Ordinal));
                if (target is null) diagnostics.Add(new ExchangeDiagnostic("missing-group-target", $"Row {rowNumber} references missing achievement '{code}'.", rowNumber, "mutualExclusionCodes"));
                else if (string.IsNullOrWhiteSpace(row.GroupId) || !string.Equals(row.GroupId, target.GroupId, StringComparison.Ordinal)) diagnostics.Add(new ExchangeDiagnostic("cross-group-reference", $"Row {rowNumber} references achievement '{code}' outside its group.", rowNumber, "mutualExclusionCodes"));
                else if (!target.EffectiveMutualExclusionCodes.Contains(row.LegacyCode, StringComparer.Ordinal)) diagnostics.Add(new ExchangeDiagnostic("non-reciprocal-reference", $"Row {rowNumber} has a non-reciprocal reference to '{code}'.", rowNumber, "mutualExclusionCodes"));
            }
        }
        return diagnostics;
    }

    private OcrApplyResult OcrApplyFailure(WorkspaceErrorCode code, string message) =>
        new(false, _state is null ? WorkspaceSnapshot.Empty : CreateSnapshot(_state), 0, 0, 0, new WorkspaceError(code, message));

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
