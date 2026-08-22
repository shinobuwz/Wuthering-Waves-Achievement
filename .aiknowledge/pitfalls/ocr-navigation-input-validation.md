# OCR 导航和输入必须先验证环境与页面反馈

## 不要这样做

不要只依赖固定 Tab 坐标、假设一次点击一定切换成功，或把一批滚轮输入当作已经被游戏接收；也不要在窗口尺寸、前台焦点或进程完整性未知时启动全量扫描。

## 反例

直接按历史坐标遍历二级 Tab，固定滚动次数后把未看到的分类当作已扫描；或者在 Native 中把 `SendInput` 批量滚轮作为唯一路径，未检测游戏窗口状态就继续 OCR。

## 正例

先按允许的进程名发现可见主窗口并验证客户区尺寸；输入前恢复/激活窗口并聚焦游戏。一级 Tab 点击后用 OCR 验证名称，二级 Tab 从当前画面发现并按已知分类名做受阈值约束的匹配，访问集合去重并在连续无新内容时有界停止。滚轮或像素拖动必须与 Native/游戏实际行为一致，并保留必要 fallback；不能确认的分类记录为 warning 而不是伪造结果。

## 为什么不行

游戏 UI 的滚动惯性、窗口坐标、权限级别和可见 Tab 数量都会变化。点击或滚轮未生效时，扫描器可能重复同一页、跳过分类，或把错误页面的成就写入结果。当前项目的输入路径还受到 Windows 前台窗口和同等 integrity level 的约束。

## 适用前提

当任务涉及 Windows game capture、鼠标/滚轮自动化、OCR 全局 Tab 导航、1920×1080 布局或手工 smoke 时适用。不适用于纯文本匹配、离线 OCR unit test 或不发出桌面输入的 fake coordinator；这些测试仍要覆盖有界停止和失败报告。

## 验证

回读 `src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs`、`src/Wuwa.App/MainWindow.xaml.cs` 和 `%LocalAppData%/WutheringWavesAchievement/native-ocr.log` 的 capture/scroll 诊断。Native 侧运行 `WindowsGameWindowCaptureSmokeTests`、`OcrScanServiceTests`，并按 `openspec/changes/native-ocr-scan/tasks/04-global-navigation-and-matching.md` 检查 full-scan acceptance。

## 重审条件

当游戏 UI、分辨率策略、输入权限或 Native full-scan coordinator 完成并有 captured-image/manual differential evidence 后，重新验证滚轮参数、Tab discovery 阈值和终止条件；不要仅因单次运行成功就删除环境检查。
