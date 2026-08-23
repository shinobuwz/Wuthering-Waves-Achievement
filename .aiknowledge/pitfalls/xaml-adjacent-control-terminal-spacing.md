# XAML 控件组对齐必须核算四向间距和父容器内缩

## 不要这样做

不要只比较按钮的 `Width`、`MinWidth`、`MinHeight` 或截图中的最终尺寸，也不要用 `Margin="0"` 粗暴清除末项间距。必须逐项检查 `Margin` 的左、上、右、下四个分量、命中的样式、父容器测量方向，以及从控件到共同祖先之间每一层的 `Margin`、`Padding` 和 `BorderThickness`。

## 反例

反例一：`ActionButtonStyle` 的 Margin 是 `0,0,8,8`，横向 `StackPanel` 中最后一个按钮为了清除右侧 8 px 写成 `Margin="0"`。这同时删除了底部 8 px：前一个按钮以 `32 + 8 = 40` 撑高整行，末项在默认 `VerticalAlignment="Stretch"` 下被拉伸到 40 px，于是相邻按钮可见高度变成 32/40 px。

反例二：为相邻 `TabItem` 或导航 `RadioButton` 统一设置 `Margin="0,0,8,0"`，最后一项没有清除右侧分量，导致控件组右边永久多出 8 px。

反例三：一组按钮位于 `BorderThickness="1"` 且内容 `Grid Margin="16"` 的面板内，另一组按钮直接位于窗口根 Grid。即使按钮自身属性完全相同，右边缘仍会相差 `1 + 16 = 17` px；增大窗口或调整按钮宽度不会修复。

## 正例

### 横向单行按钮使用专用样式

不要在横向单行按钮组中直接复用带底部 Margin 的 `ActionButtonStyle`。项目统一使用：

```xml
<Style x:Key="InlineActionButtonStyle"
       TargetType="Button"
       BasedOn="{StaticResource ActionButtonStyle}">
    <Setter Property="Height" Value="32" />
    <Setter Property="MinHeight" Value="32" />
    <Setter Property="Margin" Value="0,0,8,0" />
    <Setter Property="VerticalAlignment" Value="Center" />
</Style>

<Style x:Key="TerminalInlineActionButtonStyle"
       TargetType="Button"
       BasedOn="{StaticResource InlineActionButtonStyle}">
    <Setter Property="Margin" Value="0" />
</Style>
```

同一横向按钮组的所有按钮必须使用 `InlineActionButtonStyle`；末项使用 `TerminalInlineActionButtonStyle`。由于专用样式已统一移除上下间距并固定高度，末项 `Margin="0"` 不会再产生 32/40 px 高度差。

对于 WrapPanel 或需要换行的操作区，继续使用 `ActionButtonStyle` 的右/下间距；不能把横向单行和可换行布局的间距职责混在一个样式中。

### 末项只清除项目间距

如果共享样式本来只有右侧间距、底部为 0，例如页签：

```xml
<Style TargetType="TabItem">
    <Setter Property="Margin" Value="0,0,8,0" />
</Style>

<TabItem Header="第一页" />
<TabItem Header="第二页" Margin="0" />
```

此时末项清除完整 Margin 是安全的。使用前必须先展开并确认共享 Margin 的四个分量，不能照抄其它控件组的末项写法。

### 跨容器对齐要比较完整布局链

修改布局前先找到控件组的共同祖先并列出完整路径：

```text
组 A：窗口 Margin 18 → TabControl Border 1 → 内容 Grid Margin 16 → 按钮组
组 B：窗口 Margin 18 → 根 Grid → 按钮组
```

视觉边缘由整条链共同决定。优先把需要对齐的控件组放到相同布局层级；做不到时，让较浅层控件组补齐等价内缩。当前 OCR 设置窗口的页内按钮右侧内缩是 17 px，窗口底部公共按钮组必须匹配该内缩。

动态生成控件无法直接标记末项时，应使用样式选择器、可识别末项的容器逻辑，或改由父 Grid/容器统一管理间距，不能让每项无条件携带尾部间距。

## 为什么不行

WPF 的 `Margin`、`Padding` 和 `BorderThickness` 都参与 measure/arrange。横向 StackPanel 会按包含 Margin 的最大 DesiredSize 决定行高，默认 Stretch 又会让垂直 Margin 不一致的控件填满不同可用高度。截图中的边缘和高度是多层布局结果，不是按钮单个属性的直接映射。XAML 编译和单元测试只能证明模板合法，无法发现 8 px、17 px 或 32/40 px 的视觉错位。

## 适用前提

当任务涉及 WPF/XAML 横向操作按钮、工具栏、页头按钮、筛选按钮、底部批量操作区、`TabItem`、导航 `RadioButton`，或控件跨 `Border`、`Grid`、`StackPanel`、`WrapPanel`、`UniformGrid` 层级对齐时适用。

## 验证

1. 回读目标控件与相邻控件的显式属性，以及命中的隐式/显式 Style。
2. 把 `Margin` 展开为左、上、右、下四个值；不能把 `Margin="0"` 只理解为“清除右间距”。
3. 确认容器方向：横向单行组统一使用 `InlineActionButtonStyle` / `TerminalInlineActionButtonStyle`，可换行组使用 `ActionButtonStyle`。
4. 从每组控件向上追踪到共同祖先，记录每层 `Margin`、`Padding`、`BorderThickness`、测量方向和对齐方式。
5. 区分项目间距、行内垂直间距和控件组边界内缩；末项只清除项目间距。
6. 使用 Live Visual Tree、UI Automation 边界或截图检查相邻按钮 `ActualHeight`，重点排查 32/40 px 差异。
7. 在默认宽度、最小宽度、各页签选中状态、明暗主题和常用 DPI 下检查左右边缘与高度。
8. XAML 构建通过后仍需 Windows runtime 实际验证。

当前参考实现：

- `src/Wuwa.App/App.xaml`：`ActionButtonStyle`、`InlineActionButtonStyle`、`TerminalInlineActionButtonStyle`。
- `src/Wuwa.App/OcrWorkbenchView.xaml`：页头、筛选和 Tag 横向按钮组。
- `src/Wuwa.App/OcrPagingSettingsWindow.xaml`：横向校准/保存按钮、页签末项和跨容器右边缘对齐。

## 重审条件

当 `ActionButtonStyle` 不再包含底部间距、控件改用新的 ItemsPanel/主题模板、动态生成、不同 Border/Padding，或父容器提供原生 spacing 时，重新确认间距责任。删除专用样式或补偿内缩前必须重新做 runtime 边界和高度验证。
