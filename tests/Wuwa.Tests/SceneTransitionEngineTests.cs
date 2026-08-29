using System.Collections.Concurrent;
using Wuwa.Core;

namespace Wuwa.Tests;

[TestClass]
public sealed class SceneTransitionEngineTests
{
    [TestMethod]
    public async Task ProcessAsync_EvaluatesCandidatesInOrderAndStopsAtFirstMatch()
    {
        var matcher = new StubMatcher((_, scene, _) => scene switch
        {
            "battle" => Match(scene, false, 0.22),
            "selection" => Match(scene, true, 0.93),
            "lobby" => Match(scene, true, 0.99),
            _ => Match(scene, false)
        });
        using var engine = new SceneTransitionEngine<string>(CreateOptions(), matcher);

        var result = await engine.ProcessAsync("frame-1");

        CollectionAssert.AreEqual(new[] { "battle", "selection" }, matcher.EvaluatedScenes.ToArray());
        CollectionAssert.AreEqual(new[] { "battle", "selection" }, result.EvaluatedScenes.ToArray());
        Assert.AreEqual("selection", result.DetectedScene);
        Assert.AreEqual("selection", result.CurrentScene);
        Assert.AreEqual(0.93, result.Confidence, 0.0001);
        Assert.IsTrue(result.IsTransitionConfirmed);
    }

    [TestMethod]
    public async Task ProcessAsync_UsesTheCurrentStableScenesCandidateRow()
    {
        var matcher = new StubMatcher((frame, scene, _) => Match(scene, frame switch
        {
            "enter-lobby" => scene == "lobby",
            "stay-lobby" => scene == "lobby",
            _ => false
        }));
        var options = new SceneTransitionOptions(
            "unknown",
            "unknown",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["unknown"] = new[] { "lobby" },
                ["lobby"] = new[] { "battle", "lobby" },
                ["battle"] = new[] { "battle" }
            });
        using var engine = new SceneTransitionEngine<string>(options, matcher);

        _ = await engine.ProcessAsync("enter-lobby");
        matcher.EvaluatedScenes.Clear();
        var result = await engine.ProcessAsync("stay-lobby");

        CollectionAssert.AreEqual(new[] { "battle", "lobby" }, matcher.EvaluatedScenes.ToArray());
        CollectionAssert.AreEqual(new[] { "battle", "lobby" }, result.EvaluatedScenes.ToArray());
        Assert.AreEqual("lobby", result.CurrentScene);
    }

    [TestMethod]
    public async Task ProcessAsync_UsesKnownAndUnknownConfirmationPolicies()
    {
        var activeScene = "lobby";
        var matcher = new StubMatcher((_, scene, _) => Match(scene, scene == activeScene, scene == activeScene ? 0.9 : 0.1));
        using var engine = new SceneTransitionEngine<string>(CreateOptions(), matcher);

        var lobby = await engine.ProcessAsync("lobby");
        activeScene = "none";
        var firstMissing = await engine.ProcessAsync("missing-1");
        var secondMissing = await engine.ProcessAsync("missing-2");

        Assert.AreEqual("lobby", lobby.CurrentScene, "Known scenes should transition on their first matching frame by default.");
        Assert.AreEqual("lobby", firstMissing.CurrentScene);
        Assert.AreEqual("unknown", firstMissing.PendingScene);
        Assert.AreEqual(1, firstMissing.PendingConfirmationFrames);
        Assert.IsFalse(firstMissing.IsTransitionConfirmed);
        Assert.AreEqual("unknown", secondMissing.CurrentScene);
        Assert.IsNull(secondMissing.PendingScene);
        Assert.AreEqual(0, secondMissing.PendingConfirmationFrames);
        Assert.IsTrue(secondMissing.IsTransitionConfirmed);
    }

    [TestMethod]
    public async Task ProcessAsync_DirectPendingTargetReplacementRestartsAtOne()
    {
        var activeScene = "lobby";
        var matcher = new StubMatcher((_, scene, _) => Match(scene, scene == activeScene));
        using var engine = new SceneTransitionEngine<string>(CreateOptions(knownFrames: 3), matcher);

        var pendingLobby = await engine.ProcessAsync("frame-1");
        activeScene = "selection";
        var pendingSelectionOne = await engine.ProcessAsync("frame-2");
        var pendingSelectionTwo = await engine.ProcessAsync("frame-3");
        var confirmedSelection = await engine.ProcessAsync("frame-4");

        Assert.AreEqual("lobby", pendingLobby.PendingScene);
        Assert.AreEqual(1, pendingLobby.PendingConfirmationFrames);
        Assert.AreEqual("selection", pendingSelectionOne.PendingScene);
        Assert.AreEqual(1, pendingSelectionOne.PendingConfirmationFrames);
        Assert.AreEqual(2, pendingSelectionTwo.PendingConfirmationFrames);
        Assert.AreEqual("selection", confirmedSelection.CurrentScene);
        Assert.IsTrue(confirmedSelection.IsTransitionConfirmed);
    }

    [TestMethod]
    public async Task ProcessAsync_SeeingStableSceneClearsPendingTransition()
    {
        var activeScene = "lobby";
        var matcher = new StubMatcher((_, scene, _) => Match(scene, scene == activeScene));
        using var engine = new SceneTransitionEngine<string>(CreateOptions(knownFrames: 2), matcher);

        var pending = await engine.ProcessAsync("pending");
        activeScene = "none";
        var restored = await engine.ProcessAsync("restored");

        Assert.AreEqual("lobby", pending.PendingScene);
        Assert.AreEqual("unknown", restored.CurrentScene);
        Assert.IsNull(restored.PendingScene);
        Assert.AreEqual(0, restored.PendingConfirmationFrames);
    }

    [TestMethod]
    public async Task ProcessAsync_InvokesRegisteredHandlerForEveryRealMatchWithOriginalContext()
    {
        var frame = new object();
        var rawMatch = Match("selection", true, 0.88, "match-data");
        var matcher = new StubMatcher<object>((_, scene, _) => scene == "selection" ? rawMatch : Match(scene, false));
        var handler = new CaptureHandler<object>();
        using var engine = new SceneTransitionEngine<object>(
            CreateOptions(knownFrames: 2),
            matcher,
            new Dictionary<string, ISceneHandler<object>> { ["selection"] = handler });
        using var cancellation = new CancellationTokenSource();

        var pending = await engine.ProcessAsync(frame, cancellation.Token);
        var confirmed = await engine.ProcessAsync(frame, cancellation.Token);
        var stable = await engine.ProcessAsync(frame, cancellation.Token);

        Assert.AreEqual(3, handler.Contexts.Count);
        Assert.AreSame(frame, handler.Contexts[0].Frame);
        Assert.AreSame(rawMatch, handler.Contexts[0].Match);
        Assert.AreEqual(cancellation.Token, handler.Tokens[0]);
        Assert.IsFalse(handler.Contexts[0].IsTransitionConfirmed);
        Assert.AreEqual("unknown", handler.Contexts[0].PreviousScene);
        Assert.AreEqual("unknown", handler.Contexts[0].CurrentScene);
        Assert.IsTrue(handler.Contexts[1].IsTransitionConfirmed);
        Assert.AreEqual("selection", handler.Contexts[1].CurrentScene);
        Assert.IsFalse(handler.Contexts[2].IsTransitionConfirmed);
        Assert.AreEqual("selection", handler.Contexts[2].PreviousScene);
        Assert.IsTrue(pending.HandlerInvoked && confirmed.HandlerInvoked && stable.HandlerInvoked);
    }

    [TestMethod]
    public async Task ProcessAsync_DistinguishesRealAndSyntheticUnknownForHandlers()
    {
        var realUnknownHandler = new CaptureHandler<string>();
        var realMatcher = new StubMatcher((_, scene, _) => Match(scene, scene == "unknown", 0.91));
        var options = new SceneTransitionOptions(
            "lobby",
            "unknown",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["lobby"] = new[] { "unknown" },
                ["unknown"] = new[] { "unknown" }
            },
            unknownConfirmationFrames: 1);
        using var realEngine = new SceneTransitionEngine<string>(
            options,
            realMatcher,
            new Dictionary<string, ISceneHandler<string>> { ["unknown"] = realUnknownHandler });

        var real = await realEngine.ProcessAsync("real");

        var syntheticHandler = new CaptureHandler<string>();
        var syntheticMatcher = new StubMatcher((_, scene, _) => Match(scene, false));
        using var syntheticEngine = new SceneTransitionEngine<string>(
            options,
            syntheticMatcher,
            new Dictionary<string, ISceneHandler<string>> { ["unknown"] = syntheticHandler });
        var synthetic = await syntheticEngine.ProcessAsync("synthetic");

        Assert.IsTrue(real.HandlerInvoked);
        Assert.AreEqual(1, realUnknownHandler.Contexts.Count);
        Assert.AreEqual(0.91, real.Confidence, 0.0001);
        Assert.IsFalse(synthetic.HandlerInvoked);
        Assert.AreEqual(0, syntheticHandler.Contexts.Count);
        Assert.AreEqual(0, synthetic.Confidence);
    }

    [TestMethod]
    public async Task ProcessAsync_UnregisteredMatchUsesGenericFallback()
    {
        var matcher = new StubMatcher((_, scene, _) => Match(scene, scene == "lobby"));
        using var engine = new SceneTransitionEngine<string>(CreateOptions(), matcher);

        var result = await engine.ProcessAsync("lobby");

        Assert.AreEqual("lobby", result.CurrentScene);
        Assert.IsFalse(result.HandlerInvoked);
    }

    [TestMethod]
    public async Task ProcessAsync_CancellationDuringHandlerDoesNotCommitTransition()
    {
        var matcher = new StubMatcher((_, scene, _) => Match(scene, scene == "selection"));
        var handler = new BlockingHandler<string>();
        using var engine = new SceneTransitionEngine<string>(
            CreateOptions(),
            matcher,
            new Dictionary<string, ISceneHandler<string>> { ["selection"] = handler });
        using var cancellation = new CancellationTokenSource();

        var processing = engine.ProcessAsync("frame", cancellation.Token).AsTask();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await ExpectCancellationAsync(processing);
        Assert.AreEqual(cancellation.Token, handler.Token);
        Assert.AreEqual("unknown", engine.CurrentScene);
        Assert.IsNull(engine.PendingScene);
        Assert.AreEqual(0, engine.PendingConfirmationFrames);
    }

    [TestMethod]
    public async Task ProcessAsync_CancellationDuringMatcherPreservesExistingPendingState()
    {
        var matcher = new PendingThenBlockingMatcher();
        using var engine = new SceneTransitionEngine<string>(CreateOptions(knownFrames: 2), matcher);
        _ = await engine.ProcessAsync("establish-pending");
        using var cancellation = new CancellationTokenSource();

        var processing = engine.ProcessAsync("cancel-in-matcher", cancellation.Token).AsTask();
        await matcher.BlockingCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await ExpectCancellationAsync(processing);
        Assert.AreEqual(cancellation.Token, matcher.BlockingToken);
        Assert.AreEqual("unknown", engine.CurrentScene);
        Assert.AreEqual("selection", engine.PendingScene);
        Assert.AreEqual(1, engine.PendingConfirmationFrames);
    }

    [TestMethod]
    public async Task ProcessAsync_SerializesConcurrentFramesInCallOrder()
    {
        var matcher = new SerialMatcher();
        using var engine = new SceneTransitionEngine<string>(SerialOptions(), matcher);
        Task<SceneTransitionResult>? first = null;
        try
        {
            first = engine.ProcessAsync("frame-1").AsTask();
            await matcher.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = engine.ProcessAsync("frame-2").AsTask();

            Assert.IsFalse(matcher.LaterStarted.Task.IsCompleted, "The second frame must not enter the matcher before the first completes.");
            matcher.ReleaseFirst.TrySetResult();
            await Task.WhenAll(first, second);

            CollectionAssert.AreEqual(new[] { "frame-1", "frame-2" }, matcher.StartedFrames.ToArray());
            Assert.AreEqual(1, matcher.MaximumConcurrentCalls);
        }
        finally
        {
            matcher.ReleaseFirst.TrySetResult();
            if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestMethod]
    public async Task ProcessAsync_CancelledQueuedFrameDoesNotReleaseItsSuccessorEarly()
    {
        var matcher = new SerialMatcher();
        using var engine = new SceneTransitionEngine<string>(SerialOptions(), matcher);
        Task<SceneTransitionResult>? first = null;
        try
        {
            first = engine.ProcessAsync("frame-1").AsTask();
            await matcher.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var cancellation = new CancellationTokenSource();
            var cancelled = engine.ProcessAsync("frame-cancelled", cancellation.Token).AsTask();
            var third = engine.ProcessAsync("frame-3").AsTask();
            cancellation.Cancel();

            await ExpectCancellationAsync(cancelled);
            Assert.IsFalse(matcher.LaterStarted.Task.IsCompleted, "A cancelled queue entry must retain the barrier until its predecessor completes.");
            matcher.ReleaseFirst.TrySetResult();
            await Task.WhenAll(first, third);

            CollectionAssert.AreEqual(new[] { "frame-1", "frame-3" }, matcher.StartedFrames.ToArray());
            Assert.AreEqual(1, matcher.MaximumConcurrentCalls);
        }
        finally
        {
            matcher.ReleaseFirst.TrySetResult();
            if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestMethod]
    public async Task ResetAsync_IsQueuedClearsPendingAndValidatesNamedScene()
    {
        var matcher = new SerialMatcher();
        using var engine = new SceneTransitionEngine<string>(SerialOptions(knownFrames: 2), matcher);
        Task<SceneTransitionResult>? first = null;
        try
        {
            first = engine.ProcessAsync("frame-1").AsTask();
            await matcher.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var reset = engine.ResetAsync().AsTask();
            Assert.IsFalse(reset.IsCompleted, "Reset should wait asynchronously behind an accepted frame.");
            matcher.ReleaseFirst.TrySetResult();
            await Task.WhenAll(first, reset);

            Assert.AreEqual("unknown", engine.CurrentScene);
            Assert.IsNull(engine.PendingScene);
            Assert.AreEqual(0, engine.PendingConfirmationFrames);
            await engine.ResetAsync("selection");
            Assert.AreEqual("selection", engine.CurrentScene);
        }
        finally
        {
            matcher.ReleaseFirst.TrySetResult();
            if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.ThrowsException<ArgumentNullException>(() => { _ = engine.ResetAsync(null!); });
        Assert.ThrowsException<ArgumentException>(() => { _ = engine.ResetAsync(" "); });
        Assert.ThrowsException<ArgumentException>(() => { _ = engine.ResetAsync("missing"); });
    }

    [TestMethod]
    public async Task MatcherAndHandlerCannotEnqueueReentrantEngineOperations()
    {
        SceneTransitionEngine<string>? engine = null;
        Exception? matcherError = null;
        Exception? handlerError = null;
        var matcher = new StubMatcher((_, scene, _) =>
        {
            if (scene == "selection")
            {
                try { _ = engine!.ResetAsync(); }
                catch (Exception exception) { matcherError = exception; }
                return Match(scene, true);
            }
            return Match(scene, false);
        });
        var handler = new DelegateHandler<string>((_, _) =>
        {
            try { _ = engine!.ProcessAsync("reentrant"); }
            catch (Exception exception) { handlerError = exception; }
            return ValueTask.CompletedTask;
        });
        engine = new SceneTransitionEngine<string>(
            CreateOptions(),
            matcher,
            new Dictionary<string, ISceneHandler<string>> { ["selection"] = handler });
        using (engine)
        {
            var result = await engine.ProcessAsync("outer").AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsInstanceOfType<InvalidOperationException>(matcherError);
            Assert.IsInstanceOfType<InvalidOperationException>(handlerError);
            Assert.AreEqual("selection", result.CurrentScene);
        }
    }

    [TestMethod]
    public async Task ConfigurationHandlersAndResultsAreDefensivelyCopied()
    {
        var unknownCandidates = new List<string> { "selection" };
        var matrix = new Dictionary<string, IReadOnlyList<string>>
        {
            ["unknown"] = unknownCandidates,
            ["selection"] = new[] { "selection" }
        };
        var handlers = new Dictionary<string, ISceneHandler<string>>();
        var originalHandler = new CaptureHandler<string>();
        handlers["selection"] = originalHandler;
        var matcher = new StubMatcher((_, scene, _) => Match(scene, scene == "selection"));
        using var engine = new SceneTransitionEngine<string>(
            new SceneTransitionOptions("unknown", "unknown", matrix),
            matcher,
            handlers);

        unknownCandidates.Clear();
        unknownCandidates.Add("unknown");
        matrix.Clear();
        handlers.Clear();
        var first = await engine.ProcessAsync("frame-1");
        var evaluated = (IList<string>)first.EvaluatedScenes;
        Assert.ThrowsException<NotSupportedException>(() => evaluated.Add("mutated"));
        _ = await engine.ProcessAsync("frame-2");

        Assert.AreEqual("selection", first.CurrentScene);
        CollectionAssert.AreEqual(new[] { "selection" }, first.EvaluatedScenes.ToArray());
        Assert.AreEqual(2, originalHandler.Contexts.Count);
    }

    [TestMethod]
    public async Task ProcessAsync_RejectsMalformedMatcherResultsWithoutChangingState()
    {
        var invalidResults = new (SceneMatch? Match, string ExpectedText)[]
        {
            (new SceneMatch("wrong-scene", true, 0.5), "battle"),
            (new SceneMatch("battle", false, double.NaN), "battle"),
            (new SceneMatch("battle", true, double.PositiveInfinity), "battle"),
            (new SceneMatch("battle", true, double.NegativeInfinity), "battle"),
            (null, "battle")
        };
        foreach (var invalid in invalidResults)
        {
            var matcher = new StubMatcher((_, _, _) => invalid.Match!);
            using var engine = new SceneTransitionEngine<string>(CreateOptions(), matcher);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () => await engine.ProcessAsync("frame"));
            StringAssert.Contains(exception.Message, invalid.ExpectedText);
            Assert.AreEqual("unknown", engine.CurrentScene);
            Assert.IsNull(engine.PendingScene);
        }
    }

    [TestMethod]
    public void Options_RejectAllInvalidContractShapes()
    {
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions(" ", "unknown", SelfMatrix("unknown")));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("unknown", " ", SelfMatrix("unknown")));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("lobby", "unknown", SelfMatrix("unknown")));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("lobby", "unknown", SelfMatrix("lobby")));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("unknown", "unknown", new Dictionary<string, IReadOnlyList<string>> { ["unknown"] = null! }));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("unknown", "unknown", new Dictionary<string, IReadOnlyList<string>> { ["unknown"] = Array.Empty<string>() }));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("unknown", "unknown", new Dictionary<string, IReadOnlyList<string>> { ["unknown"] = new[] { " " } }));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("unknown", "unknown", new Dictionary<string, IReadOnlyList<string>> { ["unknown"] = new[] { "missing" } }));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionOptions("unknown", "unknown", new Dictionary<string, IReadOnlyList<string>> { ["unknown"] = new[] { "unknown", "unknown" } }));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateOptions(knownFrames: 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateOptions(knownFrames: -1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateOptions(unknownFrames: 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateOptions(unknownFrames: -1));

        var options = CreateOptions();
        var matcher = new StubMatcher((_, scene, _) => Match(scene, false));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionEngine<string>(options, matcher, new Dictionary<string, ISceneHandler<string>> { [" "] = new CaptureHandler<string>() }));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionEngine<string>(options, matcher, new Dictionary<string, ISceneHandler<string>> { ["lobby"] = null! }));
        Assert.ThrowsException<ArgumentException>(() => new SceneTransitionEngine<string>(options, matcher, new Dictionary<string, ISceneHandler<string>> { ["missing"] = new CaptureHandler<string>() }));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SelfMatrix(string scene) =>
        new Dictionary<string, IReadOnlyList<string>> { [scene] = new[] { scene } };

    private static SceneTransitionOptions CreateOptions(int knownFrames = 1, int unknownFrames = 2) =>
        new(
            "unknown",
            "unknown",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["unknown"] = new[] { "battle", "selection", "lobby" },
                ["selection"] = new[] { "selection", "battle", "lobby" },
                ["lobby"] = new[] { "lobby", "selection", "battle" },
                ["battle"] = new[] { "battle", "selection", "lobby" }
            },
            knownFrames,
            unknownFrames);

    private static SceneTransitionOptions SerialOptions(int knownFrames = 1) =>
        new(
            "unknown",
            "unknown",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["unknown"] = new[] { "selection" },
                ["selection"] = new[] { "selection" }
            },
            knownFrames);

    private static SceneMatch Match(string scene, bool isMatch, double confidence = 0, object? data = null) =>
        new(scene, isMatch, confidence, data);

    private static async Task ExpectCancellationAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Fail("The operation should have been cancelled.");
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException is an expected cancellation subtype.
        }
    }

    private sealed class StubMatcher(Func<string, string, CancellationToken, SceneMatch> match) : ISceneMatcher<string>
    {
        public List<string> EvaluatedScenes { get; } = [];

        public ValueTask<SceneMatch> MatchAsync(string frame, string candidateScene, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvaluatedScenes.Add(candidateScene);
            return ValueTask.FromResult(match(frame, candidateScene, cancellationToken));
        }
    }

    private sealed class StubMatcher<TFrame>(Func<TFrame, string, CancellationToken, SceneMatch> match) : ISceneMatcher<TFrame>
    {
        public ValueTask<SceneMatch> MatchAsync(TFrame frame, string candidateScene, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(match(frame, candidateScene, cancellationToken));
        }
    }

    private sealed class CaptureHandler<TFrame> : ISceneHandler<TFrame>
    {
        public List<SceneHandlerContext<TFrame>> Contexts { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask HandleAsync(SceneHandlerContext<TFrame> context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(context);
            Tokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelegateHandler<TFrame>(Func<SceneHandlerContext<TFrame>, CancellationToken, ValueTask> callback) : ISceneHandler<TFrame>
    {
        public ValueTask HandleAsync(SceneHandlerContext<TFrame> context, CancellationToken cancellationToken = default) =>
            callback(context, cancellationToken);
    }

    private sealed class BlockingHandler<TFrame> : ISceneHandler<TFrame>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public async ValueTask HandleAsync(SceneHandlerContext<TFrame> context, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class PendingThenBlockingMatcher : ISceneMatcher<string>
    {
        private int _frame;

        public TaskCompletionSource BlockingCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken BlockingToken { get; private set; }

        public async ValueTask<SceneMatch> MatchAsync(string frame, string candidateScene, CancellationToken cancellationToken = default)
        {
            if (_frame == 0)
            {
                if (candidateScene == "selection")
                {
                    _frame = 1;
                    return Match(candidateScene, true, 0.9);
                }
                return Match(candidateScene, false);
            }

            BlockingToken = cancellationToken;
            BlockingCallStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Match(candidateScene, false);
        }
    }

    private sealed class SerialMatcher : ISceneMatcher<string>
    {
        private int _activeCalls;
        private int _maximumConcurrentCalls;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LaterStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> StartedFrames { get; } = new();
        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

        public async ValueTask<SceneMatch> MatchAsync(string frame, string candidateScene, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(active);
            StartedFrames.Enqueue(frame);
            try
            {
                if (frame == "frame-1")
                {
                    FirstStarted.TrySetResult();
                    await ReleaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    LaterStarted.TrySetResult();
                }
                return Match(candidateScene, true, 0.9);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentCalls);
                if (value <= current || Interlocked.CompareExchange(ref _maximumConcurrentCalls, value, current) == current) return;
            }
        }
    }
}
