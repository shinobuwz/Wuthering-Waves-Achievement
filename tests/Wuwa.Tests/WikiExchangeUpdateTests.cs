using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class WikiExchangeUpdateTests
{
    [TestMethod]
    public async Task WikiFetch_ParsesHierarchyAndStableReferences()
    {
        const string html = "<details class='kr-collapse-details'><summary class='kr-collapse-summary'>索拉漫行</summary><table class='kr-table-filter' data-uid='table-a'><tr data-freeze='row'><td>名称</td><td>版本</td><td>合集</td><td>描述</td><td>奖励</td></tr><tr data-index='4' data-filter-tag='版本-1.0,合集-区域·一'><td>「隐藏成就」晨光</td><td>1.0</td><td>fallback</td><td>找到地标</td><td>星声*5</td></tr></table><table data-uid='table-b'><tr data-index='4'><td>回响</td><td>1.1</td><td>技巧</td><td>完成挑战</td><td>星声*10</td></tr></table></details>";
        using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, Envelope(html)));
        var result = await new KuroWikiAchievementSource(client).FetchAsync();

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(2, result.Achievements.Count);
        Assert.AreEqual("索拉漫行", result.Achievements[0].FirstCategory);
        Assert.AreEqual("fallback", result.Achievements[0].SecondCategory);
        Assert.IsTrue(result.Achievements[0].IsHidden);
        CollectionAssert.AreEquivalent(
            new[] { $"{KuroWikiAchievementSource.EntryId}/table-a/4", $"{KuroWikiAchievementSource.EntryId}/table-b/4" },
            result.Achievements.Select(row => row.WikiSourceRef).ToArray());
    }

    [TestMethod]
    public async Task WikiFetch_SplitsChoiceRowsIntoStableGroupedAchievements()
    {
        const string html = "<details class='kr-collapse-details'><summary class='kr-collapse-summary'>长路留迹</summary><table class='kr-table-filter' data-uid='choice-table'><tr data-freeze='row'><td>名称</td><td>版本</td><td>合集</td><td>描述</td><td>奖励</td></tr><tr data-index='18502' data-filter-tag='版本-3.0,合集-世间百态&amp;middot;二,特殊-隐藏成就,特殊-二选一'><td><p>「隐藏成就」独自凝望的星海</p><p>或</p><p>「隐藏成就」恒定不变的星海</p></td><td>3.0</td><td>fallback</td><td><p>选择告知真相。</p><p>或</p><p>选择隐瞒真相。</p></td><td>星声*5</td></tr></table></details>";
        using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, Envelope(html)));

        var result = await new KuroWikiAchievementSource(client).FetchAsync();

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(2, result.Achievements.Count);
        CollectionAssert.AreEqual(
            new[] { "独自凝望的星海", "恒定不变的星海" },
            result.Achievements.Select(item => item.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "选择告知真相。", "选择隐瞒真相。" },
            result.Achievements.Select(item => item.Description).ToArray());
        Assert.IsTrue(result.Achievements.All(item => item.IsHidden));
        Assert.AreEqual("fallback", result.Achievements[0].SecondCategory);
        Assert.AreEqual(result.Achievements[0].GroupId, result.Achievements[1].GroupId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Achievements[0].GroupId));
        CollectionAssert.AreEqual(
            new[]
            {
                $"{KuroWikiAchievementSource.EntryId}/choice-table/18502/choice-1",
                $"{KuroWikiAchievementSource.EntryId}/choice-table/18502/choice-2"
            },
            result.Achievements.Select(item => item.WikiSourceRef).ToArray());
        Assert.AreNotEqual(result.Achievements[0].Id, result.Achievements[1].Id);
    }

    [TestMethod]
    public async Task WikiFetch_InfersProgressionGroupFromTieredRows()
    {
        const string html = "<details><summary>探索</summary><table data-uid='progression-table'><tr data-freeze='row'><td>名称</td><td>版本</td><td>合集</td><td>描述</td><td>奖励</td></tr><tr data-index='1'><td>累计采集·一</td><td>1.0</td><td>区域</td><td>采集20次。</td><td>星声*5</td></tr><tr data-index='2'><td>累计采集·二</td><td>1.0</td><td>区域</td><td>采集50次。</td><td>星声*10</td></tr><tr data-index='3'><td>累计采集·三</td><td>1.0</td><td>区域</td><td>采集100次。</td><td>星声*20</td></tr></table></details>";
        using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, Envelope(html)));

        var result = await new KuroWikiAchievementSource(client).FetchAsync();

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(3, result.Achievements.Count);
        Assert.IsTrue(result.Achievements.All(item => item.GroupId?.StartsWith("progression-", StringComparison.Ordinal) == true));
        Assert.AreEqual(1, result.Achievements.Select(item => item.GroupId).Distinct().Count());
    }

    [TestMethod]
    public async Task WikiFetch_RejectsMalformedChoiceRows()
    {
        const string html = "<details><summary>长路留迹</summary><table data-uid='choice-table'><tr data-index='1' data-filter-tag='版本-3.0,合集-世间百态&amp;middot;二,特殊-二选一'><td><p>成就甲</p><p>或</p><p>成就乙</p></td><td>3.0</td><td>fallback</td><td><p>只有一段描述</p></td><td>星声*5</td></tr></table></details>";
        using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, Envelope(html)));

        var result = await new KuroWikiAchievementSource(client).FetchAsync();

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error, "matching achievement names and descriptions");
    }

    [TestMethod]
    public async Task WikiFetch_HttpSuccessBusinessFailureIsRejected()
    {
        using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, "{\"code\":500,\"success\":false,\"msg\":\"denied\",\"data\":{}}"));
        var result = await new KuroWikiAchievementSource(client).FetchAsync();
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error, "code 500");
    }

    [TestMethod]
    public async Task SyncWiki_AmbiguousFullSignatureIsQuarantinedWithoutMutation()
    {
        var a = Achievement("1", "same-ref-a");
        var b = Achievement("2", "same-ref-b");
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([a, b], Categories())));
        var opened = await workspace.OpenAsync();
        var remote = Achievement("remote", "new-ref");

        var result = await workspace.SyncWikiAsync(new FixedWikiSource(remote), new WikiSyncOptions(MinimumPlausibleRowCount: 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkspaceErrorCode.WikiRejected, result.Error?.Code);
        Assert.AreEqual(opened.Snapshot.Revision, result.Snapshot.Revision);
        Assert.AreEqual(2, result.Snapshot.Rows.Count);
    }

    [TestMethod]
    public async Task SyncWiki_LegacyBootstrapFallbackRetainsIdentityAndProgressAcrossCategoryChange()
    {
        var local = Achievement("401") with { FirstCategory = "旧分类", SecondCategory = "旧区域" };
        var categories = new CategoryCatalog(new Dictionary<string, int> { ["旧分类"] = 1 }, new Dictionary<string, IReadOnlyDictionary<string, int>> { ["旧分类"] = new Dictionary<string, int> { ["旧区域"] = 1 } });
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([local], categories)));
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(local.Id, ProgressStatus.Completed);
        var remote = Achievement("remote", "new-ref");

        var result = await workspace.SyncWikiAsync(new FixedWikiSource(remote), new WikiSyncOptions(MinimumPlausibleRowCount: 1));

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.AreEqual(local.Id, result.Snapshot.Rows.Single().Id);
        Assert.AreEqual("401", result.Snapshot.Rows.Single().LegacyCode);
        Assert.AreEqual(ProgressStatus.Completed, result.Snapshot.Rows.Single().Status);
    }

    [TestMethod]
    public async Task SyncWiki_UpdatesCategoryLabelsAndRetainsUiCompatibilityAliases()
    {
        var local = new Achievement(AchievementId.FromLegacyCode("401"), "401", 1, "1.0", "长路留迹", "旧标签", "名称", "描述", "星声*5", false);
        var categories = new CategoryCatalog(
            new Dictionary<string, int> { ["长路留迹"] = 1 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["长路留迹"] = new Dictionary<string, int>
                {
                    ["漂泊之旅"] = 10,
                    ["漂泊之旅·一"] = 10,
                    ["旧标签"] = 20
                }
            });
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([local], categories)));
        await workspace.OpenAsync();

        var remote = local with
        {
            FirstCategory = "长路留迹",
            SecondCategory = "漂泊之旅",
            WikiSourceRef = "new-ref"
        };
        var result = await workspace.SyncWikiAsync(new FixedWikiSource(remote), new WikiSyncOptions(MinimumPlausibleRowCount: 1));

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.AreEqual("漂泊之旅", result.Snapshot.Rows.Single().SecondCategory);
        var labels = result.Snapshot.Categories.SecondCategories["长路留迹"];
        Assert.IsTrue(labels.ContainsKey("漂泊之旅"));
        Assert.IsTrue(labels.ContainsKey("漂泊之旅·一"));
        Assert.IsFalse(labels.ContainsKey("旧标签"));
    }

    [TestMethod]
    public async Task JsonExchange_ReadsEnglishAliasesAndRejectsConflicts()
    {
        await WithRoot(async root =>
        {
            var path = Path.Combine(root, "full.json");
            await File.WriteAllTextAsync(path, "[{\"absoluteOrder\":1,\"version\":\"1.0\",\"firstCategory\":\"探索\",\"secondCategory\":\"区域一\",\"legacyCode\":\"401\",\"name\":\"名称·测试\",\"description\":\"描述，Unicode\",\"reward\":\"星声*5\",\"isHidden\":true,\"status\":\"已完成\",\"groupId\":\"\",\"mutualExclusionCodes\":[] }]");
            var payload = await new JsonAchievementExchange(path).ReadAsync();
            Assert.AreEqual("名称·测试", payload.Achievements.Single().Name);
            Assert.IsTrue(payload.Achievements.Single().IsHidden);
            Assert.AreEqual(ProgressStatus.Completed, payload.Progress["401"]);

            await File.WriteAllTextAsync(path, "[{\"编号\":\"401\",\"legacyCode\":\"999\",\"版本\":\"1.0\",\"第一分类\":\"探索\",\"第二分类\":\"区域一\",\"名称\":\"名称\",\"描述\":\"描述\"}]");
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new JsonAchievementExchange(path).ReadAsync());
        });
    }

    [TestMethod]
    public async Task ExchangeImport_InvalidCandidateReturnsDiagnosticsAndDoesNotSave()
    {
        var valid = Achievement("401");
        var store = new CountingStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([valid], Categories())));
        await workspace.OpenAsync();
        var savesBefore = store.SaveCount;
        var invalid = valid with { Name = "", SecondCategory = "未知" };

        var result = await workspace.ImportExchangeAsync(new FixedExchangeSource(new ExchangePayload(ExchangeDocumentKind.FullJson, [invalid], new Dictionary<string, ProgressStatus> { ["401"] = ProgressStatus.Completed })), true, true);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.EffectiveDiagnostics.Any(diagnostic => diagnostic.Field == "name"));
        Assert.IsTrue(result.EffectiveDiagnostics.Any(diagnostic => diagnostic.Field == "secondCategory"));
        Assert.AreEqual(savesBefore, store.SaveCount);
    }

    [TestMethod]
    public async Task NonReplacementProgressImportPreservesUnspecifiedStatuses()
    {
        var first = Achievement("401") with { Name = "first", Description = "first" };
        var second = Achievement("402") with { Name = "second", Description = "second", AbsoluteOrder = 2 };
        var store = new InMemoryAppDataStore();
        var workspace = new AchievementWorkspace(store, new FixedAchievementLibrarySource(new AchievementLibrary([first, second], Categories())));
        await workspace.OpenAsync();
        await workspace.ChangeStatusAsync(first.Id, ProgressStatus.Completed);
        await workspace.ChangeStatusAsync(second.Id, ProgressStatus.Unavailable);

        var result = await workspace.ImportExchangeAsync(new FixedExchangeSource(new ExchangePayload(ExchangeDocumentKind.ProgressJson, Array.Empty<Achievement>(), new Dictionary<string, ProgressStatus> { ["401"] = ProgressStatus.Incomplete })), replace: false, confirmReplace: false);

        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        Assert.AreEqual(ProgressStatus.Incomplete, result.Snapshot.Rows.Single(row => row.Id == first.Id).Status);
        Assert.AreEqual(ProgressStatus.Unavailable, result.Snapshot.Rows.Single(row => row.Id == second.Id).Status);
    }

    [TestMethod]
    public async Task XlsxExportAndImportRoundTripsContractedFields()
    {
        await WithRoot(async root =>
        {
            var path = Path.Combine(root, "exchange.xlsx");
            var achievement = new Achievement(AchievementId.FromLegacyCode("401"), "401", 1, "1.0", "探索", "区域一", "名称·测试", "描述，Unicode", "星声*5", true);
            var state = new WorkspaceState(1, [achievement], new Dictionary<AchievementId, ProgressStatus> { [achievement.Id] = ProgressStatus.Completed }, Categories());
            await new ExcelAchievementExchange(path).WriteAsync(state);
            using (var workbook = ZipFile.OpenRead(path))
                Assert.IsTrue(workbook.Entries.Any(entry => entry.FullName == "xl/worksheets/sheet1.xml"));
            var payload = await new ExcelAchievementExchange(path).ReadAsync();
            Assert.AreEqual(achievement.Name, payload.Achievements.Single().Name);
            Assert.AreEqual(ProgressStatus.Completed, payload.Progress["401"]);
        });
    }

    [TestMethod]
    public async Task XlsxImport_AcceptsV1CrawlerSevenColumnLayout()
    {
        await WithRoot(async root =>
        {
            var path = Path.Combine(root, "v1-crawler.xlsx");
            WriteInlineXlsx(path,
                ["名称", "描述", "奖励", "版本", "是否隐藏", "第一分类", "第二分类"],
                ["「隐藏成就」旧版成就", "完成旧版任务。", "5", "3", "", "探索", "区域一"]);

            var workspace = new AchievementWorkspace(
                new InMemoryAppDataStore(),
                new FixedAchievementLibrarySource(new AchievementLibrary([Achievement("existing")], Categories())));
            await workspace.OpenAsync();

            var imported = await workspace.ImportExchangeAsync(
                new ExcelAchievementExchange(path),
                replace: true,
                confirmReplace: true);

            Assert.IsTrue(imported.IsSuccess, imported.Error?.Message);
            var row = imported.Snapshot.Rows.Single();
            Assert.AreEqual("旧版成就", row.Name);
            Assert.AreEqual("3.0", row.Version);
            Assert.AreEqual("星声*5", row.Reward);
            Assert.IsTrue(row.IsHidden);
            Assert.IsTrue(row.LegacyCode.StartsWith("legacy-", StringComparison.Ordinal));
            Assert.AreEqual(ProgressStatus.Incomplete, row.Status);
        });
    }

    [TestMethod]
    public void ExchangeFactory_UsesExplicitExtensions()
    {
        Assert.IsInstanceOfType<JsonAchievementExchange>(AchievementExchangeFactory.CreateImport("a.json"));
        Assert.IsInstanceOfType<ExcelAchievementExchange>(AchievementExchangeFactory.CreateImport("a.xlsx"));
        Assert.ThrowsException<NotSupportedException>(() => AchievementExchangeFactory.CreateImport("a.csv"));
    }

    [TestMethod]
    public async Task UpdateChecker_ComparesVersionsUsesCacheAndRejectsUnsafeUrl()
    {
        await WithRoot(async root =>
        {
            var cache = Path.Combine(root, "cache.json");
            using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, "{\"tag_name\":\"v1.2.3\",\"html_url\":\"https://github.com/shinobuwz/Wuthering-Waves-Achievement/releases/tag/v1.2.3\"}"));
            var checker = new GitHubUpdateChecker(httpClient: client, cachePath: cache, clock: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var result = await checker.CheckAsync("1.2.0");
            Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.IsFalse(result.IsCached);

            var cached = await checker.CheckAsync("1.2.0");
            Assert.IsTrue(cached.IsCached);
            Assert.IsFalse(checker.IsTrustedReleaseUrl("http://github.com/shinobuwz/Wuthering-Waves-Achievement/releases/tag/v1"));
            Assert.IsFalse(checker.IsTrustedReleaseUrl("https://evil.example/releases/tag/v1"));
        });
    }

    [TestMethod]
    public async Task UpdateChecker_MalformedResponseFallsBackToValidatedCache()
    {
        await WithRoot(async root =>
        {
            var cache = Path.Combine(root, "cache.json");
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (var firstClient = new HttpClient(new FixtureHandler(HttpStatusCode.OK, "{\"tag_name\":\"v1.2.3\",\"html_url\":\"https://github.com/shinobuwz/Wuthering-Waves-Achievement/releases/tag/v1.2.3\"}")))
                await new GitHubUpdateChecker(httpClient: firstClient, cachePath: cache, clock: () => now, cacheTtl: TimeSpan.Zero).CheckAsync("1.2.0");
            var malformedHandler = new FixtureHandler(HttpStatusCode.OK, "{}");
            using var malformedClient = new HttpClient(malformedHandler);
            var fallback = await new GitHubUpdateChecker(httpClient: malformedClient, cachePath: cache, clock: () => now.AddHours(2), cacheTtl: TimeSpan.FromHours(1), maximumFallbackAge: TimeSpan.FromDays(1)).CheckAsync("1.2.0");
            Assert.AreEqual(1, malformedHandler.RequestCount);
            Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, fallback.Status);
            Assert.IsTrue(fallback.IsCached);
        });
    }

    private static string Envelope(string html) => System.Text.Json.JsonSerializer.Serialize(new { code = 200, success = true, msg = "ok", data = new { lastUpdateTime = "now", content = new { modules = new[] { new { components = new[] { new { type = "filter-component", content = html } } } } } } });
    private static Achievement Achievement(string code, string? source = null) => new(AchievementId.FromLegacyCode(code), code, 1, "1.0", "探索", "区域一", "同名", "同描述", "星声*5", false, WikiSourceRef: source);
    private static CategoryCatalog Categories() => new(new Dictionary<string, int> { ["探索"] = 1 }, new Dictionary<string, IReadOnlyDictionary<string, int>> { ["探索"] = new Dictionary<string, int> { ["区域一"] = 1 } });

    private static async Task WithRoot(Func<string, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "wuwa-native-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await action(root); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void WriteInlineXlsx(string path, params string[][] rows)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var stream = entry.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetData = new XElement(ns + "sheetData");
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = new XElement(ns + "row", new XAttribute("r", rowIndex + 1));
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                var cellReference = ColumnName(columnIndex) + (rowIndex + 1);
                row.Add(new XElement(
                    ns + "c",
                    new XAttribute("r", cellReference),
                    new XAttribute("t", "inlineStr"),
                    new XElement(ns + "is", new XElement(ns + "t", rows[rowIndex][columnIndex]))));
            }
            sheetData.Add(row);
        }

        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(ns + "worksheet", sheetData)).ToString(SaveOptions.DisableFormatting));
    }

    private static string ColumnName(int index)
    {
        var result = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
        {
            result = (char)('A' + (value - 1) % 26) + result;
        }
        return result;
    }

    private sealed class FixtureHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class FixedWikiSource(params Achievement[] rows) : IWikiAchievementSource
    {
        public Task<WikiFetchResult> FetchAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WikiFetchResult(true, rows));
    }
    private sealed class FixedExchangeSource(ExchangePayload payload) : IAchievementImportSource
    {
        public Task<ExchangePayload> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(payload);
    }
    private sealed class CountingStore : IAppDataStore
    {
        private WorkspaceState? _state;
        public int SaveCount { get; private set; }
        public Task<WorkspaceState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default) { SaveCount++; _state = state; return Task.CompletedTask; }
    }
}
