## 为什么

用户需要手动打开浏览器开发者工具、找到 `getConfig` 请求、复制 `devcode` 和 `token` 字段——这个流程对非技术用户极不友好，也是新用户配置的主要障碍。通过在工具内嵌入浏览器并自动拦截凭据，可以将配置步骤简化为"点击按钮 → 登录 → 完成"。

## 变更内容

- 在设置页「通用认证设置」区域新增「自动获取」按钮
- 点击后弹出内嵌 `QWebEngineView` 浏览器窗口，打开 `https://www.kurobbs.com/`
- 用户在内嵌浏览器中正常登录后，工具自动拦截包含 `devcode` 和 `token` 请求头的 HTTP 请求
- 捕获到凭据后自动填入设置页输入框，弹窗关闭
- 原有手动输入框保留，作为备用输入方式

## 功能 (Capabilities)

### 新增功能
- `browser-auth-capture`: 内嵌 WebEngine 浏览器登录并自动从请求头拦截 devcode/token 凭据

### 修改功能
（无规范级行为变更）

## 影响

- **`core/settings_dialog.py`**：认证设置区域新增「自动获取」按钮及弹窗逻辑
- **依赖**：`PySide6-WebEngineWidgets`（已确认可用，无需额外安装）
- 不影响爬取逻辑、OCR 模块、用户进度存储
