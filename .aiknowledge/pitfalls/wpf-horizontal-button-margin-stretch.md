# WPF 横向按钮组不能继承带垂直外边距的通用样式

## 不要这样做

不要在横向 `StackPanel` 或 `DockPanel` 按钮组中直接混用带底部 `Margin` 的通用按钮样式和 `Margin="0"`。即使两个按钮具有相同的 `MinHeight`，最终可见高度也可能不同。

## 反例

项目的 `ActionButtonStyle` 继承了按钮的 `MinHeight="32"`，并设置了 `Margin="0,0,8,8"`。下面的第一个按钮保留 8 像素底部外边距，期望布局高度为 40；第二个按钮清除外边距后，在横向 `StackPanel` 的交叉轴上按默认 `VerticalAlignment="Stretch"` 拉伸到 40：

```xaml
<StackPanel Orientation="Horizontal">
    <Button Content="使用帮助" />
    <Button Content="返回主页面" Margin="0" />
</StackPanel>
```

结果可能是第一个按钮边框高 32，第二个按钮边框高 40。只统一 `MinWidth` 或 `MinHeight` 不能解决这个问题。

## 正例

为单行横向按钮组使用独立样式，移除垂直外边距，并在界面要求固定高度时显式统一 `Height`：

```xaml
<Style x:Key="InlineActionButtonStyle"
       TargetType="Button"
       BasedOn="{StaticResource ActionButtonStyle}">
    <Setter Property="Height" Value="32" />
    <Setter Property="MinHeight" Value="32" />
    <Setter Property="Margin" Value="0,0,8,0" />
    <Setter Property="VerticalAlignment" Value="Center" />
</Style>

<StackPanel Orientation="Horizontal">
    <Button Style="{StaticResource InlineActionButtonStyle}" Content="使用帮助" />
    <Button Style="{StaticResource InlineActionButtonStyle}" Content="返回主页面" Margin="0" />
</StackPanel>
```

同一按钮组的所有按钮都应使用该样式；最后一个按钮只清除右侧间距，不能重新引入不同的上下间距。需要自动适应内容时，也至少要统一所有按钮的垂直 `Margin` 和 `VerticalAlignment`。

## 为什么不行

横向 `StackPanel` 会用子元素中最大的期望高度决定自身高度。按钮的外边距参与期望尺寸计算，而控件边框本身不包含外边距。一个按钮的 32 像素高度加 8 像素底部外边距会把容器撑到 40；另一个没有外边距且保持默认拉伸的按钮便可能填满这 40 像素，造成肉眼可见的高度不一致。

## 适用前提

当任务涉及 WPF 横向操作按钮、工具栏、页头按钮、筛选按钮或底部批量操作按钮时适用，尤其是在复用为 `WrapPanel` 设计、包含底部间距的通用样式时。

## 验证

不要只比较 XAML 中的 `MinHeight`。应在实际运行窗口中检查按钮边框，并通过 Visual Studio Live Visual Tree、UI Automation 边界或截图确认 `ActualHeight` 一致。至少检查项目支持的最小窗口尺寸和常用 DPI／主题。

当前项目的参考实现位于 `src/Wuwa.App/OcrWorkbenchView.xaml` 的 `InlineActionButtonStyle`，应用于页头帮助／返回按钮、扫描结果筛选按钮和 Tag 批量操作按钮。

## 重审条件

当 `ActionButtonStyle` 不再包含底部外边距，或操作区统一改用不拉伸子元素的布局控件时，可以重新评估是否仍需固定 `Height`；删除专用样式前仍须进行实际布局验证。
