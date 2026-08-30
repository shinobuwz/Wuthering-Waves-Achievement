# OCR 导航和输入必须先验证环境与页面反馈

## 不要这样做

不要只依赖固定 Tab 坐标、假设一次点击一定切换成功，或把一批滚轮输入当作已经被游戏接收；也不要在窗口尺寸、前台焦点或进程完整性未知时启动全量扫描。不要让只读连招助手复用 OCR 的输入发送适配器。

## 反例

直接按历史坐标遍历二级 Tab，固定滚动次数后把未看到的分类当作已扫描；或者在 Native 中把 OCR 的 `SendInput`/滚轮作为无条件路径，未检测游戏窗口状态就继续。另一个反例是为了复用窗口代码，让 Rotation 调用 `WindowsGameWindowCapture` 的点击、滚动或键盘发送方法。

## 正例

OCR 扫描先按允许的进程名发现可见主窗口并验证客户区尺寸；输入前恢复/激活窗口并聚焦游戏。一级 Tab 点击后用 OCR 验证名称，二级 Tab 从当前画面发现并按已知分类名做受阈值约束的匹配，访问集合去重并在连续无新内容时有界停止。滚轮或像素拖动必须与游戏实际行为一致，并保留必要 fallback；不能确认的分类记录为 warning 而不是伪造结果。Rotation 则使用独立的只读 Hook/foreground contract，忽略注入事件并始终调用 `CallNextHookEx`。

## 为什么不行

游戏 UI 的滚动惯性、窗口坐标、权限级别和可见 Tab 数量都会变化。点击或滚轮未生效时，扫描器可能重复同一页、跳过分类，或把错误页面的成就写入结果。OCR 的自动导航与 Rotation 的只读观察拥有相反的输入职责；混用会破坏“只提示、不代替操作”的安全边界。

## 适用前提

当任务涉及 Windows game capture、鼠标/滚轮自动化、OCR 全局 Tab 导航、1920×1080 布局、同等 integrity level、连招 Hook 边界或手工 smoke 时适用。不适用于纯文本匹配、离线 OCR unit test 或不发出桌面输入的 fake coordinator；这些测试仍要覆盖有界停止和失败报告。

## 验证

回读 `src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs`、`src/Wuwa.App/MainWindow.xaml.cs` 和 `<程序目录>\log\native-ocr-YYYY-MM-DD.log` 的 capture/scroll 诊断。OCR 侧运行 `WindowsGameWindowCaptureSmokeTests`、`OcrScanServiceTests` 和相关导航测试；Rotation 侧运行 `RotationSafetyBoundaryTests`、`scripts/verify-rotation-runtime.ps1`，并在真实《鸣潮》中确认物理输入透传、错误动作不推进和无自动操作。

## 重审条件

当游戏 UI、分辨率策略、输入权限、OCR full-scan coordinator 或 Windows Hook 策略改变并有 captured-image/manual differential evidence 后，重新验证滚轮参数、Tab discovery 阈值、终止条件和只读输入边界；不要仅因单次运行成功就删除环境检查。
