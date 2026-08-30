using System.Text;
using System.Text.Json;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class RotationProfileAndBindingTests
{
    [TestMethod]
    public void BindingValidation_ReportsDuplicatesAndMissingActions()
    {
        var profile = TestProfiles.BasicLoop();
        var same = new RotationPhysicalInput(RotationInputDevice.Keyboard, 65);
        var bindings = new RotationBindingSet(new[]
        {
            new RotationBinding(RotationBindingAction.Start, same),
            new RotationBinding(RotationBindingAction.Reset, same)
        });

        var result = RotationBindingValidator.Validate(profile, bindings);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "bindings.duplicate"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "bindings.required"));
    }

    [TestMethod]
    public void ProfileValidation_RejectsEmptySequenceAndInvalidIntro()
    {
        var empty = new RotationProfile(RotationProfileId.New(), "Empty", new[] { new RotationCharacterSlot(1, "A") }, 1, Array.Empty<RotationStep>(), Array.Empty<RotationStep>());
        var invalidIntro = new RotationProfile(RotationProfileId.New(), "Intro", new[] { new RotationCharacterSlot(1, "A") }, 1, new[] { new RotationStep(RotationActionKind.Intro, "switch", TargetSlot: 2) }, Array.Empty<RotationStep>());

        Assert.IsFalse(RotationProfileValidator.Validate(empty).IsValid);
        Assert.IsFalse(RotationProfileValidator.Validate(invalidIntro).IsValid);
    }
}

[TestClass]
public sealed class HekiliRotationProfileImporterTests
{
    [TestMethod]
    public void Import_MapsActionsAliasesVariantsAndStripsAbsoluteIcons()
    {
        const string json = """
        {
          "name":"Demo",
          "team_config":{"1":"A","2":"B","3":"C"},
          "team_aliases":{"1":"Alpha","2":"","3":"Gamma"},
          "initial_char_index":1,
          "opener_script":[
            {"type":"basic","desc":"hit","variant":"强化_1","custom_icon":"D:\\legacy\\hit.png"},
            {"type":"intro","desc":"switch","next_char":2}
          ],
          "loop_script":[{"type":"ult","desc":"burst"}]
        }
        """;
        using var document = JsonDocument.Parse(json);

        var result = new HekiliRotationProfileImporter().Import(document.RootElement, "source");

        Assert.IsTrue(result.IsSuccess, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsNotNull(result.Profile);
        Assert.AreEqual("Alpha", result.Profile.Team[0].Alias);
        Assert.AreEqual("强化_1", result.Profile.Opener[0].Variant);
        Assert.IsNull(result.Profile.Opener[0].IconReference);
        Assert.AreEqual(RotationActionKind.Liberation, result.Profile.Loop[0].Action);
        Assert.IsTrue(result.Warnings.Any(issue => issue.Code == "import.icon.stripped"));
    }

    [TestMethod]
    public void Import_RejectsBlankNameAndMalformedJson()
    {
        const string blankName = """
        {"name":"","team_config":{"1":"A"},"team_aliases":{},"initial_char_index":1,
         "opener_script":[{"type":"basic","desc":"hit"}],"loop_script":[]}
        """;
        using var document = JsonDocument.Parse(blankName);
        var blank = new HekiliRotationProfileImporter().Import(document.RootElement, "source");
        Assert.IsFalse(blank.IsSuccess);
        Assert.IsTrue(blank.Errors.Any(issue => issue.Code == "import.name"));
    }

    [TestMethod]
    public async Task ImportFile_ReportsMalformedJsonWithoutWriting()
    {
        var root = TestDirectory.Create();
        try
        {
            var source = Path.Combine(root, "broken.json");
            await File.WriteAllTextAsync(source, "{broken");
            var store = new CountingRotationStore();
            var result = await new RotationProfileImportService(store).ImportAsync(source);
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Errors.Any(issue => issue.Code == "import.read"));
            Assert.AreEqual(0, store.SaveCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Import_RejectsUnknownActionAndInvalidSlots()
    {
        const string json = """
        {"name":"Bad","team_config":{"4":"A"},"team_aliases":{},"initial_char_index":4,
         "opener_script":[{"type":"magic","desc":"bad"}],"loop_script":[]}
        """;
        using var document = JsonDocument.Parse(json);

        var result = new HekiliRotationProfileImporter().Import(document.RootElement, "bad");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Profile);
        Assert.IsTrue(result.Errors.Count >= 2);
    }

    [TestMethod]
    public async Task ImportFile_IsReadOnlyAndInvalidImportDoesNotSave()
    {
        var root = TestDirectory.Create();
        try
        {
            var source = Path.Combine(root, "source.json");
            await File.WriteAllTextAsync(source, "{\"name\":\"Bad\",\"team_config\":{},\"team_aliases\":{},\"initial_char_index\":1,\"opener_script\":[],\"loop_script\":[]}", Encoding.UTF8);
            var before = await File.ReadAllBytesAsync(source);
            var write = File.GetLastWriteTimeUtc(source);
            var store = new CountingRotationStore();

            var result = await new RotationProfileImportService(store).ImportAsync(source);

            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(source));
            Assert.AreEqual(write, File.GetLastWriteTimeUtc(source));
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(0, store.SaveCount);
        }
        finally { Directory.Delete(root, true); }
    }
}

[TestClass]
public sealed class JsonRotationStoreTests
{
    [TestMethod]
    public async Task ProfileAndSettings_RoundTripUnderIndependentRotationsRoot()
    {
        var root = TestDirectory.Create();
        try
        {
            var profile = TestProfiles.BasicLoop();
            var profiles = new JsonRotationProfileStore(root);
            var settingsStore = new JsonRotationSettingsStore(root);
            var settings = RotationSettings.Default with { SelectedProfileId = profile.Id };

            await profiles.SaveAsync(profile);
            await settingsStore.SaveAsync(settings);
            var loaded = await profiles.GetAsync(profile.Id);
            var listed = await profiles.ListAsync();
            var loadedSettings = await settingsStore.LoadAsync();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(profile.Name, loaded.Name);
            Assert.AreEqual(1, listed.Profiles.Count);
            Assert.AreEqual(profile.Id, loadedSettings.SelectedProfileId);
            Assert.IsTrue(File.Exists(Path.Combine(root, "rotations", "profiles", $"{profile.Id.Value:N}.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "rotations", "settings.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "generations")));
            Assert.AreEqual(0, Directory.EnumerateFiles(Path.Combine(root, "rotations"), "*.tmp", SearchOption.AllDirectories).Count());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ReplacementFailure_PreservesExistingProfileAndCleansTemporaryFile()
    {
        var root = TestDirectory.Create();
        try
        {
            var original = TestProfiles.BasicLoop();
            await new JsonRotationProfileStore(root).SaveAsync(original);
            var replacement = new RotationProfile(original.Id, "Replacement", original.Team, original.InitialSlot, original.Opener, original.Loop);
            var failing = new JsonRotationProfileStore(root, new ThrowingRotationStoreFaultInjector(RotationStoreCheckpoint.BeforeProfileReplacement));

            await Assert.ThrowsExceptionAsync<IOException>(() => failing.SaveAsync(replacement));

            var loaded = await new JsonRotationProfileStore(root).GetAsync(original.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(original.Name, loaded.Name);
            Assert.AreEqual(0, Directory.EnumerateFiles(Path.Combine(root, "rotations"), "*.tmp", SearchOption.AllDirectories).Count());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SettingsReplacementFailure_PreservesExistingSettings()
    {
        var root = TestDirectory.Create();
        try
        {
            var original = RotationSettings.Default;
            await new JsonRotationSettingsStore(root).SaveAsync(original);
            var replacement = original with { HeavyThreshold = TimeSpan.FromMilliseconds(900) };
            var failing = new JsonRotationSettingsStore(root, new ThrowingRotationStoreFaultInjector(RotationStoreCheckpoint.BeforeSettingsReplacement));

            await Assert.ThrowsExceptionAsync<IOException>(() => failing.SaveAsync(replacement));

            var loaded = await new JsonRotationSettingsStore(root).LoadAsync();
            Assert.AreEqual(original.HeavyThreshold, loaded.HeavyThreshold);
            Assert.AreEqual(0, Directory.EnumerateFiles(Path.Combine(root, "rotations"), "*.tmp", SearchOption.AllDirectories).Count());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task List_ReportsCorruptProfileWithoutHidingValidProfiles()
    {
        var root = TestDirectory.Create();
        try
        {
            var store = new JsonRotationProfileStore(root);
            await store.SaveAsync(TestProfiles.BasicLoop());
            var directory = Path.Combine(root, "rotations", "profiles");
            await File.WriteAllTextAsync(Path.Combine(directory, "corrupt.json"), "not json");

            var result = await store.ListAsync();

            Assert.AreEqual(1, result.Profiles.Count);
            Assert.AreEqual(1, result.Issues.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}

[TestClass]
public sealed class RotationRunSessionTests
{
    [TestMethod]
    public void StartOpenerLoop_UsesExactPressReleaseAndWrapsPreview()
    {
        var profile = TestProfiles.OpenerAndLoop();
        var bindings = TestProfiles.Bindings();
        var session = new RotationRunSession(profile, bindings, TimeSpan.FromMilliseconds(500));
        session.Start();
        Assert.AreEqual(RotationRunStatus.AwaitingStart, session.Snapshot.Status);
        Assert.AreEqual("START", session.Snapshot.Preview[0].Description);

        Press(session, RotationBindingAction.Start, bindings);
        Assert.AreEqual(RotationRunPhase.Opener, session.Snapshot.Phase);
        var basic = bindings.Bindings[RotationBindingAction.Basic];
        Assert.IsTrue(session.Receive(new(basic, true)).Accepted);
        Assert.IsFalse(session.Receive(new(bindings.Bindings[RotationBindingAction.Skill], false)).Advanced);
        Assert.IsTrue(session.Receive(new(basic, false)).Advanced);
        Assert.AreEqual(RotationRunPhase.Loop, session.Snapshot.Phase);
        Assert.AreEqual(RotationActionKind.Skill, session.Snapshot.Preview[0].Action);

        Complete(session, RotationBindingAction.Skill, bindings);
        Assert.AreEqual(RotationActionKind.Intro, session.Snapshot.Preview[0].Action);
        Complete(session, RotationBindingAction.Intro2, bindings);
        Assert.AreEqual(2, session.Snapshot.CurrentCharacterSlot);
        Assert.AreEqual(RotationActionKind.Skill, session.Snapshot.Preview[0].Action);
    }

    [TestMethod]
    public void PreviewAndDiagnostics_ExposeThreeStepsWithoutChangingSequence()
    {
        var profile = TestProfiles.OpenerAndLoop();
        var bindings = TestProfiles.Bindings();
        var session = new RotationRunSession(profile, bindings, TimeSpan.FromMilliseconds(500));
        session.Start();
        CollectionAssert.AreEqual(new[] { "START", "open", "skill" }, session.Snapshot.Preview.Select(item => item.Description).ToArray());
        Press(session, RotationBindingAction.Start, bindings);
        CollectionAssert.AreEqual(new[] { "open", "skill", "switch" }, session.Snapshot.Preview.Select(item => item.Description).ToArray());
        var before = session.Snapshot.Preview.Select(item => item.Description).ToArray();

        var wrong = session.Receive(new(new RotationPhysicalInput(RotationInputDevice.Keyboard, 0x5A), true));

        Assert.IsFalse(wrong.Advanced);
        Assert.AreEqual("input.wrongPress", wrong.DiagnosticCode);
        CollectionAssert.AreEqual(before, wrong.Snapshot.Preview.Select(item => item.Description).ToArray());
    }

    [TestMethod]
    public void RepeatedDownAndReset_ClearPendingAction()
    {
        var profile = TestProfiles.OpenerAndLoop();
        var bindings = TestProfiles.Bindings();
        var session = new RotationRunSession(profile, bindings, TimeSpan.FromMilliseconds(500));
        session.Start();
        Press(session, RotationBindingAction.Start, bindings);
        var basic = bindings.Bindings[RotationBindingAction.Basic];
        Assert.IsTrue(session.Receive(new(basic, true)).Accepted);
        var repeated = session.Receive(new(basic, true));
        Assert.IsFalse(repeated.Advanced);
        Assert.AreEqual("input.repeat", repeated.DiagnosticCode);

        Press(session, RotationBindingAction.Reset, bindings);
        Assert.AreEqual(RotationRunPhase.Start, session.Snapshot.Phase);
        Press(session, RotationBindingAction.Start, bindings);
        var staleRelease = session.Receive(new(basic, false));
        Assert.IsFalse(staleRelease.Advanced);
        Assert.AreEqual("input.unexpectedRelease", staleRelease.DiagnosticCode);
        Assert.AreEqual("open", staleRelease.Snapshot.Preview[0].Description);
    }

    [TestMethod]
    public void Heavy_ShortHoldClearsStateAndMatchingLongHoldAdvances()
    {
        var clock = new ManualTimeProvider();
        var profile = TestProfiles.HeavyOnly();
        var bindings = TestProfiles.Bindings();
        var session = new RotationRunSession(profile, bindings, TimeSpan.FromMilliseconds(500), clock);
        session.Start();
        Press(session, RotationBindingAction.Start, bindings);
        var basic = bindings.Bindings[RotationBindingAction.Basic];

        session.Receive(new(basic, true));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.IsFalse(session.Receive(new(basic, false)).Advanced);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsFalse(session.Receive(new(basic, false)).Advanced);
        session.Receive(new(basic, true));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.IsTrue(session.Receive(new(basic, false)).Advanced);
        Assert.AreEqual(RotationRunStatus.Finished, session.Snapshot.Status);
    }

    [TestMethod]
    public void PauseResumeResetAndStop_PreservePublicSemantics()
    {
        var profile = TestProfiles.OpenerAndLoop();
        var bindings = TestProfiles.Bindings();
        var session = new RotationRunSession(profile, bindings, TimeSpan.FromMilliseconds(500));
        session.Start();
        Press(session, RotationBindingAction.Start, bindings);
        session.SetGameForeground(false);
        var paused = session.Snapshot;
        Assert.AreEqual(RotationRunStatus.Paused, paused.Status);
        Assert.IsFalse(session.Receive(new(bindings.Bindings[RotationBindingAction.Basic], true)).Advanced);
        session.SetGameForeground(true);
        Assert.AreEqual(RotationRunStatus.Running, session.Snapshot.Status);
        session.Reset();
        Assert.AreEqual(profile.InitialSlot, session.Snapshot.CurrentCharacterSlot);
        Assert.AreEqual(RotationRunPhase.Start, session.Snapshot.Phase);
        session.Stop();
        var revision = session.Snapshot.Revision;
        session.Stop();
        Assert.AreEqual(revision, session.Snapshot.Revision);
    }

    private static void Press(RotationRunSession session, RotationBindingAction action, RotationBindingSet bindings) =>
        session.Receive(new(bindings.Bindings[action], true));

    private static void Complete(RotationRunSession session, RotationBindingAction action, RotationBindingSet bindings)
    {
        var input = bindings.Bindings[action];
        session.Receive(new(input, true));
        Assert.IsTrue(session.Receive(new(input, false)).Advanced);
    }
}

[TestClass]
public sealed class RotationRuntimeContractTests
{
    [TestMethod]
    public void ScriptedInputSource_UsesSameObservedInputContractAsProduction()
    {
        using IRotationInputSource source = new ScriptedRotationInputSource();
        RotationObservedInput? observed = null;
        source.InputObserved += (_, input) => observed = input;
        source.Start();
        ((ScriptedRotationInputSource)source).Emit(new(new(RotationInputDevice.Keyboard, 65), true), new nint(1234));

        Assert.IsNotNull(observed);
        Assert.AreEqual(new nint(1234), observed.ForegroundWindow);
        Assert.AreEqual(65, observed.Input.Input.Code);
        Assert.IsTrue(source.Stop(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void ScriptedSourceAndFakeMonitor_DriveThePublicSessionSeam()
    {
        var profile = TestProfiles.BasicLoop();
        var bindings = TestProfiles.Bindings();
        var session = new RotationRunSession(profile, bindings, TimeSpan.FromMilliseconds(500));
        session.Start();
        var game = new RotationGameWindow(new nint(1234), 1, "game", "game");
        var monitor = new FakeRotationGameMonitor();
        using var source = new ScriptedRotationInputSource();
        RotationInputResult? last = null;
        source.InputObserved += (_, observed) =>
        {
            var state = monitor.ReadState(game);
            if (RotationRuntimeInputGate.CanAccept(observed, game, state)) last = session.Receive(observed.Input);
        };
        source.Start();

        source.Emit(new(bindings.Bindings[RotationBindingAction.Start], true), game.Handle);
        Assert.AreEqual(RotationRunStatus.Running, session.Snapshot.Status);
        source.Emit(new(new(RotationInputDevice.Keyboard, 0x5A), true), game.Handle);
        Assert.IsNotNull(last);
        Assert.IsFalse(last.Advanced);
        var basic = bindings.Bindings[RotationBindingAction.Basic];
        source.Emit(new(basic, true), game.Handle);
        source.Emit(new(basic, false), game.Handle);
        Assert.IsTrue(last.Advanced);
    }

    [TestMethod]
    public void InputGate_RequiresForegroundAtObservationAndConsumptionTime()
    {
        var game = new RotationGameWindow(new nint(100), 1, "game", "game");
        var currentForeground = new RotationGameWindowState(true, true, false, true, new(game.Handle, 0, 0, 800, 600));
        var currentBackground = currentForeground with { IsForeground = false };
        var input = new RotationInputEvent(new(RotationInputDevice.Keyboard, 65), true);

        Assert.IsFalse(RotationRuntimeInputGate.CanAccept(new(input, new nint(200)), game, currentForeground));
        Assert.IsFalse(RotationRuntimeInputGate.CanAccept(new(input, game.Handle), game, currentBackground));
        Assert.IsTrue(RotationRuntimeInputGate.CanAccept(new(input, game.Handle), game, currentForeground));
    }

    [TestMethod]
    public async Task FakeMonitor_UsesSharedWindowStateContract()
    {
        IRotationGameMonitor monitor = new FakeRotationGameMonitor();
        var window = await monitor.TryFindAsync(new[] { "test" });
        Assert.IsNotNull(window);
        var state = monitor.ReadState(window);
        Assert.IsTrue(state.Exists);
        Assert.IsTrue(state.IsForeground);
        Assert.AreEqual(800, state.Bounds?.Width);
    }
}

[TestClass]
public sealed class WindowsRotationInputSourceLifecycleTests
{
    [TestMethod]
    public void StartThenImmediateStop_ConfirmsHookThreadAndHandlesAreReleased()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows low-level Hook lifecycle test.");
        using var source = new WindowsRotationInputSource();
        source.Start();
        Assert.IsTrue(source.IsRunning);
        Assert.IsTrue(source.Stop(TimeSpan.FromSeconds(3)));
        Assert.IsFalse(source.IsRunning);
    }

    [TestMethod]
    public void Stop_RetriesWhenInitialQuitPostIsUnavailable()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows low-level Hook lifecycle test.");
        var attempts = new List<int>();
        using var source = new WindowsRotationInputSource(attempt =>
        {
            attempts.Add(attempt);
            return attempt > 1;
        });
        source.Start();

        Assert.IsTrue(source.Stop(TimeSpan.FromSeconds(3)));
        CollectionAssert.AreEqual(new[] { 1, 2 }, attempts);
        Assert.IsFalse(source.IsRunning);
    }
}

[TestClass]
public sealed class RotationSafetyBoundaryTests
{
    [TestMethod]
    public void RotationProductionPaths_DoNotReferenceInputSendingOrProcessInjectionApis()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*Rotation*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToArray();
        var scopes = files.ToDictionary(file => file, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        var mainWindowPath = Path.Combine(root, "src", "Wuwa.App", "MainWindow.xaml.cs");
        var mainWindow = File.ReadAllText(mainWindowPath);
        scopes["MainWindow.StartRotationAsync"] = ExtractScope(mainWindow, "private async Task StartRotationAsync", "private void RestoreFromRotation");
        scopes["MainWindow.HideMapOverlayForRotation"] = ExtractScope(mainWindow, "private void HideMapOverlayForRotation", "private void ShowMapError");
        var forbidden = new[] { "SendInput", "mouse_event", "keybd_event", "PostMessage", "ReadProcessMemory", "WriteProcessMemory", "OpenProcess", "CreateRemoteThread", "WindowsGameWindowCapture", "IGameWindowCapture" };
        foreach (var scope in scopes)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(scope.Value.Contains(token, StringComparison.Ordinal), $"Forbidden Rotation dependency '{token}' in {scope.Key}.");
        }

        var inputSource = File.ReadAllText(Path.Combine(root, "src", "Wuwa.Infrastructure", "WindowsRotationRuntime.cs"));
        Assert.IsTrue(inputSource.Contains("CallNextHookEx", StringComparison.Ordinal));
        Assert.IsTrue(inputSource.Contains("KeyboardInjected", StringComparison.Ordinal));
        Assert.IsTrue(inputSource.Contains("MouseInjected", StringComparison.Ordinal));
    }

    private static string ExtractScope(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, $"Unable to locate safety scope {startMarker}.");
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WutheringWavesAchievement.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}

internal static class TestProfiles
{
    internal static RotationProfile BasicLoop() => new(RotationProfileId.New(), "Basic", new[] { new RotationCharacterSlot(1, "A") }, 1, Array.Empty<RotationStep>(), new[] { new RotationStep(RotationActionKind.Basic, "basic") });
    internal static RotationProfile HeavyOnly() => new(RotationProfileId.New(), "Heavy", new[] { new RotationCharacterSlot(1, "A") }, 1, new[] { new RotationStep(RotationActionKind.Heavy, "heavy") }, Array.Empty<RotationStep>());
    internal static RotationProfile OpenerAndLoop() => new(
        RotationProfileId.New(), "Demo",
        new[] { new RotationCharacterSlot(1, "A"), new RotationCharacterSlot(2, "B") }, 1,
        new[] { new RotationStep(RotationActionKind.Basic, "open") },
        new[] { new RotationStep(RotationActionKind.Skill, "skill"), new RotationStep(RotationActionKind.Intro, "switch", TargetSlot: 2) });
    internal static RotationBindingSet Bindings() => RotationBindingSet.CreateDefaults();
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}

internal sealed class CountingRotationStore : IRotationProfileStore
{
    public int SaveCount { get; private set; }
    public Task DeleteAsync(RotationProfileId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<RotationProfile?> GetAsync(RotationProfileId id, CancellationToken cancellationToken = default) => Task.FromResult<RotationProfile?>(null);
    public Task<RotationProfileLoadResult> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RotationProfileLoadResult(Array.Empty<RotationProfile>(), Array.Empty<RotationIssue>()));
    public Task SaveAsync(RotationProfile profile, CancellationToken cancellationToken = default) { SaveCount++; return Task.CompletedTask; }
}

internal sealed class ScriptedRotationInputSource : IRotationInputSource
{
    public event EventHandler<RotationObservedInput>? InputObserved;
    public bool IsRunning { get; private set; }
    public void Start() => IsRunning = true;
    public void Emit(RotationInputEvent input, nint foregroundWindow)
    {
        if (!IsRunning) throw new InvalidOperationException("Scripted source is not running.");
        InputObserved?.Invoke(this, new(input, foregroundWindow));
    }
    public bool Stop(TimeSpan timeout) { IsRunning = false; return true; }
    public void Dispose() => Stop(TimeSpan.FromSeconds(1));
}

internal sealed class FakeRotationGameMonitor : IRotationGameMonitor
{
    private readonly RotationGameWindow _window = new(new nint(1234), 1, "test", "test");
    public Task<RotationGameWindow?> TryFindAsync(IReadOnlyCollection<string> processNames, int minimumWidth = 800, int minimumHeight = 600, CancellationToken cancellationToken = default) => Task.FromResult<RotationGameWindow?>(_window);
    public RotationGameWindowState ReadState(RotationGameWindow window) => new(true, true, false, true, new(window.Handle, 10, 20, 800, 600));
    public bool TryActivate(RotationGameWindow window) => true;
}

internal sealed class ThrowingRotationStoreFaultInjector : IRotationStoreFaultInjector
{
    private readonly RotationStoreCheckpoint _checkpoint;
    internal ThrowingRotationStoreFaultInjector(RotationStoreCheckpoint checkpoint) => _checkpoint = checkpoint;
    public void OnCheckpoint(RotationStoreCheckpoint checkpoint)
    {
        if (checkpoint == _checkpoint) throw new IOException($"Injected Rotation store failure at {checkpoint}.");
    }
}

internal static class TestDirectory
{
    internal static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "wuwa-rotation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
