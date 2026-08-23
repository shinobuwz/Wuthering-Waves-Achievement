# 鸣潮成就管理器

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

发布目录为 `publish/win-x64/`。

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

- 958 条成就数据加载、搜索、筛选、排序和虚拟化表格
- 成就状态管理、成就组状态转换和统计
- generation 事务化本地存储、故障恢复和原子激活
- JSON、TSV、XLSX 导入导出
- Kuro Wiki 数据同步与稳定身份匹配
- Native PP-OCRv5 检测、分类、识别和 OCR 结果预览
- 当前分类 OCR 扫描及全量分类 OCR 扫描入口
- 增量扫描未完成成就；高置信识别为已完成后立即通过工作区写入本地进度
- “校准与输入设置”内提供安全的搜索框输入测试，并可调整聚焦、修饰键、按键、全选和粘贴等待时间；测试不执行搜索或修改进度
- 深色/浅色主题和便携发布

## 数据位置

- 随程序发布的只读资源：`resources/base_achievements.json`、`resources/category_config.json`、`resources/ocr_templates/`
- Native 可变工作区：默认位于 `<程序目录>\data`；设置 `WUWA_NATIVE_DATA_ROOT` 时使用指定目录
- Native OCR 诊断日志：`<程序目录>\log\native-ocr-YYYY-MM-DD.log`

游戏窗口若以管理员权限运行，Native 应用也会请求管理员权限，以保证 Windows 鼠标/键盘输入不会被完整性级别拦截。

OCR/增量扫描运行时：右上角“停止扫描”或 `Ctrl+Shift+F12` 为协作取消；`Ctrl+Alt+F12` 为强制中止。增量扫描已高置信识别并成功入库的完成状态会保留，当前尚未识别完成的项目不会写入。

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

真实 Windows 游戏窗口验证、UI 自动化和便携生命周期验证脚本位于 `scripts/`。
