# Native 连招助手

## Purpose

导航 Native 连招流程、绑定、公共运行状态机、一次性 Hekili 导入、独立持久化、Windows 只读键鼠观察、游戏前台监视、三步浮窗和安全停止生命周期。连招助手只提示，不代替玩家操作。

## Entry points

- `src/Wuwa.Core/RotationModels.cs`、`RotationBindings.cs`：流程、步骤、队伍槽位、绑定和验证语义。
- `src/Wuwa.Core/RotationRunSession.cs`：START、Opener、Loop、Heavy、Intro、暂停/恢复、Reset/Stop 和三步 preview 的最高公共行为 seam。
- `src/Wuwa.Core/RotationRuntimeContracts.cs`：生产/脚本输入源和游戏窗口 monitor 共用的观察契约。
- `src/Wuwa.Infrastructure/HekiliRotationProfileImporter.cs`：wuwa-Hekili JSON 的显式、只读、一次性转换。
- `src/Wuwa.Infrastructure/RotationPersistence.cs`：当前 Native data root 下 `rotations/` 的版本化、原子 profile/settings store。
- `src/Wuwa.Infrastructure/WindowsRotationRuntime.cs`：可见游戏窗口发现、前台/客户区状态和忽略注入事件的低级键鼠 Hook。
- `src/Wuwa.App/RotationWorkbenchView.xaml.cs`、`RotationRuntimeCoordinator.cs`、`RotationOverlayWindow.xaml.cs`：配置 UI、启动/暂停/停止协调和 NoActivate/点击穿透浮窗。
- Tests/runtime：`tests/Wuwa.Tests/RotationTests.cs`、`scripts/verify-rotation-runtime.ps1`、提升权限的 UI/portable 检查和真实《鸣潮》物理输入 smoke。

## Boundaries

- Core 不依赖 WPF、JSON 文件或 Win32；测试以 `RotationRunSession` 公开 snapshot/state transition 为最高行为面。
- 流程和绑定位于当前 Native data root 的 `rotations/profiles/` 与 `rotations/settings.json`，不进入成就 generation；默认 data root 是 `<程序目录>\data`，可由 `WUWA_NATIVE_DATA_ROOT` 覆盖。
- Hekili 导入先完整验证再原子保存；源文件不修改、不监视、不双向同步，绝对/越界图标路径不持久化。
- 生产输入源只消费未标记为 injected 的键鼠事件；每个低级 Hook callback 始终调用 `CallNextHookEx`，不得依赖 OCR 的输入发送适配器。
- 只有已验证游戏窗口前台时才推进；Alt-Tab 暂停隐藏，游戏失效或 `Ctrl+Shift+F11` 停止并释放 Hook、关闭浮窗、恢复连招页。
- 生产 Rotation 路径不得调用 `SendInput`、`mouse_event`、输入型 `PostMessage`、`keybd_event` 或游戏进程内存 API。

## Read next

- 修改步骤语义先完整读取 `RotationRunSession.cs`、`RotationModels.cs` 和对应 public-session tests。
- 修改导入/存储时读 `HekiliRotationProfileImporter.cs`、`RotationPersistence.cs` 及原子失败/源文件不变测试。
- 修改 Windows 输入或前台逻辑时读 `RotationRuntimeContracts.cs`、`WindowsRotationRuntime.cs`、`RotationRuntimeCoordinator.cs` 和安全边界测试。
- 修改浮窗时读 `RotationOverlayWindow.xaml/.cs`，并运行提升权限的 visible-window smoke 与真实游戏 focus/pass-through checklist。
- 修改发布时读 `Wuwa.App.csproj`、`publish-native.ps1` 和 `verify-portable-lifecycle.ps1`，确认只读徽标进入包而用户流程/绑定不进入。
- `verified_against: commit:2486e5a5bedb0bc23468d152c08a1e43031d96a1`
