# 鸣潮成就管理器

作者 Bilibili：[蓝若惜miss](https://www.bilibili.com/video/BV13e826hEJQ/?spm_id_from=333.1387.homepage.video_card.click)

本仓库目前只维护 **Native WPF/.NET 8 版本**。项目源码、Native OCR、模型和构建脚本均位于仓库根目录，不再使用额外的 `native/` 目录层级。

Python/PySide6 版本及其打包脚本、OCR 运行时已移除，后续功能和 OCR 导航只在 Native 代码中实现。

## 快速开始

```powershell
dotnet restore WutheringWavesAchievement.sln
dotnet test WutheringWavesAchievement.sln -c Release
dotnet build WutheringWavesAchievement.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Configuration Release
```

Visual Studio 用户打开根目录的 `WutheringWavesAchievement.sln`，启动项目选择 `Wuwa.App`。

发布目录为 `publish/win-x64/`。脚本同时读取 `src/Wuwa.App/Wuwa.App.csproj` 的 `Version`，生成版本化压缩包 `publish/WutheringWavesAchievement-vx.x.x.zip`（当前版本例如 `WutheringWavesAchievement-v2.1.0.zip`）。

## 项目结构

```text
├── src/
│   ├── Wuwa.App/             # WPF 主程序
│   ├── Wuwa.Core/             # 工作区、数据和业务逻辑
│   └── Wuwa.Infrastructure/  # OCR、截图、输入、存储适配器
├── ocr/                      # C++ Native OCR 和 C ABI
├── models/ppocrv5/           # Native OCR 模型与字典
├── tests/                    # .NET 测试
├── scripts/                  # 构建、发布和验证脚本
├── resources/                # 成就库、分类配置和 OCR 模板
└── WutheringWavesAchievement.sln
```

## 当前功能

- 本地成就数据加载、搜索、筛选、排序和虚拟化表格
- 成就状态管理、成就组状态转换和统计
- generation 事务化本地存储、故障恢复和原子激活
- JSON、TSV、XLSX 导入导出
- 兼容 v1.0.0 爬虫导出的 7 列 XLSX（自动生成稳定兼容编号并默认标记为未完成）
- Kuro Wiki 数据同步与稳定身份匹配
- Native PP-OCRv5 检测、分类、识别和 OCR 结果预览
- 当前分类 OCR 扫描及全量分类 OCR 扫描入口
- 增量扫描未完成成就；高置信完成结果进入最新扫描结果列表，经人工勾选后统一应用
- “校准与输入设置”内提供安全的搜索框输入测试，并可调整聚焦、修饰键、按键、全选和粘贴等待时间；测试不执行搜索或修改进度
- 左侧模块导航、总览、成就管理、连招助手、游戏工具、设置和使用帮助
- Native 连招流程：Hekili JSON 一次性只读导入、独立原子存储、键鼠绑定和三步提示浮窗
- 深色/浅色主题和便携发布

## 数据位置

- 随程序发布的只读资源：`resources/base_achievements.json`、`resources/category_config.json`、`resources/ocr_templates/`
- Native 可变工作区：默认位于 `<程序目录>\data`；设置 `WUWA_NATIVE_DATA_ROOT` 时使用指定目录
- 连招流程与绑定：`<dataRoot>\rotations\profiles\` 和 `<dataRoot>\rotations\settings.json`，与成就 generation 完全独立
- Native OCR 诊断日志：`<程序目录>\log\native-ocr-YYYY-MM-DD.log`

游戏窗口若以管理员权限运行，Native 应用也会请求管理员权限，以保证 Windows 窗口观察、OCR 输入适配器和键鼠 Hook 处于相同完整性级别。

## 连招助手安全边界

连招助手是“**只提示、不代替操作**”的前台辅助模块：它只观察用户真实键盘／鼠标按下与松开事件，匹配当前步骤后更新三步提示浮窗。它不调用输入注入接口、不读取或写入游戏进程内存，也不会自动执行任何游戏动作。低级 Hook 始终把事件继续传递给游戏。

- 启动前必须选择有效流程、补齐当前流程所需绑定，并找到可见、未最小化的《鸣潮》客户区。
- 运行时主窗口隐藏；浮窗为 Topmost、NoActivate、点击穿透，不应夺取游戏焦点。
- 仅游戏窗口位于前台时推进。Alt-Tab 会暂停并隐藏浮窗，返回游戏后从原步骤恢复。
- `Ctrl+Shift+F11` 是固定安全停止快捷键，会释放 Hook、关闭浮窗并恢复连招页面。
- 支持显式选择 wuwa-Hekili JSON，并读取 `team_config`、`team_aliases`、`initial_char_index`、`opener_script`、`loop_script` 做一次性转换。源文件不会被修改或监视；绝对／越界图标路径不会写入 Native 流程。
- MVP 支持键盘和鼠标；不支持手柄、可视化流程编辑器、图标捕获、Python 子进程或旧仓库双向同步。

真实游戏验收必须在同等完整性级别下使用可见的无边框／窗口化《鸣潮》执行，确认浮窗不抢焦点、物理输入继续到达游戏、错误动作不推进、Alt-Tab 暂停恢复及安全停止均正常。

OCR/增量扫描运行时：右上角“停止扫描”或 `Ctrl+Shift+F12` 为协作取消，会把中断前的最新识别结果加载到扫描结果列表；`Ctrl+Alt+F12` 为强制中止，不保证保留结果。所有扫描结果都需要人工勾选并应用后才会写入进度。

仓库中保留的 `resources/config.json` 和 `resources/user_progress_{uid}.json` 仅作为旧数据的一次性、只读导入来源，不再对应一个可运行的 Python 应用。

## Native OCR 构建

Debug 配置会在需要时自动调用 C++ OCR 构建脚本，并把最新 OCR DLL、运行库和模型复制到 Debug 输出目录：

```powershell
dotnet build WutheringWavesAchievement.sln -c Debug
```

如果需要手动构建或发布 Release OCR：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-native-ocr.ps1 -Configuration Release
```

如需跳过 Debug 的自动 OCR 构建，可传入 `-p:BuildNativeOcr=false`。Native OCR 使用 C++20、ONNX Runtime、OpenCV 和稳定 C ABI；WPF 侧通过 `Wuwa.Infrastructure` 调用 OCR、窗口捕获和游戏输入，并通过预览确认后写入 Native 工作区。

## 测试与验证

```powershell
dotnet test WutheringWavesAchievement.sln -c Release
dotnet build WutheringWavesAchievement.sln -c Release
```

真实 Windows 游戏窗口验证、UI 自动化和便携生命周期验证脚本位于 `scripts/`。由于应用清单请求管理员权限，UI、追踪和连招窗口 smoke 必须从提升后的 PowerShell 运行；`scripts/verify-rotation-runtime.ps1` 默认先执行静态只读输入边界检查，使用 `-RunWindowSmoke` 可启用可见测试窗口生命周期检查。
