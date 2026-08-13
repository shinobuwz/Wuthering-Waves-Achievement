## 上下文

设置页认证区目前需要用户手动粘贴 `devcode` 和 `token`。这两个值存在于用户登录库街区后的 HTTP 请求头中，可通过拦截 `getConfig` 等请求自动获取。`PySide6-WebEngineWidgets` 已在环境中可用，支持 `QWebEngineView` + `QWebEngineProfile` 网络请求拦截。

## 目标 / 非目标

**目标：**
- 在设置页认证区新增「自动获取」按钮
- 弹出内嵌浏览器窗口打开 `https://www.kurobbs.com/`
- 通过 `QWebEngineUrlRequestInterceptor` 拦截所有请求头，识别含 `devcode` 和 `token` 的请求
- 捕获成功后自动填入输入框，关闭弹窗并提示成功

**非目标：**
- 不保存或上传用户的账号密码
- 不处理二维码登录以外的其他登录方式的特殊逻辑（统一依赖浏览器内自然登录）
- 不持久化浏览器 session（每次点击「自动获取」均使用独立 profile）

## 决策

**Decision 1：使用 `QWebEngineUrlRequestInterceptor` 拦截请求头**
- 理由：PySide6 WebEngine 原生支持，无需代理、无需证书，只需继承 `QWebEngineUrlRequestInterceptor` 重写 `interceptRequest`，在其中检查请求头。
- 备选方案：`QWebEnginePage.acceptNavigationRequest` 只能拦截导航，不适用；JS 注入无法读取请求头。

**Decision 2：拦截目标为任意含 `devcode` + `token` 双字段的请求**
- 理由：不局限于 `getConfig`，任意 API 请求携带这两个头即可，更健壮。
- 判断条件：`request.httpHeader(b"devcode")` 和 `request.httpHeader(b"token")` 均非空且均非 `""`。

**Decision 3：独立 `QWebEngineProfile`（非默认 profile）**
- 理由：避免污染工具主进程的 cookie/session；使用 `QWebEngineProfile("kurobbs_auth")` 创建隔离 profile，复用同一 profile 可保持登录态（用户无需每次重新登录）。

**Decision 4：弹窗为独立 `QDialog`，捕获后自动 `accept()`**
- 理由：阻塞式对话框，捕获完成后父窗口回调填写输入框，逻辑简单。

## 风险 / 权衡

- [风险] `QWebEngineUrlRequestInterceptor.interceptRequest` 在网络线程调用，不能直接操作 UI → 用 `Signal` 跨线程通知主线程填写输入框
- [风险] 库街区更新后可能改变请求头字段名 → 字段名作为常量，便于修改
- [权衡] 独立 profile 会在磁盘写入缓存（`%AppData%/QtWebEngine/kurobbs_auth`）→ 可接受，方便用户保持登录态无需每次重新扫码
