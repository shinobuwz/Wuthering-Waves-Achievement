# 01 — 地图覆盖层 MVP

**What to build:** 完成从 Native 主窗口入口到游戏客户区 WebView2 覆盖层的单一 vertical slice：添加 WebView2 依赖，定位现有游戏窗口客户区，创建可复用的无边框地图窗口，注册 `Alt+M`/`Ctrl+Alt+M` 全局快捷键，处理显示隐藏、页面交互、窗口跟随、失焦隐藏、运行时缺失和退出释放。

**Blocked by:** None — can start immediately

**Suggested Files:**

- `src/Wuwa.App/Wuwa.App.csproj`
- `src/Wuwa.App/MainWindow.xaml`
- `src/Wuwa.App/MainWindow.xaml.cs`
- `src/Wuwa.App/MapOverlayWindow.xaml`
- `src/Wuwa.App/MapOverlayWindow.xaml.cs`
- `src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs`
- `src/Wuwa.Core/OcrScanContracts.cs`（仅在需要扩展结构化窗口边界契约时导航）
- `tests/Wuwa.Tests/`
- `scripts/publish-native.ps1`

**Acceptance:**

1. Native 应用启动后，主窗口提供打开地图入口，并显示当前生效快捷键或可理解的不可用状态。
2. 游戏以无边框/窗口化全屏运行且 WebView2 Runtime 可用时，点击入口或按 `Alt+M` 能在游戏客户区显示固定 Kuro 地图；再次按生效快捷键或 `Esc` 能隐藏。
3. 地图窗口无边框、不出现在任务栏，WebView2 可以拖动、缩放和点击；地图隐藏后游戏重新接收输入。
4. 游戏窗口移动、改变大小或发生 DPI/显示器变化时，覆盖层保持与客户区一致；游戏最小化、不可见或切出时覆盖层自动隐藏，且不遮挡其他应用。
5. `Alt+M` 注册失败时使用 `Ctrl+Alt+M`；游戏窗口不可用、WebView2 Runtime 缺失或两个快捷键都不可用时，应用不崩溃且按钮仍提供明确错误/替代路径。
6. 主应用退出后无残留置顶窗口、热键或 WebView2 资源；成就工作区快照、设置和 legacy 文件没有被写入或修改。

**Verification:**

- `dotnet test WutheringWavesAchievement.sln -c Release`：所有现有测试通过。
- `dotnet build WutheringWavesAchievement.sln -c Release`：构建通过。
- `powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Configuration Release`：发布流程通过。
- 真实 Windows 手工场景：100%/125%/150% DPI；单显示器/多显示器；游戏移动/调整大小；地图拖动和滚轮缩放；`Alt+M` 正常与冲突回退；`Esc`；Alt+Tab；最小化；WebView2 Runtime 缺失；独占全屏；主应用退出。每个场景记录 pass/fail 和残余风险到当前实现反馈中。

## Behavior Context

- 地图覆盖层是独立的 WPF 顶层窗口，不是游戏进程内绘制层。
- 对齐目标是游戏 HWND 的客户区屏幕矩形；如果无法获得有效矩形，必须失败并提示，不得猜测桌面全屏位置。
- 地图需要接收输入，因此不采用点击穿透；覆盖层显示时游戏暂时不作为输入目标是可接受的 MVP 行为。
- `RegisterHotKey` 只负责全局组合键通知，不承诺对游戏 Raw Input 做绝对屏蔽；MVP 的低冲突策略是组合键和回退组合键，而不是低级键盘钩子。

## Feedback / Review

- Direct feedback：`dotnet build WutheringWavesAchievement.sln -c Release`、`dotnet test WutheringWavesAchievement.sln -c Release`、Native Release 发布脚本和 `scripts/verify-ui.ps1` 均通过；发布包包含 `WebView2Loader.dll` 和地图入口 AutomationId。
- Spec review：实现保持独立 WPF/WebView2 顶层窗口、Infrastructure 客户区定位、`Alt+M` 回退、失焦隐藏和退出释放边界；未引入 Hook、注入或 `AchievementWorkspace` 数据写入。
- Residual：当前环境未运行真实《鸣潮》窗口，因此 DPI/多显示器/独占全屏/实际地图网页交互和目标机 WebView2 Runtime 缺失提示仍需 `opsx-verify` 手工验证。
