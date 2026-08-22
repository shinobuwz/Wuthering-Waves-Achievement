# Working Checkpoint

## Change

- change_id: `map-overlay-mvp`
- branch: `feat/map-overlay-mvp`
- owner: Main

## Snapshot

- base/HEAD: `a91b06b` (`Improve status filter and colors`)
- scope fingerprint: `implement-map-overlay-mvp-v2-default-map-entry`
- declared scope: Native WPF/WebView2 地图覆盖层、游戏客户区定位、全局快捷键和运行时降级

## Frontier

- current task: `1.1` — 地图覆盖层 MVP
- status: `done-for-verify`
- blocked-by: `None`

## Completed

- 已添加 `Microsoft.Web.WebView2` 依赖和 Kuro 地图初始入口 URL；不再固化个人坐标、缩放或 `items` 参数。
- 已实现 `MapOverlayWindow`：无边框、置顶、无任务栏、WebView2 交互、Esc 隐藏和页面状态复用。
- 已扩展 `WindowsGameWindowCapture`：客户区屏幕边界、前台判断和游戏窗口重新激活。
- 已在 `MainWindow` 接入地图按钮、`Alt+M`/`Ctrl+Alt+M` 注册回退、`WM_HOTKEY`、200ms 边界跟随、失焦/最小化自动隐藏和退出清理。
- 已将 UI smoke 检查扩展到 `MapOverlayButton`。
- 已完成一次 Main inline Spec/Code review；未发现需要改变 scope 或方案的 blocker。

## Open Findings

- `F-001`（medium，open）：发布包已包含 `WebView2Loader.dll`，但目标机 WebView2 Runtime 缺失时的真实提示路径尚未在无 Runtime 环境验证。
- `F-002`（medium，open）：真实《鸣潮》窗口的 DPI、多显示器、窗口移动、无边框全屏和权限等级组合尚未运行时验证。
- `F-003`（low，open）：`RegisterHotKey` 是否被其他软件占用只能在目标机运行时确定；实现保留回退快捷键和按钮入口。
- `F-004`（medium，open）：当前环境未运行真实地图网页，WebView2 页面加载、拖动、滚轮缩放和初始入口实际响应尚未在应用内验证。

## Latest Verification

- `dotnet build WutheringWavesAchievement.sln -c Release --no-restore`：通过，0 warning / 0 error。
- `dotnet test WutheringWavesAchievement.sln -c Release`：通过，61 passed / 3 skipped。
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Configuration Release -SkipTests`：通过；OCR 构建 2/2 native tests passed，发布包生成成功，包含 `WebView2Loader.dll`。
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-ui.ps1 -OutputDirectory artifacts/ui-map-mvp`：通过；4 张截图生成且新增地图入口 AutomationId 可达。
- residual risks: F-001、F-002、F-003、F-004

## Next Packet

- 交给 `opsx-verify`：读取本 checkpoint、当前 diff、`spec.md`、`tasks.md` 和任务详情，执行 fresh Spec/Code review，并在可用环境完成真实 Windows 地图覆盖验收。
- acceptance: 验证按钮/快捷键、WebView2 页面、客户区对齐、跟随、失焦隐藏、Esc/再次打开和退出回收；记录无法执行的真实游戏场景及残余风险。
- stop conditions: 发现需要独占全屏 Hook/注入、低级键盘钩子或改变已确认用户边界时停止并返回 decision checkpoint。

## Do Not Repeat

- 不要再次使用 `openspec-cn` 或旧的 spec-driven artifact 流程；本 change 使用项目自有 OPSX 的 `spec.md`/`tasks.md`/`evidence.md`。
- 不要把地图覆盖层实现放入 `AchievementWorkspace`，也不要修改 legacy 文件或 generation 数据格式。
- 不要把独占全屏支持、低级键盘钩子和游戏进程注入作为隐含的“兼容性修复”。
- 构建、测试、发布和 UI smoke 已完成；下一轮优先验证真实 Windows/WebView2 行为，不重复机械回归。
