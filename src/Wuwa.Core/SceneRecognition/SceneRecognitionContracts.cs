using System.Collections.ObjectModel;

namespace Wuwa.Core;

/// <summary>
/// Immutable scene-transition configuration. Candidate order is significant:
/// the first matching scene wins for the current frame.
/// </summary>
public sealed class SceneTransitionOptions
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _transitionMatrix;

    public SceneTransitionOptions(
        string initialScene,
        string unknownScene,
        IReadOnlyDictionary<string, IReadOnlyList<string>> transitionMatrix,
        int transitionConfirmationFrames = 1,
        int unknownConfirmationFrames = 2)
    {
        if (string.IsNullOrWhiteSpace(initialScene)) throw new ArgumentException("Initial scene cannot be blank.", nameof(initialScene));
        if (string.IsNullOrWhiteSpace(unknownScene)) throw new ArgumentException("Unknown scene cannot be blank.", nameof(unknownScene));
        ArgumentNullException.ThrowIfNull(transitionMatrix);
        if (transitionConfirmationFrames <= 0) throw new ArgumentOutOfRangeException(nameof(transitionConfirmationFrames));
        if (unknownConfirmationFrames <= 0) throw new ArgumentOutOfRangeException(nameof(unknownConfirmationFrames));

        InitialScene = initialScene.Trim();
        UnknownScene = unknownScene.Trim();
        TransitionConfirmationFrames = transitionConfirmationFrames;
        UnknownConfirmationFrames = unknownConfirmationFrames;

        var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in transitionMatrix)
        {
            var scene = string.IsNullOrWhiteSpace(pair.Key)
                ? throw new ArgumentException("Scene-transition keys cannot be blank.", nameof(transitionMatrix))
                : pair.Key.Trim();
            if (pair.Value is null || pair.Value.Count == 0)
            {
                throw new ArgumentException($"Scene '{scene}' must define at least one candidate scene.", nameof(transitionMatrix));
            }

            var candidates = pair.Value.Select(candidate => string.IsNullOrWhiteSpace(candidate)
                    ? throw new ArgumentException($"Scene '{scene}' contains a blank candidate.", nameof(transitionMatrix))
                    : candidate.Trim())
                .ToArray();
            if (candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            {
                throw new ArgumentException($"Scene '{scene}' contains duplicate candidates.", nameof(transitionMatrix));
            }
            if (!copy.TryAdd(scene, Array.AsReadOnly(candidates)))
            {
                throw new ArgumentException($"Scene '{scene}' has more than one transition row.", nameof(transitionMatrix));
            }
        }

        if (!copy.ContainsKey(InitialScene)) throw new ArgumentException($"Initial scene '{InitialScene}' is missing from the transition matrix.", nameof(transitionMatrix));
        if (!copy.ContainsKey(UnknownScene)) throw new ArgumentException($"Unknown scene '{UnknownScene}' is missing from the transition matrix.", nameof(transitionMatrix));

        var missingTargets = copy.Values
            .SelectMany(candidates => candidates)
            .Where(candidate => !copy.ContainsKey(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missingTargets.Length > 0)
        {
            throw new ArgumentException($"Candidate scenes are missing transition rows: {string.Join(", ", missingTargets)}.", nameof(transitionMatrix));
        }

        _transitionMatrix = new ReadOnlyDictionary<string, IReadOnlyList<string>>(copy);
    }

    public string InitialScene { get; }
    public string UnknownScene { get; }
    public int TransitionConfirmationFrames { get; }
    public int UnknownConfirmationFrames { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TransitionMatrix => _transitionMatrix;

    public bool ContainsScene(string scene) =>
        !string.IsNullOrWhiteSpace(scene) && _transitionMatrix.ContainsKey(scene.Trim());

    public IReadOnlyList<string> GetCandidates(string currentScene)
    {
        if (string.IsNullOrWhiteSpace(currentScene)) throw new ArgumentException("Current scene cannot be blank.", nameof(currentScene));
        var normalized = currentScene.Trim();
        return _transitionMatrix.TryGetValue(normalized, out var candidates)
            ? candidates
            : throw new InvalidOperationException($"Scene '{normalized}' has no transition row.");
    }
}

/// <summary>A single matcher observation for one candidate scene.</summary>
public sealed record SceneMatch(
    string Scene,
    bool IsMatch,
    double Confidence = 0,
    object? Data = null);

/// <summary>
/// Detects one candidate scene. Implementations can use template matching, OCR,
/// pixel rules, or test fixtures; the transition engine owns candidate ordering.
/// </summary>
public interface ISceneMatcher<in TFrame>
{
    ValueTask<SceneMatch> MatchAsync(
        TFrame frame,
        string candidateScene,
        CancellationToken cancellationToken = default);
}

/// <summary>Context supplied to an explicitly registered scene handler.</summary>
public sealed record SceneHandlerContext<TFrame>(
    TFrame Frame,
    string PreviousScene,
    string CurrentScene,
    SceneMatch Match,
    bool IsTransitionConfirmed);

/// <summary>
/// Optional caller-specific callback invoked after a real candidate scene match.
/// </summary>
public interface ISceneHandler<TFrame>
{
    ValueTask HandleAsync(
        SceneHandlerContext<TFrame> context,
        CancellationToken cancellationToken = default);
}

/// <summary>Immutable result of processing one frame through the transition engine.</summary>
public sealed record SceneTransitionResult(
    string PreviousScene,
    string CurrentScene,
    string DetectedScene,
    bool IsTransitionConfirmed,
    string? PendingScene,
    int PendingConfirmationFrames,
    IReadOnlyList<string> EvaluatedScenes,
    double Confidence,
    bool HandlerInvoked);
