namespace Wuwa.Core;

/// <summary>
/// Ordered, debounced scene-transition engine. It contains no WPF, OpenCV,
/// OCR, persistence, or game-specific dependencies.
/// </summary>
public sealed class SceneTransitionEngine<TFrame> : IDisposable
{
    private readonly SceneTransitionOptions _options;
    private readonly ISceneMatcher<TFrame> _matcher;
    private readonly IReadOnlyDictionary<string, ISceneHandler<TFrame>> _handlers;
    private readonly object _queueSync = new();
    private readonly object _stateSync = new();
    private readonly AsyncLocal<int> _callbackDepth = new();
    private Task _queueTail = Task.CompletedTask;
    private string _currentScene;
    private string? _pendingScene;
    private int _pendingConfirmationFrames;
    private bool _disposed;

    public SceneTransitionEngine(
        SceneTransitionOptions options,
        ISceneMatcher<TFrame> matcher,
        IReadOnlyDictionary<string, ISceneHandler<TFrame>>? handlers = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _handlers = CopyHandlers(handlers, options);
        _currentScene = options.InitialScene;
    }

    public string CurrentScene
    {
        get { lock (_stateSync) return _currentScene; }
    }

    public string? PendingScene
    {
        get { lock (_stateSync) return _pendingScene; }
    }

    public int PendingConfirmationFrames
    {
        get { lock (_stateSync) return _pendingConfirmationFrames; }
    }

    public ValueTask<SceneTransitionResult> ProcessAsync(
        TFrame frame,
        CancellationToken cancellationToken = default)
    {
        var ticket = EnqueueOperation();
        return new ValueTask<SceneTransitionResult>(ProcessQueuedAsync(frame, ticket, cancellationToken));
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken = default) =>
        ResetQueuedAsync(_options.InitialScene, cancellationToken);

    public ValueTask ResetAsync(string scene, CancellationToken cancellationToken = default)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(scene)) throw new ArgumentException("Reset scene cannot be blank.", nameof(scene));
        var target = scene.Trim();
        if (!_options.ContainsScene(target)) throw new ArgumentException($"Scene '{target}' has no transition row.", nameof(scene));
        return ResetQueuedAsync(target, cancellationToken);
    }

    public void Dispose()
    {
        lock (_queueSync)
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    private async Task<SceneTransitionResult> ProcessQueuedAsync(
        TFrame frame,
        QueueTicket ticket,
        CancellationToken cancellationToken)
    {
        var completionDeferred = false;
        try
        {
            try
            {
                await ticket.Predecessor.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ticket.Predecessor.IsCompleted)
            {
                completionDeferred = true;
                _ = CompleteAfterAsync(ticket.Predecessor, ticket.Completion);
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await ProcessCoreAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!completionDeferred) ticket.Completion.TrySetResult();
        }
    }

    private ValueTask ResetQueuedAsync(string target, CancellationToken cancellationToken)
    {
        var ticket = EnqueueOperation();
        return new ValueTask(ResetQueuedCoreAsync(target, ticket, cancellationToken));
    }

    private async Task ResetQueuedCoreAsync(
        string target,
        QueueTicket ticket,
        CancellationToken cancellationToken)
    {
        var completionDeferred = false;
        try
        {
            try
            {
                await ticket.Predecessor.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ticket.Predecessor.IsCompleted)
            {
                completionDeferred = true;
                _ = CompleteAfterAsync(ticket.Predecessor, ticket.Completion);
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateSync)
            {
                _currentScene = target;
                _pendingScene = null;
                _pendingConfirmationFrames = 0;
            }
        }
        finally
        {
            if (!completionDeferred) ticket.Completion.TrySetResult();
        }
    }

    private QueueTicket EnqueueOperation()
    {
        if (_callbackDepth.Value > 0)
        {
            throw new InvalidOperationException("Scene engine operations cannot be enqueued reentrantly from a matcher or handler.");
        }

        lock (_queueSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var ticket = new QueueTicket(
                _queueTail,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _queueTail = ticket.Completion.Task;
            return ticket;
        }
    }

    private static async Task CompleteAfterAsync(Task predecessor, TaskCompletionSource completion)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task<SceneTransitionResult> ProcessCoreAsync(TFrame frame, CancellationToken cancellationToken)
    {
        SceneState initialState;
        lock (_stateSync)
        {
            initialState = new SceneState(_currentScene, _pendingScene, _pendingConfirmationFrames);
        }

        var candidates = _options.GetCandidates(initialState.CurrentScene);
        var evaluated = new List<string>(candidates.Count);
        SceneMatch? match = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluated.Add(candidate);
            var observation = await InvokeMatcherAsync(frame, candidate, cancellationToken).ConfigureAwait(false);
            if (observation is null)
            {
                throw new InvalidOperationException($"The matcher returned null while evaluating scene '{candidate}'.");
            }
            if (!string.Equals(observation.Scene, candidate, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The matcher returned scene '{observation.Scene}' while evaluating '{candidate}'.");
            }
            if (!double.IsFinite(observation.Confidence))
            {
                throw new InvalidOperationException($"The matcher returned a non-finite confidence for scene '{candidate}'.");
            }
            if (!observation.IsMatch) continue;
            match = observation;
            break;
        }

        var detectedScene = match?.Scene ?? _options.UnknownScene;
        var decision = CalculateDecision(initialState, detectedScene);
        var handlerInvoked = false;
        if (match is not null && _handlers.TryGetValue(match.Scene, out var handler))
        {
            var context = new SceneHandlerContext<TFrame>(
                frame,
                initialState.CurrentScene,
                decision.State.CurrentScene,
                match,
                decision.IsTransitionConfirmed);
            await InvokeHandlerAsync(handler, context, cancellationToken).ConfigureAwait(false);
            handlerInvoked = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateSync)
        {
            _currentScene = decision.State.CurrentScene;
            _pendingScene = decision.State.PendingScene;
            _pendingConfirmationFrames = decision.State.PendingConfirmationFrames;
        }

        return new SceneTransitionResult(
            initialState.CurrentScene,
            decision.State.CurrentScene,
            detectedScene,
            decision.IsTransitionConfirmed,
            decision.State.PendingScene,
            decision.State.PendingConfirmationFrames,
            Array.AsReadOnly(evaluated.ToArray()),
            match?.Confidence ?? 0,
            handlerInvoked);
    }

    private async ValueTask<SceneMatch> InvokeMatcherAsync(
        TFrame frame,
        string candidate,
        CancellationToken cancellationToken)
    {
        _callbackDepth.Value++;
        try
        {
            return await _matcher.MatchAsync(frame, candidate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _callbackDepth.Value--;
        }
    }

    private async ValueTask InvokeHandlerAsync(
        ISceneHandler<TFrame> handler,
        SceneHandlerContext<TFrame> context,
        CancellationToken cancellationToken)
    {
        _callbackDepth.Value++;
        try
        {
            await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _callbackDepth.Value--;
        }
    }

    private TransitionDecision CalculateDecision(SceneState state, string detectedScene)
    {
        if (string.Equals(detectedScene, state.CurrentScene, StringComparison.Ordinal))
        {
            return new TransitionDecision(new SceneState(state.CurrentScene, null, 0), false);
        }

        var pendingFrames = string.Equals(state.PendingScene, detectedScene, StringComparison.Ordinal)
            ? checked(state.PendingConfirmationFrames + 1)
            : 1;
        var requiredFrames = string.Equals(detectedScene, _options.UnknownScene, StringComparison.Ordinal)
            ? _options.UnknownConfirmationFrames
            : _options.TransitionConfirmationFrames;
        if (pendingFrames < requiredFrames)
        {
            return new TransitionDecision(new SceneState(state.CurrentScene, detectedScene, pendingFrames), false);
        }

        return new TransitionDecision(new SceneState(detectedScene, null, 0), true);
    }

    private static IReadOnlyDictionary<string, ISceneHandler<TFrame>> CopyHandlers(
        IReadOnlyDictionary<string, ISceneHandler<TFrame>>? handlers,
        SceneTransitionOptions options)
    {
        var copy = new Dictionary<string, ISceneHandler<TFrame>>(StringComparer.Ordinal);
        if (handlers is null) return copy;
        foreach (var pair in handlers)
        {
            var scene = string.IsNullOrWhiteSpace(pair.Key)
                ? throw new ArgumentException("Scene-handler keys cannot be blank.", nameof(handlers))
                : pair.Key.Trim();
            if (pair.Value is null) throw new ArgumentException($"Scene handler '{scene}' cannot be null.", nameof(handlers));
            if (!options.ContainsScene(scene)) throw new ArgumentException($"Scene handler '{scene}' has no transition row.", nameof(handlers));
            if (!copy.TryAdd(scene, pair.Value)) throw new ArgumentException($"Scene handler '{scene}' is registered more than once.", nameof(handlers));
        }
        return copy;
    }

    private sealed record QueueTicket(Task Predecessor, TaskCompletionSource Completion);
    private sealed record SceneState(string CurrentScene, string? PendingScene, int PendingConfirmationFrames);
    private sealed record TransitionDecision(SceneState State, bool IsTransitionConfirmed);
}
