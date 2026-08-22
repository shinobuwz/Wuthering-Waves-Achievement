# Native Wuthering Waves Achievement Workspace

`native/` 是与 Python/PySide6 版并行运行的 Windows 原生实现，使用 Visual Studio 2022、.NET 8、WPF、C++20 和 ONNX Runtime。

## 并存和数据边界

- Python 源码、入口、依赖和 legacy 数据不会被 native 版删除或改写。
- Legacy `resources/config.json` 与 `resources/user_progress_{uid}.json` 只作为显式、一次性的导入输入。
- 两个版本之间没有 watcher、后台同步或双向合并；重新导入属于需要确认的 native 状态替换。
- Native 可变状态位于 `%LocalAppData%\WutheringWavesAchievement`，以完整 generation 加原子 `current.json` 指针保存。
- 便携程序覆盖升级、删除或重新部署不会主动删除上述 LocalAppData 数据。
- 自动化或 smoke test 应设置 `WUWA_NATIVE_DATA_ROOT` 指向临时目录，避免接触正式用户状态。

## 当前功能

- 958 条 shipped 成就加载、搜索、筛选、排序和虚拟化表格。
- 四种进度状态、互斥成就组转换和同 revision 统计。
- 事务化 generations、故障恢复和只读 legacy profile 导入。
- 匿名 Kuro Wiki 拉取、业务状态/结构校验、稳定身份协调、歧义隔离和 tombstone。
- Progress JSON、完整 JSON、TSV 和原生 `.xlsx` 导入导出；导入先验证后激活。
- 深色/浅色主题和受限 GitHub Releases 更新检查。
- 单页 native OCR：窗口发现、GDI 捕获、C++ PP-OCRv5 det/cls/rec、预览确认和防降级事务合并。

尚未完成：native OCR 全局 Tab 导航/滚动扫描、捕获游戏图差异验证、tracker overlay 和最终 Python cutover。

## 构建与测试

```powershell
dotnet restore native/WutheringWavesAchievement.sln
dotnet test native/WutheringWavesAchievement.sln -c Release
dotnet build native/WutheringWavesAchievement.sln -c Release
```

Native OCR 需要 C++ DLL、ONNX Runtime/OpenCV 依赖和 PP-OCRv5 模型。正式使用请运行发布脚本；脚本会自动构建并将 OCR 组件、模型和模板放入同一个发布包，用户不需要再手动执行 OCR 构建脚本：

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/publish-native.ps1 -Configuration Release
```

然后启动：

```text
native/publish/win-x64/Wuwa.App.exe
```

源码开发环境如果已经运行过 `build-native-ocr.ps1`，后续 `Wuwa.App` 的 Release 输出也会自动复制已构建的 OCR 组件到 `ocr/` 子目录；普通 `dotnet build` 不会自动下载 C++ 依赖或编译 OCR，以避免构建过程产生隐式网络和 Visual Studio 依赖。

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/publish-native.ps1 -Configuration Release
```

发布脚本固定使用 `native/global.json` 的 .NET 8 SDK，构建并测试 C++ OCR，生成 self-contained `win-x64` 包，将 immutable resources、OCR DLL/模型和 `package-manifest.json` 放入 `native/publish/win-x64/`。输出目录被限制在 `native/publish/` 下，并使用 staged replacement 避免误删任意目录或先删除已知良好包。

## 独立验收命令

```powershell
# 真实匿名 Wiki；只使用临时 native data root
powershell -ExecutionPolicy Bypass -File native/scripts/verify-wiki-live.ps1

# WPF UI Automation 可达性和四张深/浅主题截图
powershell -ExecutionPolicy Bypass -File native/scripts/verify-ui.ps1

# 便携包启动、重启、alternate-copy 启动、删除程序目录和重新部署模拟
powershell -ExecutionPolicy Bypass -File native/scripts/verify-portable-lifecycle.ps1
```

`verify-portable-lifecycle.ps1 -LegacyConfig <copied-fixture-config.json>` 还会执行无交互的 legacy migration smoke，并验证 profile metadata、完成状态和 legacy 文件 hash。请仅传入复制出来的测试 fixture，不要把自动化脚本指向正式用户文件。

## Native OCR 单独构建

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/build-native-ocr.ps1 -Configuration Release
```
