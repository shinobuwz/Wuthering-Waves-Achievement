# XAML 控件组对齐必须核算末项间距和父容器内缩

## 不要这样做

不要只比较按钮本身的 `Width`、`MinWidth` 或截图中的最终尺寸，也不要只修正最后一个控件的 `Margin` 就认为整组已经对齐。必须同时检查控件继承的样式，以及从控件到共同祖先之间每一层容器的 `Margin`、`Padding` 和 `BorderThickness`。

## 反例

反例一：为相邻的 `Button`、`TabItem` 或导航 `RadioButton` 统一设置 `Margin="0,0,8,0"`，最后一项没有覆盖 `Margin="0"`，导致控件组右边永久多出 8 px。

反例二：一组操作按钮位于带 `BorderThickness="1"` 的面板内，并由内容 `Grid Margin="16"` 再次内缩；另一组底部按钮直接放在窗口根 Grid 中且没有右边距。即使两组按钮自身的 `Margin` 完全相同，右边缘仍会相差 17 px。增大窗口或调整按钮宽度不会修复这个差异。

## 正例

修改布局前先找到两组控件的共同祖先，分别列出完整布局链：

```text
组 A：窗口 Margin 18 → TabControl Border 1 → 内容 Grid Margin 16 → 按钮组
组 B：窗口 Margin 18 → 根 Grid → 按钮组
```

视觉边缘由整条链共同决定。需要对齐时，应优先把两组控件放在相同布局层级；做不到时，再让较浅层的控件组显式补齐等价内缩。相邻项目之间如果由项目自身右边距提供间隔，则最后一项必须清除末端间距：

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

WPF 的 `Margin`、`Padding` 和 `BorderThickness` 都参与 measure/arrange。截图中看到的控件边缘是多层布局结果，不是按钮单个属性的直接映射。编译和单元测试只能证明 XAML 合法，无法发现 8 px 或 17 px 的视觉错位；固定加宽窗口通常只会让问题变得不明显。

## 适用前提

当任务涉及 WPF/XAML 中相邻或需要跨区域对齐的 `Button`、`TabItem`、`RadioButton`、筛选器、工具栏、页签头、面板内操作区和窗口底部操作区时适用，尤其是控件分布在 `Border`、`Grid`、`StackPanel`、`WrapPanel`、`UniformGrid` 不同层级时。

## 验证

1. 回读目标控件和相邻控件的显式属性，以及它们命中的隐式/显式 Style。
2. 从每组控件向上追踪到共同祖先，记录每层 `Margin`、`Padding`、`BorderThickness` 和对齐方式。
3. 区分“项目之间的间距”和“控件组边界的内缩”；最后一项不能携带项目间距。
4. 对需要对齐的两组控件，计算并比较累计左/右内缩，不要仅比较按钮本身。
5. 在默认宽度、最小宽度、两个页签选中状态和当前 DPI 下检查左右边缘。
6. XAML 构建通过后仍需 Windows runtime 截图或实际窗口检查。

当前实例回读入口：`src/Wuwa.App/OcrPagingSettingsWindow.xaml`。重点检查 `TabItem` 共享 Margin、末项 `Margin="0"`、TabControl 的 1 px Border、页内 Grid 的 16 px Margin，以及窗口底部按钮组的等价右侧内缩。

## 重审条件

当控件改用新的 ItemsPanel、全局样式、主题模板、动态生成、不同 Border/Padding 或父容器原生 spacing 能力时，重新确认间距责任归属。若所有控件组已移到相同布局层级，应删除补偿性的固定内缩，避免叠加两套间距。
