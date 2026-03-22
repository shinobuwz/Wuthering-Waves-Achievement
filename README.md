# 鸣潮成就管理器

基于 PySide6 开发的鸣潮游戏成就管理工具，支持成就数据获取、进度管理与 OCR 自动扫描。

## 功能特性

### 🎯 成就管理
- **成就数据爬取**：从在线 wiki 获取最新成就数据
- **进度追踪**：记录个人成就完成状态，支持已完成 / 未完成 / 暂不可获取三档
- **分类浏览**：按一级 / 二级分类、版本、完成状态过滤
- **快速搜索**：关键词实时过滤成就列表
- **数据导入导出**：JSON 格式备份与恢复，建议每版本更新前先导出备份

### 📷 OCR 自动扫描
- **全量自动扫描**：自动遍历所有一级 / 二级 Tab，滚动扫描当前分类下的全部成就
- **成就识别**：模板匹配定位图标，OCR 识别名称与完成状态
- **模糊匹配**：Levenshtein 编辑距离 + 中点字符归一化，容忍 OCR 识别偏差
- **防降级保护**：已完成状态不会被 OCR 结果覆盖为未完成
- **结果预览**：扫描结果实时展示，OCR 原文与匹配名称不一致时高亮提示

### 🎨 界面
- **明暗主题**：一键切换
- **自定义头像**：支持游戏内角色头像与立绘
- **多用户**：为不同账号保存独立进度

## 安装说明

### 环境要求
- Python 3.11+
- Windows（OCR 扫描功能依赖 Win32 API）

### 安装依赖
```bash
pip install -r requirements.txt
```

### 主要依赖
| 依赖 | 用途 |
|------|------|
| PySide6 | GUI 框架 |
| opencv-python | 图像处理 / 模板匹配 |
| pyautogui | 鼠标模拟 / 滚轮控制 |
| mss | 屏幕截图 |
| onnxruntime | OCR 推理引擎 |
| requests / beautifulsoup4 | 数据爬取 |

## 使用说明

### 启动
```bash
python main.py
```

### OCR 扫描
1. 打开游戏并进入成就页面
2. 切换到「OCR 扫描」标签页
3. 点击「检测游戏窗口」
4. 点击「开始扫描」，工具将自动遍历所有分类
5. 扫描完成后点击「保存到用户进度」

> **注意**：OCR 扫描目前固定适配 1920×1080 分辨率前台窗口。

### 数据格式

`resources/user_progress_{uid}.json`：
```json
{
  "10100001": { "获取状态": "已完成" },
  "10100002": { "获取状态": "未完成" }
}
```

## 项目结构

```
├── main.py                    # 入口
├── version.py                 # 版本号
├── requirements.txt
├── core/
│   ├── main_window.py         # 主窗口
│   ├── manage_tab.py          # 成就管理
│   ├── crawl_tab.py           # 数据爬取
│   ├── ocr_tab.py             # OCR 扫描
│   ├── statistics_tab.py      # 统计图表
│   ├── achievement_ocr.py     # OCR 核心（识别 / 匹配 / Tab 切换 / 全量扫描）
│   ├── game_capture.py        # 窗口检测与截图
│   ├── config.py              # 配置与数据管理
│   └── ...                    # UI 组件
├── onnxocr/                   # OCR 引擎（见致谢）
└── resources/
    ├── base_achievements.json # 成就数据库
    ├── user_progress_*.json   # 用户进度
    └── ocr_templates/         # 模板匹配图标
```

## 致谢

- **[onnxocr](https://github.com/jingsongliujing/onnxocr)**：基于 PaddleOCR 转 ONNX 的轻量本地 OCR 引擎，用于成就名称识别
- **原项目 [zsh19961226](https://github.com/zsh19961226)**：本项目基于其早期版本完全重构

## 许可证

本项目仅供学习与个人使用。
