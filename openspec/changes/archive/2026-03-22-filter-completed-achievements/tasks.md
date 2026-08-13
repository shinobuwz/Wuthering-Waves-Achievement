## 1. 数据层

- [x] 1.1 在 `AchievementManager.filter_data()` 签名中新增 `hide_completed=False` 参数
- [x] 1.2 在 `filter_data()` 的循环内增加过滤逻辑：当 `hide_completed=True` 且 `获取状态 == "已完成"` 时跳过该条目

## 2. UI 层

- [x] 2.1 在 `ManageTab.init_ui()` 中，在 `filter_layout2` 末尾新增 `QCheckBox("隐藏已完成")` 控件，连接 `stateChanged` 到 `filter_data`
- [x] 2.2 在 `ManageTab.filter_data()` 中读取复选框状态，传入 `hide_completed` 参数调用 `self.manager.filter_data()`
- [x] 2.3 对新增复选框应用主题样式（`apply_theme` 中补充 CheckBox 颜色）
