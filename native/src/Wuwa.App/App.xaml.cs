using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var resourceDirectory = FindResourceDirectory();
            var source = new ShippedJsonAchievementLibrarySource(
                Path.Combine(resourceDirectory, "base_achievements.json"),
                Path.Combine(resourceDirectory, "category_config.json"));
            var dataRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_DATA_ROOT");
            var store = new JsonAppDataStore(dataRoot);
            var workspace = new AchievementWorkspace(store, source);
            await PrepareWorkspaceAsync(workspace, resourceDirectory, store.HasActiveState);

            var window = new MainWindow(workspace)
            {
                Owner = null
            };
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法启动成就工作区。\n\n{exception.Message}",
                "Wuthering Waves Achievement",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task PrepareWorkspaceAsync(AchievementWorkspace workspace, string resourceDirectory, bool hasActiveState)
    {
        if (hasActiveState)
        {
            var existing = await workspace.OpenAsync();
            if (!existing.IsSuccess) throw new InvalidDataException(existing.Error?.Message ?? "无法读取 native workspace。");
            return;
        }

        var opened = await workspace.OpenAsync(createEmptyIfMissing: false);
        if (!opened.IsSuccess)
        {
            throw new InvalidDataException(opened.Error?.Message ?? "无法读取 native workspace。");
        }

        var configPath = Path.Combine(resourceDirectory, "config.json");
        if (File.Exists(configPath))
        {
            var legacySource = new JsonLegacyProfileSource();
            var discovery = await workspace.DiscoverLegacyProfilesAsync(legacySource, configPath);
            if (discovery.Candidates.Count > 0)
            {
                var candidate = await SelectLegacyCandidateAsync(discovery);
                if (candidate is not null)
                {
                    var imported = await workspace.ImportLegacyProfileAsync(
                        legacySource,
                        new LegacyImportOptions(candidate, ConfirmReplace: true));
                    if (!imported.IsSuccess)
                    {
                        MessageBox.Show(
                            imported.Error?.Message ?? "旧版进度导入失败，将创建空的 native 进度。",
                            "旧版进度导入",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }

        var initialized = await workspace.OpenAsync(createEmptyIfMissing: true);
        if (!initialized.IsSuccess)
        {
            throw new InvalidDataException(initialized.Error?.Message ?? "无法初始化 native workspace。");
        }
    }

    private static async Task<LegacyProfileCandidate?> SelectLegacyCandidateAsync(LegacyDiscoveryResult discovery)
    {
        if (discovery.Status == LegacyDiscoveryStatus.Unambiguous)
        {
            var candidate = discovery.Candidates[0];
            var prompt = $"发现旧版进度：\n昵称：{candidate.Nickname}\nUID：{candidate.Uid}\n进度条目：{candidate.ProgressCount}\n\n是否导入？";
            return MessageBox.Show(prompt, "导入旧版进度", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                ? candidate
                : null;
        }

        var window = new Window
        {
            Title = "选择旧版进度",
            Width = 560,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var list = new ListBox { ItemsSource = discovery.Candidates, DisplayMemberPath = nameof(LegacyProfileCandidate.Nickname) };
        list.SelectedIndex = 0;
        DockPanel.SetDock(list, Dock.Top);
        panel.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消", Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        var confirm = new Button { Content = "导入选中", Width = 110 };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        confirm.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        cancel.Click += (_, _) => { window.DialogResult = false; window.Close(); };
        window.Content = panel;
        var result = window.ShowDialog();
        await Task.CompletedTask;
        return result == true ? list.SelectedItem as LegacyProfileCandidate : null;
    }

    private static string FindResourceDirectory()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            var resources = Path.Combine(directory.FullName, "resources");
            if (File.Exists(Path.Combine(resources, "base_achievements.json")) && File.Exists(Path.Combine(resources, "category_config.json")))
            {
                return resources;
            }
        }

        throw new FileNotFoundException("找不到 shipped resources 目录。");
    }
}
