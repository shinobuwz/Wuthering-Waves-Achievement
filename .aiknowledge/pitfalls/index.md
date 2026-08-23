# Pitfall Index

| State | Read when | Target |
|---|---|---|
| active | Legacy migration、用户进度、并行版本、side-by-side 发布 | [并行版本不能做隐藏双向同步](legacy-native-no-live-sync.md) |
| active | Native save、manifest、crash recovery、generation、rollback | [Native 状态必须以完整 generation 原子激活](native-generation-atomic-activation.md) |
| active | Wiki 拉取、远端更新、身份匹配、tombstone、部分响应 | [Wiki 同步必须验证并隔离歧义](wiki-sync-validation-and-quarantine.md) |
| active | OCR preview、扫描取消、状态合并、保存进度、降级保护 | [OCR 只能从已确认 preview 合并进度](ocr-preview-before-merge.md) |
| active | 游戏窗口、Tab 导航、滚轮、截图、全量扫描、输入权限 | [OCR 导航和输入必须先验证环境与页面反馈](ocr-navigation-input-validation.md) |
| active | 互斥成就、累计进度链、已占用、统计、手动或 OCR 状态变更 | [成就组状态必须经过统一 transition](achievement-group-status-transition.md) |
| active | WPF、XAML、相邻按钮、页签、Margin、Padding、Border、跨区域对齐 | [XAML 控件组对齐必须核算末项间距和父容器内缩](xaml-adjacent-control-terminal-spacing.md) |
