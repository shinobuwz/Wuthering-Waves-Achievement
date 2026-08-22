# Tasks：map-overlay-mvp

正文默认使用中文；路径、命令保持原文。

`tasks.md` 是执行合同，也是本 change 唯一的进度视图：普通复选框 + 简洁验收与验证，不重复 `spec.md` 的范围或设计。

## 独立任务文档索引

- `tasks/01-map-overlay-mvp.md`：地图覆盖层从入口、快捷键、游戏窗口定位到 WebView2 交互和生命周期的完整 vertical slice。

## 1. 地图覆盖层 MVP

- [x] 1.1 实现固定 Kuro 地图的无边框游戏窗口覆盖层
  - 详情：`tasks/01-map-overlay-mvp.md`
  - Suggested Files: `src/Wuwa.App/Wuwa.App.csproj`、`src/Wuwa.App/MainWindow.xaml`、`src/Wuwa.App/MainWindow.xaml.cs`、`src/Wuwa.App/MapOverlayWindow.xaml`、`src/Wuwa.App/MapOverlayWindow.xaml.cs`、`src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs`、`tests/Wuwa.Tests/`
  - 验收：主窗口按钮和全局快捷键可以显示/隐藏 WebView2 地图；地图覆盖游戏客户区并可交互；窗口移动、DPI、失焦、最小化、Esc 和应用退出行为符合 `spec.md`；缺失 WebView2/游戏窗口/快捷键时有明确降级行为；不修改成就工作区数据。
  - 验证：`dotnet test WutheringWavesAchievement.sln -c Release`、`dotnet build WutheringWavesAchievement.sln -c Release`，并完成 `tasks/01-map-overlay-mvp.md` 的真实 Windows 验收矩阵。
