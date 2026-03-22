"""
统计信息标签页
"""
import logging
import math
from PySide6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
                               QComboBox, QGroupBox, QGridLayout)
from PySide6.QtCore import Qt, QRect
from PySide6.QtGui import QPainter, QColor, QFontMetrics

from core.config import config
from core.signal_bus import signal_bus
from core.styles import get_button_style

logger = logging.getLogger(__name__)


class SimpleChartWidget(QWidget):
    """简单的图表组件"""

    def __init__(self, chart_type="pie", parent=None):
        super().__init__(parent)
        self.chart_type = chart_type
        self.data = {}
        self.theme = config.theme
        self.setMinimumSize(400, 300)

        # hover效果相关
        self.hover_index = -1  # 当前hover的条形索引
        self.setMouseTracking(True)  # 启用鼠标跟踪

        self.colors = [

            QColor(46, 204, 113),  # 绿色
            QColor(52, 152, 219),  # 蓝色
            QColor(231, 76, 60),  # 红色
            QColor(241, 196, 15),  # 黄色
            QColor(155, 89, 182),  # 紫色
            QColor(230, 126, 34),  # 橙色
            QColor(149, 165, 166),  # 灰色
        ]
        self.bg_color = QColor(0, 0, 0, 0)  # 透明背景
        # 设置主题颜色
        if self.theme == "light":
            self.text_color = QColor(50, 50, 50)
        else:
            self.text_color = QColor(255, 255, 200)

    def set_data(self, data):
        """设置图表数据"""
        self.data = data
        self.update()

    def mouseMoveEvent(self, event):
        """鼠标移动事件"""
        if self.chart_type == "horizontal_bar" and self.data:
            # 计算鼠标位置对应的条形索引
            rect = self.rect()
            padding = 20
            label_width = 60
            chart_rect = rect.adjusted(label_width + padding, padding, -padding, -padding)

            # 获取排序后的数据（与绘制时保持一致）
            sorted_items = sorted(self.data.items(), key=lambda x: x[1], reverse=True)

            # 限制显示最多10个版本
            max_display = 10
            if len(sorted_items) > max_display:
                sorted_items = sorted_items[:max_display]
                # 添加"其他"项
                other_count = sum(self.data.values()) - sum(item[1] for item in sorted_items)
                if other_count > 0:
                    sorted_items.append(("其他", other_count))

            bar_height = min(30, (chart_rect.height() - (len(sorted_items) - 1) * 5) / len(sorted_items))

            for i, (label, value) in enumerate(sorted_items):
                y = chart_rect.top() + i * (bar_height + 5)
                bar_bottom = y + bar_height
                # 检查鼠标是否在当前条形内（考虑条形间的5像素间距）
                if y <= event.pos().y() <= bar_bottom and event.pos().y() < y + bar_height + 5:
                    if self.hover_index != i:
                        self.hover_index = i
                        self.update()
                    # 显示提示框
                    global_pos = self.mapToGlobal(event.pos())
                    # 通过父级访问提示框
                    parent = self.parent()
                    while parent and not hasattr(parent, 'tooltip'):
                        parent = parent.parent()
                    if parent and hasattr(parent, 'tooltip'):
                        parent.tooltip.show_tooltip(global_pos, f"{label}", f"成就数量: {value}")
                    return
            else:
                if self.hover_index != -1:
                    self.hover_index = -1
                    self.update()
                # 通过父级访问提示框
                parent = self.parent()
                while parent and not hasattr(parent, 'tooltip'):
                    parent = parent.parent()
                if parent and hasattr(parent, 'tooltip'):
                    parent.tooltip.hide()
        elif self.chart_type == "bar" and self.data:
            # 计算鼠标位置对应的柱形索引
            rect = self.rect()
            label_height = 70
            padding = 40
            chart_rect = rect.adjusted(padding, padding, -padding, -(padding + label_height))

            # 动态计算柱宽（与draw_bar方法保持一致）
            labels = list(self.data.keys())
            min_bar_width = 40
            max_bar_width = 100
            spacing = 15

            if len(labels) <= 5:
                target_width = (chart_rect.width() - (len(labels) - 1) * spacing) / len(labels)
                bar_width = min(target_width, max_bar_width)
            elif len(labels) <= 10:
                bar_width = (chart_rect.width() - (len(labels) - 1) * spacing) / len(labels)
                bar_width = max(min(bar_width, 80), min_bar_width)
            else:
                bar_width = min_bar_width

            # 计算所有柱子的总宽度和起始位置
            total_bars_width = len(labels) * bar_width + (len(labels) - 1) * spacing
            start_x = chart_rect.left() + (chart_rect.width() - total_bars_width) // 2

            for i, (label, data) in enumerate(self.data.items()):
                x = start_x + i * (bar_width + spacing)
                # 检查鼠标是否在当前柱形内
                if x <= event.pos().x() <= x + bar_width:
                    if self.hover_index != i:
                        self.hover_index = i
                        self.update()
                    # 显示提示框
                    completed = data['completed']
                    total = data['total']
                    percentage = int(completed * 100 / total) if total > 0 else 0
                    global_pos = self.mapToGlobal(event.pos())
                    # 通过父级访问提示框
                    parent = self.parent()
                    while parent and not hasattr(parent, 'tooltip'):
                        parent = parent.parent()
                    if parent and hasattr(parent, 'tooltip'):
                        parent.tooltip.show_tooltip(global_pos, f"{label}", f"完成度: {completed}/{total} ({percentage}%)")
                    return
            else:
                if self.hover_index != -1:
                    self.hover_index = -1
                    self.update()
                # 通过父级访问提示框
                parent = self.parent()
                while parent and not hasattr(parent, 'tooltip'):
                    parent = parent.parent()
                if parent and hasattr(parent, 'tooltip'):
                    parent.tooltip.hide()
        elif self.chart_type == "pie" and self.data:
            # 计算鼠标位置对应的扇形索引
            rect = self.rect()
            pie_width = rect.width() * 0.6
            padding = 20

            # 计算饼图位置（与draw_pie方法保持一致）
            max_pie_size = min(pie_width - padding * 2, rect.height() - padding * 2)
            pie_size = int(max_pie_size * 0.8)
            pie_x = rect.left() + padding + (max_pie_size - pie_size) // 2
            pie_y = rect.top() + (rect.height() - pie_size) // 2
            chart_rect = QRect(pie_x, pie_y, pie_size, pie_size)

            # 计算鼠标相对于饼图中心的位置
            center_x = chart_rect.center().x()
            center_y = chart_rect.center().y()
            dx = event.pos().x() - center_x
            dy = event.pos().y() - center_y

            # 检查鼠标是否在饼图圆形内
            distance = math.sqrt(dx*dx + dy*dy)
            if distance <= pie_size / 2:
                # 计算鼠标角度
                angle = math.degrees(math.atan2(-dy, dx))  # 负dy因为y轴向下
                if angle < 0:
                    angle += 360

                # 计算每个扇形的角度范围
                total = sum(self.data.values())
                start_angle = 0
                for i, (label, value) in enumerate(self.data.items()):
                    span_angle = int(360 * value / total) if total > 0 else 0
                    if span_angle == 0:
                        span_angle = 1

                    end_angle = start_angle + span_angle

                    # 检查鼠标是否在当前扇形内
                    if start_angle <= angle < end_angle:
                        if self.hover_index != i:
                            self.hover_index = i
                            self.update()
                        # 显示提示框
                        percentage = int(value * 100 / total) if total > 0 else 0
                        global_pos = self.mapToGlobal(event.pos())
                        # 通过父级访问提示框
                        parent = self.parent()
                        while parent and not hasattr(parent, 'tooltip'):
                            parent = parent.parent()
                        if parent and hasattr(parent, 'tooltip'):
                            parent.tooltip.show_tooltip(global_pos, f"{label}", f"占比: {percentage}%")
                        return
                    start_angle = end_angle
                else:
                    if self.hover_index != -1:
                        self.hover_index = -1
                        self.update()
                    # 通过父级访问提示框
                    parent = self.parent()
                    while parent and not hasattr(parent, 'tooltip'):
                        parent = parent.parent()
                    if parent and hasattr(parent, 'tooltip'):
                        parent.tooltip.hide()
            else:
                if self.hover_index != -1:
                    self.hover_index = -1
                    self.update()
                # 通过父级访问提示框
                parent = self.parent()
                while parent and not hasattr(parent, 'tooltip'):
                    parent = parent.parent()
                if parent and hasattr(parent, 'tooltip'):
                    parent.tooltip.hide()
        else:
            if self.hover_index != -1:
                self.hover_index = -1
                self.update()
            # 通过父级访问提示框
            parent = self.parent()
            while parent and not hasattr(parent, 'tooltip'):
                parent = parent.parent()
            if parent and hasattr(parent, 'tooltip'):
                parent.tooltip.hide()

    def leaveEvent(self, event):
        """鼠标离开事件"""
        if self.hover_index != -1:
            self.hover_index = -1
            self.update()

    def paintEvent(self, event):
        """绘制事件"""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # 不绘制背景，保持透明

        # 根据图表类型绘制
        if self.chart_type == "pie":
            self.draw_pie(painter, self.rect())
        elif self.chart_type == "bar":
            self.draw_bar(painter, self.rect())
        elif self.chart_type == "horizontal_bar":
            self.draw_horizontal_bar(painter, self.rect())

        # 调试：如果没有数据，显示文字提示
        if not self.data:
            painter.setPen(self.text_color)
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "无数据")

    def draw_pie(self, painter, rect):
        """绘制饼图"""
        if not self.data:
            return

        # 计算绘图区域 - 左侧饼图，右侧图例
        pie_width = rect.width() * 0.6  # 饼图占60%宽度
        legend_width = rect.width() * 0.35  # 图例占35%宽度
        padding = 20

        # 饼图区域 - 调整饼图大小为80%
        max_pie_size = min(pie_width - padding * 2, rect.height() - padding * 2)
        pie_size = int(max_pie_size * 0.8)  # 饼图占可用空间的80%
        pie_x = rect.left() + padding + (max_pie_size - pie_size) // 2
        pie_y = rect.top() + (rect.height() - pie_size) // 2
        chart_rect = QRect(pie_x, pie_y, pie_size, pie_size)

        # 图例区域
        legend_x = pie_x + pie_size + padding
        legend_y = rect.top() + padding
        legend_width = rect.right() - legend_x - padding
        legend_height = rect.height() - padding * 2

        # 计算总和
        total = sum(self.data.values())
        if total == 0:
            return

        # 绘制饼图
        start_angle = 0
        for i, (label, value) in enumerate(self.data.items()):
            # 计算角度
            span_angle = int(360 * value / total) if total > 0 else 0

            # 如果值为0，绘制一个极小的扇形以显示该分类
            if span_angle == 0:
                span_angle = 1  # 最小角度

            # 判断是否是hover状态
            is_hover = (self.hover_index == i)

            # 如果hover，扇形向外偏移
            if is_hover:
                # 计算扇形中心角度
                center_angle = start_angle + span_angle / 2
                # 计算偏移量（向外移动10像素）
                offset_x = int(10 * math.cos(math.radians(center_angle)))
                offset_y = int(-10 * math.sin(math.radians(center_angle)))  # 负值因为y轴向下
                hover_rect = chart_rect.adjusted(offset_x, offset_y, offset_x, offset_y)

                # 绘制hover阴影
                shadow_rect = hover_rect.adjusted(3, 3, 3, 3)
                painter.setBrush(QColor(0, 0, 0, 60))
                painter.setPen(Qt.PenStyle.NoPen)
                painter.drawPie(shadow_rect, start_angle * 16, span_angle * 16)

                # 绘制hover扇形（更亮的颜色）
                hover_color = QColor(
                    min(255, self.colors[i % len(self.colors)].red() + 30),
                    min(255, self.colors[i % len(self.colors)].green() + 30),
                    min(255, self.colors[i % len(self.colors)].blue() + 30)
                )
                painter.setBrush(hover_color)
                painter.setPen(QColor(255, 255, 255, 150))  # 更明显的边框
                painter.drawPie(hover_rect, start_angle * 16, span_angle * 16)
            else:
                # 绘制普通阴影
                shadow_rect = chart_rect.adjusted(2, 2, 2, 2)
                painter.setBrush(QColor(0, 0, 0, 50))
                painter.setPen(Qt.PenStyle.NoPen)
                painter.drawPie(shadow_rect, start_angle * 16, span_angle * 16)

                # 绘制普通扇形
                painter.setBrush(self.colors[i % len(self.colors)])
                painter.setPen(QColor(255, 255, 255, 100))  # 半透明白色边框
                painter.drawPie(chart_rect, start_angle * 16, span_angle * 16)

            start_angle += span_angle

        # 绘制图例
        legend_item_height = 25
        legend_y_start = legend_y + (legend_height - len(self.data) * legend_item_height) // 2

        # 定义顺序：未完成、已完成、暂不可获取
        order = ["未完成", "已完成", "暂不可获取"]
        sorted_data = []
        for key in order:
            if key in self.data:
                sorted_data.append((key, self.data[key]))
        # 添加其他不在顺序中的项
        for label, value in self.data.items():
            if label not in order:
                sorted_data.append((label, value))

        # 设置固定的标签宽度（让所有标签占用相同宽度）
        font = painter.font()
        font.setPointSize(8)  # 使用更小的字体
        painter.setFont(font)
        font_metrics = painter.fontMetrics()

        # 计算合适的固定宽度（基于最长标签）
        max_label_width = 0
        for label, value in sorted_data:
            label_width = font_metrics.horizontalAdvance(label)
            max_label_width = max(max_label_width, label_width)

        # 使用固定宽度，让所有标签占用相同空间
        fixed_label_width = max(60, max_label_width + 5)  # 最小60像素宽度
        colon_x = legend_x + 25 + fixed_label_width

        for i, (label, value) in enumerate(sorted_data):
            # 图例项位置
            item_y = legend_y_start + i * legend_item_height

            # 绘制颜色方块
            color_rect = QRect(legend_x, item_y + 5, 15, 15)
            # 找到原始索引以使用正确的颜色
            original_index = list(self.data.keys()).index(label)
            painter.fillRect(color_rect, self.colors[original_index % len(self.colors)])

            # 绘制标签文本（所有标签占用相同宽度）
            painter.setPen(self.text_color)

            # 绘制标签（使用固定宽度，让文字居中）
            label_rect = QRect(legend_x + 25, item_y, fixed_label_width, legend_item_height)
            painter.drawText(label_rect, Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignVCenter, label)

            # 绘制冒号
            colon_rect = QRect(colon_x, item_y, 8, legend_item_height)
            painter.drawText(colon_rect, Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter, ":")

            # 绘制数值和百分比（减少冒号到数值的距离）
            percentage = int(value * 100 / total) if total > 0 else 0
            value_text = f"{value} ({percentage}%)"
            # 计算可用宽度，冒号后直接跟数值
            available_width = legend_width - (colon_x - legend_x) - 5
            value_rect = QRect(colon_x + 8, item_y, available_width, legend_item_height)
            painter.drawText(value_rect, Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter, value_text)

        # 恢复原始字体大小
        font.setPointSize(12)
        painter.setFont(font)

    def draw_bar(self, painter, rect):
        """绘制堆叠柱状图"""
        if not self.data:
            return

        # 计算绘图区域，给底部标签留适当空间
        label_height = 70  # 标签区域高度
        padding = 40
        chart_rect = rect.adjusted(padding, padding, -padding, -(padding + label_height))

        # 获取数据
        labels = list(self.data.keys())
        if not labels:
            return

        # 计算最大值（总数）
        max_value = max([data['total'] for data in self.data.values()]) if self.data else 1

        # 动态计算柱宽
        min_bar_width = 40  # 最小柱宽
        max_bar_width = 100  # 最大柱宽
        spacing = 15  # 柱子间距

        # 根据柱子数量计算合适的宽度
        if len(labels) <= 5:
            # 少量柱子时使用较宽的柱子
            target_width = (chart_rect.width() - (len(labels) - 1) * spacing) / len(labels)
            bar_width = min(target_width, max_bar_width)
        elif len(labels) <= 10:
            # 中等数量时使用中等宽度
            bar_width = (chart_rect.width() - (len(labels) - 1) * spacing) / len(labels)
            bar_width = max(min(bar_width, 80), min_bar_width)
        else:
            # 大量柱子时使用最小宽度
            bar_width = min_bar_width

        # 计算所有柱子的总宽度
        total_bars_width = len(labels) * bar_width + (len(labels) - 1) * spacing

        # 计算起始x坐标，使柱状图居中
        start_x = chart_rect.left() + (chart_rect.width() - total_bars_width) // 2

        # 文字颜色
        if self.theme == "light":
            text_color = QColor(0, 0, 0)
        else:
            text_color = QColor(255, 255, 255)

        # 绘制柱形
        for i, (label, data) in enumerate(self.data.items()):
            x = start_x + i * (bar_width + spacing)
            total = data['total']
            completed = data['completed']

            # 获取该分类的颜色
            base_color = self.colors[i % len(self.colors)]

            # 计算高度
            total_height = (total / max_value) * chart_rect.height()
            completed_height = (completed / max_value) * chart_rect.height()

            # 判断是否是hover状态
            is_hover = (self.hover_index == i)

            # 如果hover，调整宽度和位置
            if is_hover:
                hover_x = x - 3  # 向左扩展3像素
                hover_width = bar_width + 6  # 宽度增加6像素
                hover_total_height = total_height + 5  # 高度增加5像素
                hover_completed_height = completed_height + 5  # 高度增加5像素
            else:
                hover_x = x
                hover_width = bar_width
                hover_total_height = total_height
                hover_completed_height = completed_height

            # 绘制总数柱形（底层）- 使用较深的颜色，添加阴影效果
            if is_hover:
                total_color = QColor(
                    min(255, int(base_color.red() * 0.6) + 20),
                    min(255, int(base_color.green() * 0.6) + 20),
                    min(255, int(base_color.blue() * 0.6) + 20),
                    200
                )
            else:
                total_color = QColor(
                    int(base_color.red() * 0.6),
                    int(base_color.green() * 0.6),
                    int(base_color.blue() * 0.6),
                    200
                )
            total_y = chart_rect.bottom() - hover_total_height

            # 绘制阴影
            if is_hover:
                shadow_rect = QRect(hover_x + 2, total_y + 2, hover_width, hover_total_height)
                painter.fillRect(shadow_rect, QColor(0, 0, 0, 40))
            else:
                shadow_rect = QRect(x + 2, total_y + 2, bar_width, total_height)
                painter.fillRect(shadow_rect, QColor(0, 0, 0, 30))

            # 绘制柱形主体
            painter.fillRect(hover_x, total_y, hover_width, hover_total_height, total_color)

            # 添加渐变效果
            if is_hover:
                gradient_rect = QRect(hover_x + 1, total_y + 1, hover_width - 2, hover_total_height - 2)
            else:
                gradient_rect = QRect(x + 1, total_y + 1, bar_width - 2, total_height - 2)
            gradient_color = QColor(
                int(base_color.red() * 0.7),
                int(base_color.green() * 0.7),
                int(base_color.blue() * 0.7),
                100
            )
            painter.fillRect(gradient_rect, gradient_color)

            # 绘制完成数柱形（上层）- 使用较浅的颜色，添加发光效果
            if completed > 0:
                if is_hover:
                    completed_color = QColor(
                        min(255, int(base_color.red() * 0.9) + 20),
                        min(255, int(base_color.green() * 0.9) + 20),
                        min(255, int(base_color.blue() * 0.9) + 20),
                        180
                    )
                else:
                    completed_color = QColor(
                        int(base_color.red() * 0.9),
                        int(base_color.green() * 0.9),
                        int(base_color.blue() * 0.9),
                        180
                    )
                completed_y = chart_rect.bottom() - hover_completed_height

                # 添加发光效果（外发光）
                if is_hover:
                    glow_rect = QRect(hover_x - 1, completed_y - 1, hover_width + 2, hover_completed_height + 2)
                    glow_color = QColor(
                        min(255, int(base_color.red()) + 20),
                        min(255, int(base_color.green()) + 20),
                        min(255, int(base_color.blue()) + 20),
                        60
                    )
                else:
                    glow_rect = QRect(x - 1, completed_y - 1, bar_width + 2, completed_height + 2)
                    glow_color = QColor(
                        int(base_color.red()),
                        int(base_color.green()),
                        int(base_color.blue()),
                        50
                    )
                painter.fillRect(glow_rect, glow_color)

                # 绘制完成数柱形主体
                painter.fillRect(hover_x, completed_y, hover_width, hover_completed_height, completed_color)

                # 添加高光效果 - 顶部高光
                if is_hover:
                    top_highlight_height = 6
                    top_highlight_alpha = 100
                    right_highlight_width = 8
                    right_highlight_alpha = 60
                else:
                    top_highlight_height = 4
                    top_highlight_alpha = 80
                    right_highlight_width = 6
                    right_highlight_alpha = 40

                # 顶部高光（渐变效果）- 避免与右侧高光重叠
                top_highlight_rect = QRect(hover_x + 2, completed_y + 2, hover_width - right_highlight_width - 2, top_highlight_height)
                top_highlight_color = QColor(255, 255, 255, top_highlight_alpha)
                painter.fillRect(top_highlight_rect, top_highlight_color)

                # 右侧高光（垂直渐变效果）- 从顶部高光下方2像素开始，延伸到底部
                right_highlight_rect = QRect(hover_x + hover_width - right_highlight_width, completed_y + top_highlight_height + 2,
                                            right_highlight_width, hover_completed_height - top_highlight_height - 2)
                right_highlight_color = QColor(255, 255, 255, right_highlight_alpha)
                painter.fillRect(right_highlight_rect, right_highlight_color)

                # 右上角单独的高光区域（不与其他高光重叠）
                if is_hover:
                    corner_highlight_rect = QRect(hover_x + hover_width - right_highlight_width, completed_y + 2,
                                                 right_highlight_width, top_highlight_height)
                    corner_highlight_color = QColor(255, 255, 255, top_highlight_alpha + 20)  # 稍微亮一点
                    painter.fillRect(corner_highlight_rect, corner_highlight_color)



            # 绘制总数（在柱子上方）
            painter.setPen(text_color)
            painter.drawText(x, total_y - 20, bar_width, 20,
                           Qt.AlignmentFlag.AlignCenter, str(total))

            # 绘制完成数（在柱子内部底部）
            if completed > 0:
                if self.theme == "light":
                    painter.setPen(QColor(0, 0, 0))  # 浅色主题用黑色文字
                else:
                    painter.setPen(QColor(255, 255, 255))  # 深色主题用白色文字
                # 在完成数柱形的底部显示数字
                painter.drawText(x, chart_rect.bottom() - 20, bar_width, 20,
                               Qt.AlignmentFlag.AlignCenter, str(completed))

            # 绘制标签（水平显示，垂直排列）
            # 根据柱子宽度动态计算每行显示的字数
            font = painter.font()
            font.setPointSize(8)
            painter.setFont(font)

            # 获取字体度量
            fm = QFontMetrics(font)
            char_width = fm.horizontalAdvance("测")  # 获取单个字符的平均宽度

            # 计算可用的文字宽度（留出边距）
            available_width = bar_width - 8  # 左右各留4像素边距

            # 计算每行最多显示的字符数
            max_chars_per_line = max(1, int(available_width / char_width))

            # 将长文本分成多行
            lines = []
            for i in range(0, len(label), max_chars_per_line):
                lines.append(label[i:i+max_chars_per_line])

            # 绘制每一行
            painter.setPen(self.text_color)
            y_offset = chart_rect.bottom() + 10
            for line in lines:
                # 确保文字在柱子范围内，留出边距
                painter.drawText(x + 4, y_offset, bar_width - 8, 15,
                               Qt.AlignmentFlag.AlignCenter, line)
                y_offset += 15  # 行间距



    def draw_horizontal_bar(self, painter, rect):
        """绘制水平条形图"""
        if not self.data:
            return

        # 计算绘图区域，给标签留更多空间
        label_width = 60
        padding = 20
        chart_rect = rect.adjusted(label_width + padding, padding, -padding, -padding)

        # 获取数据并排序（按值降序）
        sorted_items = sorted(self.data.items(), key=lambda x: x[1], reverse=True)

        # 限制显示最多10个版本
        max_display = 10
        if len(sorted_items) > max_display:
            sorted_items = sorted_items[:max_display]
            # 添加"其他"项
            other_count = sum(self.data.values()) - sum(item[1] for item in sorted_items)
            if other_count > 0:
                sorted_items.append(("其他", other_count))

        labels = [item[0] for item in sorted_items]
        values = [item[1] for item in sorted_items]

        if not labels:
            return

        # 计算最大值
        max_value = max(values) if values and max(values) > 0 else 1

        # 计算条形高度和间距
        bar_height = min(30, (chart_rect.height() - (len(labels) - 1) * 5) / len(labels))

        # 绘制条形
        for i, (label, value) in enumerate(zip(labels, values)):
            y = chart_rect.top() + i * (bar_height + 5)
            bar_width = (value / max_value) * chart_rect.width() if max_value > 0 else 0

            # 判断是否是hover状态
            is_hover = (self.hover_index == i)

            if is_hover:
                # 如果hover，只调整高度
                hover_y = y - 2  # 向上移动2像素
                hover_height = bar_height + 4  # 高度增加4像素

                # 绘制hover阴影
                shadow_rect = QRect(chart_rect.left() + 2, hover_y + 2, bar_width, hover_height)
                painter.fillRect(shadow_rect, QColor(0, 0, 0, 40))

                # 使用更亮的颜色，保持相同宽度
                hover_color = QColor(
                    min(255, self.colors[i % len(self.colors)].red() + 20),
                    min(255, self.colors[i % len(self.colors)].green() + 20),
                    min(255, self.colors[i % len(self.colors)].blue() + 20)
                )
                painter.fillRect(chart_rect.left(), hover_y, bar_width, hover_height, hover_color)

                # 添加内部高光（无边框）
                inner_highlight = QRect(chart_rect.left() + 2, hover_y + 2, bar_width - 4, hover_height // 3)
                inner_highlight_color = QColor(255, 255, 255, 30)
                painter.fillRect(inner_highlight, inner_highlight_color)
            else:
                # 绘制阴影
                shadow_rect = QRect(chart_rect.left() + 2, y + 2, bar_width, bar_height)
                painter.fillRect(shadow_rect, QColor(0, 0, 0, 30))

                # 绘制圆角矩形条形
                painter.fillRect(chart_rect.left(), y, bar_width, bar_height, self.colors[i % len(self.colors)])

                # 添加高光效果
                highlight_rect = QRect(chart_rect.left() + 2, y + 2, bar_width - 4, bar_height // 3)
                highlight_color = QColor(255, 255, 255, 40)
                painter.fillRect(highlight_rect, highlight_color)

            # 绘制标签（左侧外部显示版本号，垂直居中）
            # 确保文字颜色与背景有对比
            if self.theme == "light":
                painter.setPen(QColor(0, 0, 0))  # 深色文字
            else:
                painter.setPen(QColor(255, 255, 255))  # 白色文字
            label_y = y - 2 if self.hover_index == i else y # hover时向上移动
            # 标签绘制在图表区域左侧，与条形对齐
            painter.drawText(padding, label_y, label_width, bar_height + 4,
                           Qt.AlignmentFlag.AlignCenter, label)

            # 绘制数值（条内右侧显示数量）
            painter.setPen(QColor(255, 255, 255))  # 白色文字，在条形内更清晰
            value_y = y - 2 if self.hover_index == i else y
            # 数值绘制在条形内部右侧
            painter.drawText(chart_rect.left() + bar_width - 45, value_y, 40, bar_height + 4,
                           Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignVCenter, str(value))




class TooltipWidget(QWidget):
    """悬浮提示框"""
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowFlags(Qt.WindowType.ToolTip | Qt.WindowType.FramelessWindowHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)  # 启用透明背景
        self.setStyleSheet("""
            QWidget {
                border: 1px solid rgba(100, 100, 100, 200);
                padding: 8px 12px;
            }
            QLabel {
                color: white;
                font-size: 12px;
                font-weight: bold;
                background-color: transparent;
                border-radius: 4px;
                padding: 2px 4px;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(2)

        self.title_label = QLabel()
        self.title_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.title_label)

        self.subtitle_label = QLabel()
        self.subtitle_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.subtitle_label)

        self.hide()

    def show_tooltip(self, pos, title, subtitle=""):
        """显示提示框"""
        self.title_label.setText(title)
        if subtitle:
            self.subtitle_label.setText(subtitle)
            self.subtitle_label.show()
        else:
            self.subtitle_label.hide()

        # 调整大小
        self.adjustSize()

        # 确保在屏幕范围内显示
        screen = self.screen().availableGeometry()
        x = pos.x()
        y = pos.y() - self.height() - 10

        if x + self.width() > screen.right():
            x = screen.right() - self.width()
        if x < screen.left():
            x = screen.left()
        if y < screen.top():
            y = pos.y() + 10

        self.move(x, y)
        self.show()

    def paintEvent(self, event):
        """绘制背景"""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # 绘制圆角矩形背景
        painter.setBrush(QColor(40, 40, 40, 120))  # 50% 透明度
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawRoundedRect(self.rect(), 6, 6)


class StatisticsTab(QWidget):
    """统计信息标签页"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("statistics_tab")
        self.current_user = config.get_current_user()
        self.base_achievements = []
        self.user_progress = {}
        self.merged_achievements = []

        # 创建提示框
        self.tooltip = TooltipWidget(self)

        self.init_ui()

        # 监听用户切换信号
        signal_bus.user_switched.connect(self.on_user_switched)
        signal_bus.theme_changed.connect(self.on_theme_changed)

        # 直接加载数据
        self.load_data()

    def init_ui(self):
        """初始化UI"""
        layout = QVBoxLayout(self)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(15)

        # 控制面板
        control_group = QGroupBox("统计控制")
        control_main_layout = QVBoxLayout(control_group)

        # 第一行筛选
        filter_layout = QHBoxLayout()

        # 用户选择
        user_label = QLabel("用户:")
        user_label.setFixedWidth(100)
        user_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        filter_layout.addWidget(user_label)

        self.user_combo = QComboBox()
        self.user_combo.currentTextChanged.connect(self.on_user_changed)
        self.user_combo.setFixedWidth(120)
        filter_layout.addWidget(self.user_combo)

        # 第一分类筛选
        first_category_label = QLabel("第一分类:")
        first_category_label.setFixedWidth(70)
        first_category_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        filter_layout.addWidget(first_category_label)

        self.first_category_filter = QComboBox()
        self.first_category_filter.addItem("全部")
        self.first_category_filter.setFixedWidth(120)
        self.first_category_filter.currentTextChanged.connect(self.on_first_category_changed)
        filter_layout.addWidget(self.first_category_filter)

        # 第二分类筛选
        second_category_label = QLabel("第二分类:")
        second_category_label.setFixedWidth(70)
        second_category_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        filter_layout.addWidget(second_category_label)

        self.second_category_filter = QComboBox()
        self.second_category_filter.addItem("全部")
        self.second_category_filter.setFixedWidth(120)
        self.second_category_filter.currentTextChanged.connect(self.update_statistics)
        filter_layout.addWidget(self.second_category_filter)

        # 版本筛选
        version_label = QLabel("版本:")
        version_label.setFixedWidth(50)
        version_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        filter_layout.addWidget(version_label)

        self.version_filter = QComboBox()
        self.version_filter.addItem("全部")
        self.version_filter.setFixedWidth(100)
        self.version_filter.currentTextChanged.connect(self.update_statistics)
        filter_layout.addWidget(self.version_filter)

        # 添加弹性空间
        filter_layout.addStretch()

        # 刷新按钮
        self.refresh_btn = QPushButton("刷新统计")
        self.refresh_btn.clicked.connect(self.load_data)
        self.refresh_btn.setFixedWidth(100)
        filter_layout.addWidget(self.refresh_btn)

        control_main_layout.addLayout(filter_layout)
        layout.addWidget(control_group)

        # 统计信息
        stats_group = QGroupBox("统计概览")
        stats_layout = QHBoxLayout(stats_group)

        # 添加左侧弹性空间
        stats_layout.addStretch()

        # 总成就数
        self.total_label = QLabel("总成就数: 0")
        self.total_label.setStyleSheet("font-size: 16px; font-weight: bold;")
        stats_layout.addWidget(self.total_label)

        # 添加中间间距
        stats_layout.addSpacing(80)

        # 已完成
        self.completed_label = QLabel("已完成: 0")
        self.completed_label.setStyleSheet("color: #27ae60; font-size: 16px; font-weight: bold;")
        stats_layout.addWidget(self.completed_label)

        # 添加中间间距
        stats_layout.addSpacing(80)

        # 完成率
        self.completion_rate_label = QLabel("完成率: 0%")
        self.completion_rate_label.setStyleSheet("color: #3498db; font-size: 16px; font-weight: bold;")
        stats_layout.addWidget(self.completion_rate_label)

        # 添加中间间距
        stats_layout.addSpacing(80)

        # 暂不可获取
        self.unavailable_label = QLabel("暂不可获取: 0")
        self.unavailable_label.setStyleSheet("color: #e67e22; font-size: 16px; font-weight: bold;")
        stats_layout.addWidget(self.unavailable_label)

        # 添加右侧弹性空间
        stats_layout.addStretch()

        layout.addWidget(stats_group)

        # 图表区域
        charts_layout = QGridLayout()
        charts_layout.setSpacing(10)  # 设置组件之间的间距
        charts_layout.setContentsMargins(0, 0, 0, 0)  # 移除布局边距

        # 1. 完成状态饼图（缩小尺寸）
        self.pie_chart = SimpleChartWidget("pie")
        self.pie_chart.setMinimumSize(250, 250)
        charts_layout.addWidget(self.pie_chart, 0, 0)

        # 2. 分类完成情况柱状图（增大宽度和高度）
        self.bar_chart = SimpleChartWidget("bar")
        self.bar_chart.setMinimumSize(600, 350)
        charts_layout.addWidget(self.bar_chart, 0, 1)

        # 3. 版本分布图
        self.version_chart = SimpleChartWidget("horizontal_bar")
        self.version_chart.setMinimumSize(800, 200)
        charts_layout.addWidget(self.version_chart, 1, 0, 1, 2)

        layout.addLayout(charts_layout)

        # 应用按钮样式
        self.refresh_btn.setStyleSheet(get_button_style(config.theme))

        self.setLayout(layout)

    def on_user_switched(self, username):
        """用户切换时更新数据"""
        self.current_user = username
        self.load_data()

    def on_theme_changed(self, theme):
        """主题切换时更新样式"""
        # 更新按钮样式
        self.refresh_btn.setStyleSheet(get_button_style(theme))

        # 更新图表主题
        self.pie_chart.theme = theme
        self.bar_chart.theme = theme
        self.version_chart.theme = theme

        # 更新图表颜色
        if theme == "light":
            self.pie_chart.bg_color = QColor(0, 0, 0, 0)
            self.pie_chart.text_color = QColor(0, 0, 0)
            self.bar_chart.bg_color = QColor(0, 0, 0, 0)
            self.bar_chart.text_color = QColor(0, 0, 0)
            self.version_chart.bg_color = QColor(0, 0, 0, 0)
            self.version_chart.text_color = QColor(0, 0, 0)
        else:
            self.pie_chart.bg_color = QColor(0, 0, 0, 0)
            self.pie_chart.text_color = QColor(255, 255, 255)
            self.bar_chart.bg_color = QColor(0, 0, 0, 0)
            self.bar_chart.text_color = QColor(255, 255, 255)
            self.version_chart.bg_color = QColor(0, 0, 0, 0)
            self.version_chart.text_color = QColor(255, 255, 255)

        # 重绘图表
        self.update_statistics()

    def on_user_changed(self):
        """用户选择变化时更新统计"""
        self.current_user = self.user_combo.currentText()
        self.load_data()

    def load_data(self):
        """加载数据"""
        # 确保有当前用户
        if not self.current_user:
            self.current_user = config.get_current_user()

        # 加载基础成就数据
        try:
            self.base_achievements = config.load_base_achievements()
        except Exception as e:
            logger.error("加载基础成就数据失败: %s", e)
            self.base_achievements = []

        # 加载用户进度
        if self.current_user:
            try:
                self.user_progress = config.load_user_progress(self.current_user)
            except Exception as e:
                logger.error("加载用户进度失败: %s", e)
                self.user_progress = {}

        # 检查UI是否已经初始化
        if hasattr(self, 'first_category_filter'):
            # 更新筛选器
            self.update_filters()

            # 更新用户列表（避免循环调用）
            self._update_user_list_without_signal()

            # 确保用户下拉框选择了正确的当前用户
            if self.current_user:
                index = self.user_combo.findText(self.current_user)
                if index >= 0:
                    self.user_combo.setCurrentIndex(index)

            # 更新统计
            self.update_statistics()

    def update_user_list(self):
        """更新用户列表"""
        self._update_user_list_without_signal()

    def _update_user_list_without_signal(self):
        """更新用户列表（不触发信号）"""
        users = config.get_users()

        # 临时断开信号连接
        self.user_combo.blockSignals(True)

        self.user_combo.clear()
        for username in users.keys():
            self.user_combo.addItem(username)

        # 选择当前用户
        if self.current_user and self.current_user in users:
            index = self.user_combo.findText(self.current_user)
            if index >= 0:
                self.user_combo.setCurrentIndex(index)
        elif self.user_combo.count() > 0:
            # 如果当前用户不在列表中，选择第一个用户
            self.user_combo.setCurrentIndex(0)
            self.current_user = self.user_combo.currentText()

        # 重新连接信号
        self.user_combo.blockSignals(False)

    def update_filters(self):
        """更新筛选器选项"""
        # 更新第一分类
        first_categories = set()
        second_categories = set()
        versions = set()

        for achievement in self.base_achievements:
            first_categories.add(achievement.get('第一分类', ''))
            second_categories.add(achievement.get('第二分类', ''))
            versions.add(achievement.get('版本', ''))

        # 保存当前选择
        current_first = self.first_category_filter.currentText()
        current_second = self.second_category_filter.currentText()
        current_version = self.version_filter.currentText()

        # 更新第一分类
        self.first_category_filter.clear()
        self.first_category_filter.addItem("全部")
        for category in sorted(first_categories):
            if category:
                self.first_category_filter.addItem(category)

        # 恢复选择
        index = self.first_category_filter.findText(current_first)
        if index >= 0:
            self.first_category_filter.setCurrentIndex(index)

        # 更新版本
        self.version_filter.clear()
        self.version_filter.addItem("全部")
        for version in sorted(versions, reverse=True):
            if version:
                self.version_filter.addItem(version)

        # 恢复选择
        index = self.version_filter.findText(current_version)
        if index >= 0:
            self.version_filter.setCurrentIndex(index)

    def on_first_category_changed(self):
        """第一分类变化时更新第二分类选项并重置为全部"""
        first_category = self.first_category_filter.currentText()

        # 清空第二分类选项
        self.second_category_filter.clear()
        self.second_category_filter.addItem("全部")

        # 根据第一分类筛选第二分类
        if first_category != "全部":
            second_categories = set()
            for achievement in self.base_achievements:
                if achievement.get('第一分类', '') == first_category:
                    second_categories.add(achievement.get('第二分类', ''))

            for category in sorted(second_categories):
                if category:
                    self.second_category_filter.addItem(category)
        else:
            # 显示所有第二分类
            second_categories = set()
            for achievement in self.base_achievements:
                second_categories.add(achievement.get('第二分类', ''))

            for category in sorted(second_categories):
                if category:
                    self.second_category_filter.addItem(category)

        # 强制重置第二分类为"全部"
        self.second_category_filter.setCurrentIndex(0)

        # 触发更新
        self.update_statistics()

    def filter_achievements(self):
        """筛选成就数据"""
        filtered = []

        first_category = self.first_category_filter.currentText()
        second_category = self.second_category_filter.currentText()
        version = self.version_filter.currentText()

        # 使用合并后的数据
        for achievement in self.merged_achievements:
            # 第一分类筛选
            if first_category != "全部" and achievement.get('第一分类', '') != first_category:
                continue

            # 第二分类筛选
            if second_category != "全部" and achievement.get('第二分类', '') != second_category:
                continue

            # 版本筛选
            if version != "全部" and achievement.get('版本', '') != version:
                continue

            filtered.append(achievement)

        return filtered

    def merge_data(self):
        """合并基础成就数据和用户进度数据"""
        # 创建基础成就数据的副本
        merged_achievements = {}
        for achievement in self.base_achievements:
            key = achievement['编号']
            merged_achievements[key] = achievement.copy()

        # 将用户进度数据合并到基础数据上
        for key, value in self.user_progress.items():
            if key in merged_achievements:
                merged_achievements[key].update(value)

        # 转换为列表
        self.merged_achievements = list(merged_achievements.values())

    def calculate_statistics(self, achievements, version_filter='全部'):
        """计算统计数据"""
        stats = {
            'total': 0,
            'completed': 0,
            'incomplete': 0,
            'unavailable': 0,
            'completion_rate': 0,
            'categories': {},
            'subcategories': {},
            'versions': {}
        }

        # 预处理：收集成就组信息
        groups = {}
        for achievement in achievements:
            group_id = achievement.get('成就组ID')
            if group_id:
                if group_id not in groups:
                    groups[group_id] = []
                groups[group_id].append(achievement)

        # 统计完成状态
        processed_groups = set()
        for achievement in achievements:
            # 统计完成状态
            status = achievement.get('获取状态', '')
            group_id = achievement.get('成就组ID')
            
            if status == '已完成':
                if group_id:
                    # 成就组：检查是否已统计过
                    if group_id not in processed_groups:
                        stats['completed'] += 1
                        processed_groups.add(group_id)
                else:
                    # 普通成就
                    stats['completed'] += 1
            elif status == '暂不可获取':
                stats['unavailable'] += 1

            # 统计分类
            category = achievement.get('第一分类', '未知')
            if category not in stats['categories']:
                stats['categories'][category] = {'total': 0, 'completed': 0, 'processed_groups': set()}
            
            group_id = achievement.get('成就组ID')
            if group_id:
                # 成就组：每个组只统计一次
                if group_id not in stats['categories'][category]['processed_groups']:
                    stats['categories'][category]['total'] += 1
                    if group_id in processed_groups:  # 该组已完成
                        stats['categories'][category]['completed'] += 1
                    stats['categories'][category]['processed_groups'].add(group_id)
            else:
                # 普通成就
                stats['categories'][category]['total'] += 1
                if status == '已完成':
                    stats['categories'][category]['completed'] += 1

            # 统计第二分类
            subcategory = achievement.get('第二分类', '未知')
            if subcategory not in stats['subcategories']:
                stats['subcategories'][subcategory] = {'total': 0, 'completed': 0, 'processed_groups': set()}
            
            if group_id:
                # 成就组：每个组只统计一次
                if group_id not in stats['subcategories'][subcategory]['processed_groups']:
                    stats['subcategories'][subcategory]['total'] += 1
                    if group_id in processed_groups:  # 该组已完成
                        stats['subcategories'][subcategory]['completed'] += 1
                    stats['subcategories'][subcategory]['processed_groups'].add(group_id)
            else:
                # 普通成就
                stats['subcategories'][subcategory]['total'] += 1
                if status == '已完成':
                    stats['subcategories'][subcategory]['completed'] += 1

            # 统计版本（考虑成就组）
            # 使用一个单独的集合来跟踪已处理的成就组
            if 'processed_version_groups' not in stats:
                stats['processed_version_groups'] = set()
            
            if group_id:
                # 成就组：每个组只统计一次版本
                if group_id not in stats['processed_version_groups']:
                    version = achievement.get('版本', '未知')
                    if version_filter == '全部':
                        # 全部时，按大版本统计
                        if version != '未知':
                            # 提取大版本号（如2.7 -> 2.0）
                            major_version = version.split('.')[0] + '.0'
                            if major_version not in stats['versions']:
                                stats['versions'][major_version] = 0
                            stats['versions'][major_version] += 1
                        else:
                            if '未知' not in stats['versions']:
                                stats['versions']['未知'] = 0
                            stats['versions']['未知'] += 1
                    else:
                        # 具体版本时，显示该大版本下所有小版本
                        if version != '未知':
                            # 获取筛选条件的大版本号
                            filter_major = version_filter.split('.')[0] + '.0'
                            achievement_major = version.split('.')[0] + '.0'

                            # 只统计同一大版本的
                            if achievement_major == filter_major:
                                if version not in stats['versions']:
                                    stats['versions'][version] = 0
                                stats['versions'][version] += 1
                        else:
                            if '未知' not in stats['versions']:
                                stats['versions']['未知'] = 0
                            stats['versions']['未知'] += 1
                    
                    stats['processed_version_groups'].add(group_id)
            else:
                # 普通成就：直接统计版本
                version = achievement.get('版本', '未知')
                if version_filter == '全部':
                    # 全部时，按大版本统计
                    if version != '未知':
                        # 提取大版本号（如2.7 -> 2.0）
                        major_version = version.split('.')[0] + '.0'
                        if major_version not in stats['versions']:
                            stats['versions'][major_version] = 0
                        stats['versions'][major_version] += 1
                    else:
                        if '未知' not in stats['versions']:
                            stats['versions']['未知'] = 0
                        stats['versions']['未知'] += 1
                else:
                    # 具体版本时，显示该大版本下所有小版本
                    if version != '未知':
                        # 获取筛选条件的大版本号
                        filter_major = version_filter.split('.')[0] + '.0'
                        achievement_major = version.split('.')[0] + '.0'

                        # 只统计同一大版本的
                        if achievement_major == filter_major:
                            if version not in stats['versions']:
                                stats['versions'][version] = 0
                            stats['versions'][version] += 1
                    else:
                        if '未知' not in stats['versions']:
                            stats['versions']['未知'] = 0
                        stats['versions']['未知'] += 1

        # 计算未完成的成就组
        for group_id in groups:
            if group_id not in processed_groups:  # 该组没有完成
                stats['incomplete'] += 1
        
        # 计算未完成的普通成就
        for achievement in achievements:
            status = achievement.get('获取状态', '')
            group_id = achievement.get('成就组ID')
            if status != '已完成' and status != '暂不可获取' and not group_id:
                stats['incomplete'] += 1
        
        # 计算总计（考虑成就组）
        total_groups = len(groups)
        normal_achievements = sum(1 for a in achievements if not a.get('成就组ID'))
        stats['total'] = total_groups + normal_achievements

        # 清理临时数据
        for category in stats['categories']:
            if 'processed_groups' in stats['categories'][category]:
                del stats['categories'][category]['processed_groups']
        
        for subcategory in stats['subcategories']:
            if 'processed_groups' in stats['subcategories'][subcategory]:
                del stats['subcategories'][subcategory]['processed_groups']
        
        # 清理版本统计的临时数据
        if 'processed_version_groups' in stats:
            del stats['processed_version_groups']

        # 计算完成率
        if stats['total'] > 0:
            stats['completion_rate'] = int(stats['completed'] * 100 / stats['total'])
            stats['unavailable_rate'] = int(stats['unavailable'] * 100 / stats['total'])

        return stats

    def calculate_version_stats(self, achievements, version_filter):
        """单独计算版本统计（考虑成就组）"""
        version_stats = {}
        processed_groups = set()  # 跟踪已处理的成就组

        for achievement in achievements:
            group_id = achievement.get('成就组ID')
            version = achievement.get('版本', '未知')

            # 如果是成就组，检查是否已处理过
            if group_id and group_id in processed_groups:
                continue

            if group_id:
                # 标记该组已处理
                processed_groups.add(group_id)

            if version_filter == '全部':
                # 全部时，按大版本统计
                if version != '未知':
                    # 提取大版本号（如2.7 -> 2.0）
                    major_version = version.split('.')[0] + '.0'
                    if major_version not in version_stats:
                        version_stats[major_version] = 0
                    version_stats[major_version] += 1
                else:
                    if '未知' not in version_stats:
                        version_stats['未知'] = 0
                    version_stats['未知'] += 1
            else:
                # 具体版本时，显示该大版本下所有小版本
                if version != '未知':
                    # 获取筛选条件的大版本号
                    filter_major = version_filter.split('.')[0] + '.0'
                    achievement_major = version.split('.')[0] + '.0'

                    # 只统计同一大版本的
                    if achievement_major == filter_major:
                        if version not in version_stats:
                            version_stats[version] = 0
                        version_stats[version] += 1
                else:
                    if '未知' not in version_stats:
                        version_stats['未知'] = 0
                    version_stats['未知'] += 1

        return version_stats

    def update_statistics(self):
        """更新统计信息"""
        if not self.base_achievements:
            # 显示空数据
            self.pie_chart.set_data({})
            self.bar_chart.set_data({})
            self.version_chart.set_data({})
            return

        # 先合并数据
        self.merge_data()

        # 筛选数据
        filtered_achievements = self.filter_achievements()

        # 获取当前版本筛选条件
        version_filter = self.version_filter.currentText()

        # 统计数据（其他统计使用筛选后的数据）
        stats = self.calculate_statistics(filtered_achievements, version_filter)

        # 单独计算版本统计（使用所有数据）
        version_filter = self.version_filter.currentText()
        if version_filter == '全部':
            # 全部时，使用筛选后的数据按大版本统计
            version_stats = self.calculate_version_stats(filtered_achievements, '全部')
        else:
            # 具体版本时，使用所有合并数据统计该大版本下的所有小版本
            version_stats = self.calculate_version_stats(self.merged_achievements, version_filter)

        # 更新版本统计
        stats['versions'] = version_stats

        # 更新统计标签
        self.update_stat_labels(stats)

        # 更新图表
        self.update_charts(stats, filtered_achievements)

    def update_stat_labels(self, stats):
        """更新统计标签"""
        self.total_label.setText(f"总成就数: {stats['total']}")
        self.completed_label.setText(f"已完成: {stats['completed']}")
        self.completion_rate_label.setText(f"完成率: {stats['completion_rate']}%")
        self.unavailable_label.setText(f"暂不可获取: {stats['unavailable']}")

    def update_charts(self, stats, filtered_achievements):
        """更新图表"""
        # 1. 更新饼图 - 完成状态分布
        pie_data = {
            '已完成': stats['completed'],
            '未完成': stats['incomplete'],
            '暂不可获取': stats['unavailable']
        }
        self.pie_chart.set_data(pie_data)

        # 2. 更新柱状图 - 使用统一的stats数据
        bar_data = {}
        first_category = self.first_category_filter.currentText()
        second_category = self.second_category_filter.currentText()
        
        # 直接使用stats中已经计算好的分类数据
        if first_category == '全部':
            # 使用第一分类数据
            bar_data = stats['categories']
        elif second_category == '全部':
            # 使用第二分类数据
            bar_data = stats['subcategories']
        else:
            # 使用具体的第二分类数据
            # 从subcategories中获取对应的数据
            if second_category in stats['subcategories']:
                bar_data = {second_category: stats['subcategories'][second_category]}
            else:
                bar_data = {}
        

                

                

        self.bar_chart.set_data(bar_data)

        # 3. 更新版本分布图
        self.version_chart.set_data(stats['versions'])
    
    def leaveEvent(self, event):
        """鼠标离开事件"""
        self.tooltip.hide()
        super().leaveEvent(event)