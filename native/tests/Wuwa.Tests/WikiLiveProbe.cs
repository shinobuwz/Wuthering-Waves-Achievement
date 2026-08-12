using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class WikiLiveProbe
{
    [TestMethod]
    public async Task LiveWiki_TemporaryWorkspaceSyncsTwiceWithoutIdentityChurn()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("WUWA_RUN_LIVE_WIKI"), "1", StringComparison.Ordinal))
            Assert.Inconclusive("Set WUWA_RUN_LIVE_WIKI=1 to run the isolated live Wiki probe.");
        var root = Path.Combine(Path.GetTempPath(), "wuwa-live-wiki", Guid.NewGuid().ToString("N"));
        try { Assert.IsTrue(await RunAsync(root) > 900); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static async Task<int> RunAsync(string root, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var repositoryRoot = FindRepositoryRoot();
        var source = new ShippedJsonAchievementLibrarySource(
            Path.Combine(repositoryRoot, "resources", "base_achievements.json"),
            Path.Combine(repositoryRoot, "resources", "category_config.json"));
        var workspace = new AchievementWorkspace(new JsonAppDataStore(root), source);
        var opened = await workspace.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) throw new InvalidDataException(opened.Error?.Message);
        var first = await workspace.SyncWikiAsync(new KuroWikiAchievementSource(), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!first.IsSuccess) throw new InvalidDataException(first.Error?.Message);
        var second = await workspace.SyncWikiAsync(new KuroWikiAchievementSource(), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!second.IsSuccess) throw new InvalidDataException(second.Error?.Message);
        if (second.Snapshot.Revision != first.Snapshot.Revision) throw new InvalidDataException("Equivalent second Wiki sync advanced the workspace revision.");
        return second.Snapshot.Rows.Count;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "resources", "base_achievements.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository resources were not found.");
    }
}
