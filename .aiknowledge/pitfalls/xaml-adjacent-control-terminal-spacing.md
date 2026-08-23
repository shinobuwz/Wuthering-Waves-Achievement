# XAML 控件组对齐必须核算末项间距和父容器内缩

## 不要这样做

不要只比较按钮本身的 `Width`、`MinWidth` 或截图中的最终尺寸，也不要用 `Margin="0"` 粗暴清除末项间距。必须逐项比较 `Margin` 的左、上、右、下四个分量、控件继承的样式，以及从控件到共同祖先之间每一层容器的 `Margin`、`Padding` 和 `BorderThickness`。

## 反例

反例一：`ActionButtonStyle` 的 Margin 是 `0,0,8,8`，最后一个按钮为了清除右侧 8 px 写成 `Margin="0"`。这同时删除了底部 8 px：水平 StackPanel 的高度由前一个按钮的 `32 + 8 = 40` 决定，末项在默认 Stretch 下会被安排为 40 px 高，而前一个按钮仍只有 32 px 高。

反例二：为相邻的 `TabItem` 或导航 `RadioButton` 统一设置 `Margin="0,0,8,0"`，最后一项没有清除右侧分量，导致控件组右边永久多出 8 px。这里共享样式没有底部间距，末项才可以安全使用 `Margin="0"`。

反例三：一组操作按钮位于带 `BorderThickness="1"` 的面板内，并由内容 `Grid Margin="16"` 再次内缩；另一组底部按钮直接放在窗口根 Grid 中且没有右边距。即使两组按钮自身的 `Margin` 完全相同，右边缘仍会相差 17 px。增大窗口或调整按钮宽度不会修复这个差异。

## 正例

修改布局前先找到两组控件的共同祖先，分别列出完整布局链：

```text
组 A：窗口 Margin 18 → TabControl Border 1 → 内容 Grid Margin 16 → 按钮组
组 B：窗口 Margin 18 → 根 Grid → 按钮组
```

视觉边缘由整条链共同决定。需要对齐时，应优先把两组控件放在相同布局层级；做不到时，再让较浅层的控件组显式补齐等价内缩。清除末端间距时只能修改对应方向，必须保留共享样式中其它方向的有效间距。项目为 Action Button 提供统一末项样式：

```xml
<Style x:Key="TerminalActionButtonStyle"
       TargetType="Button"
       BasedOn="{StaticResource ActionButtonStyle}">
    <Setter Property="Margin" Value="0,0,0,8" />
</Style>
```

`0,0,0,8` 只清除右侧项目间距，保留底部 8 px，因此相邻按钮的可见高度都保持 32 px。对于本来只有右侧间距、底部为 0 的页签样式，最后一项可以清除完整 Margin：

```xml
<Style TargetType="TabItem">
    <Setter Property="Margin" Value="0,0,8,0" />
</Style>

<TabItem Header="第一页" />
<TabItem Header="第二页" Margin="0" />
```

对跨容器按钮组，还要按父容器实际内缩对齐，而不是只复制相邻按钮的属性。当前 OCR 设置窗口中，页内操作组的右侧内缩是 `Border 1 + Grid Margin 16`；窗口底部公共操作组必须匹配这条布局链的 17 px 右侧内缩。

如果控件由数据动态生成，不能直接标记末项，则应使用能区分末项的容器样式/选择器，或改用由父容器、独立 Grid 列统一管理间距的布局；不能让每个项目无条件携带尾部间距。

## 为什么不行

WPF 的 `Margin`、`Padding` 和 `BorderThickness` 都参与 measure/arrange。水平 StackPanel 会按包含 Margin 的最大 DesiredSize 决定行高，默认 `VerticalAlignment="Stretch"` 又会让缺少底部 Margin 的末项填满整行，于是 `Margin="0"` 能直接造成 32/40 px 的相邻按钮高度差。截图中看到的控件边缘和高度是多层布局结果，不是按钮单个属性的直接映射。编译和单元测试只能证明 XAML 合法，无法发现这类像素级错位。

## 适用前提

当任务涉及 WPF/XAML 中相邻或需要跨区域对齐的 `Button`、`TabItem`、`RadioButton`、筛选器、工具栏、页签头、面板内操作区和窗口底部操作区时适用，尤其是控件分布在 `Border`、`Grid`、`StackPanel`、`WrapPanel`、`UniformGrid` 不同层级时。

## 验证

1. 回读目标控件和相邻控件的显式属性，以及它们命中的隐式/显式 Style。
2. 把 `Margin` 展开为左、上、右、下四个值逐项比较；不能把 `Margin="0"` 只理解为“清除右间距”。
3. 从每组控件向上追踪到共同祖先，记录每层 `Margin`、`Padding`、`BorderThickness`、父容器测量方向和对齐方式。
4. 区分“项目之间的间距”“行内垂直间距”和“控件组边界内缩”；末项只清除项目间距。
5. 同时检查相邻按钮的 `ActualHeight`/截图高度和左右边缘，特别警惕 32/40 px 差异。
6. 对需要对齐的两组控件，计算并比较累计左/右内缩，不要仅比较按钮本身。
7. 在默认宽度、最小宽度、两个页签选中状态和当前 DPI 下检查。
8. XAML 构建通过后仍需 Windows runtime 截图或实际窗口检查。

当前实例回读入口：`src/Wuwa.App/App.xaml` 的 `ActionButtonStyle` / `TerminalActionButtonStyle`，以及 `src/Wuwa.App/OcrPagingSettingsWindow.xaml`。重点检查 Action Button 末项保留底部 8 px、`TabItem` 末项只清除右间距、TabControl 的 1 px Border、页内 Grid 的 16 px Margin，以及窗口底部按钮组的等价右侧内缩。

## 重审条件

当控件改用新的 ItemsPanel、全局样式、主题模板、动态生成、不同 Border/Padding 或父容器原生 spacing 能力时，重新确认间距责任归属。若所有控件组已移到相同布局层级，应删除补偿性的固定内缩，避免叠加两套间距。
