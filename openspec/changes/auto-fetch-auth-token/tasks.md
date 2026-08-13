## 1. 拦截器核心

- [x] 1.1 在 `core/settings_dialog.py` 中新建 `AuthInterceptor(QWebEngineUrlRequestInterceptor)` 类，在 `interceptRequest` 中检测 `devcode` + `token` 双字段，通过 Signal 发出捕获结果
- [x] 1.2 新建 `BrowserAuthDialog(QDialog)` 类，内嵌 `QWebEngineView`，使用命名 profile `kurobbs_auth`，绑定拦截器，接收凭据后调用 `accept()`

## 2. UI 集成

- [x] 2.1 在 `settings_dialog.py` 认证设置区的 DevCode 输入行旁边（或输入框下方）新增「自动获取」`QPushButton`
- [x] 2.2 按钮点击时实例化并 `exec()` `BrowserAuthDialog`，对话框 `accept` 后将捕获的 devcode/token 填入 `self.devcode_edit` 和 `self.token_edit`
- [x] 2.3 在对话框标题栏或说明标签中提示用户"请在浏览器中登录库街区，登录成功后将自动获取凭据"

## 3. 导入与兼容

- [x] 3.1 在文件顶部添加 `from PySide6.QtWebEngineWidgets import QWebEngineView` 和 `from PySide6.QtWebEngineCore import QWebEngineProfile, QWebEngineUrlRequestInterceptor` 导入
