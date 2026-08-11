using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class ShippedJsonAchievementLibrarySourceTests
{
    [TestMethod]
    public async Task LoadAsync_ReadsAllShippedRowsAndCategoryConfiguration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = new ShippedJsonAchievementLibrarySource(
            Path.Combine(repositoryRoot, "resources", "base_achievements.json"),
            Path.Combine(repositoryRoot, "resources", "category_config.json"));

        var library = await source.LoadAsync();

        Assert.AreEqual(958, library.Achievements.Count);
        Assert.AreEqual(958, library.Achievements.Select(item => item.Id).Distinct().Count());
        Assert.AreEqual(958, library.Achievements.Select(item => item.LegacyCode).Distinct().Count());
        Assert.AreEqual(4, library.Categories.FirstCategories.Count);
        Assert.AreEqual(0, library.Achievements.Count(item => item.GroupId is not null));
        Assert.AreEqual("10100001", library.Achievements[0].LegacyCode);
        Assert.AreEqual("往日之音·今州 Ⅰ", library.Achievements[0].Name);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "resources", "base_achievements.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate repository resources from the test output directory.");
        return string.Empty;
    }
}
