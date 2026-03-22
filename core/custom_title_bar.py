# ui/custom_title_bar.py
import logging
import math
from PySide6.QtWidgets import QWidget, QHBoxLayout, QLabel, QApplication
from PySide6.QtCore import Qt, QPoint, Signal, QPropertyAnimation, QEasingCurve, Property, QEvent, QTimer, QPointF, QRectF
from PySide6.QtGui import QPainter, QColor, QPaintEvent, QMouseEvent, QBrush, QPen, QRadialGradient, QPainterPath

from core.config import config
from core.signal_bus import signal_bus

logger = logging.getLogger(__name__)


class SunMoonButton(QWidget):
    """日月切换按钮 - 用于主题切换"""
    
    statusChanged = Signal(bool)  # True: 夜晚(深色), False: 白天(浅色)
    
    def __init__(self, parent=None, size=30):
        super().__init__(parent)
        self.button_size = size
        self.setFixedSize(int(size * 2.5), size)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setMouseTracking(True)
        
        # 根据当前主题设置初始状态
        self._is_night = (config.theme == "dark")
        self._ball_position = 1.0 if self._is_night else 0.0
        self._sky_color_progress = 1.0 if self._is_night else 0.0
        self._hovering = False
        
        # 动画
        self.ball_animation = None
        self.sky_animation = None
        
        # 动画更新定时器
        self.animation_timer = QTimer(self)
        self.animation_timer.timeout.connect(self._update_animations)
        self.animation_timer.start(50)
        
        # 云朵数据
        self.clouds = [
            {"x": 0.85, "y": 0.15, "size": 1.0, "speed": 0.001},
            {"x": 0.95, "y": 0.39, "size": 1.1, "speed": 0.0008},
            {"x": 0.75, "y": 0.66, "size": 0.9, "speed": 0.0012},
            {"x": 0.65, "y": 0.85, "size": 1.3, "speed": 0.0009},
            {"x": 1.0, "y": 0.75, "size": 1.2, "speed": 0.0007},
            {"x": 0.9, "y": 0.55, "size": 0.8, "speed": 0.001},
        ]
        
        # 星星数据
        self.stars = [
            {"x": 0.2, "y": 0.13, "size": 1.5, "brightness": 1.0, "phase": 0},
            {"x": 0.1, "y": 0.28, "size": 0.5, "brightness": 0.8, "phase": 1},
            {"x": 0.22, "y": 0.43, "size": 0.7, "brightness": 0.6, "phase": 2},
            {"x": 0.53, "y": 0.21, "size": 1.3, "brightness": 0.9, "phase": 0.5},
            {"x": 0.42, "y": 0.20, "size": 0.4, "brightness": 0.7, "phase": 1.5},
            {"x": 0.52, "y": 0.53, "size": 0.6, "brightness": 0.8, "phase": 2.5},
            {"x": 0.15, "y": 0.55, "size": 1.2, "brightness": 1.0, "phase": 0.8},
            {"x": 0.08, "y": 0.65, "size": 0.9, "brightness": 0.95, "phase": 1.2},
            {"x": 0.25, "y": 0.70, "size": 1.4, "brightness": 1.0, "phase": 0.3},
            {"x": 0.18, "y": 0.80, "size": 0.7, "brightness": 0.9, "phase": 1.8},
            {"x": 0.12, "y": 0.48, "size": 0.8, "brightness": 0.85, "phase": 2.2},
        ]
        
        # 流星数据（支持多颗流星）
        self.meteors = [
            {
                "progress": -0.1,  # 第一颗立即出现
                "speed": 0.015,
                "start_x": 0.95,
                "start_y": -0.05,
                "end_x": 0.05,
                "end_y": 0.75,
                "tail_length": 0.12,
                "width_scale": 1.0,  # 粗细比例
            },
            {
                "progress": -0.15,  # 第二颗稍微延迟
                "speed": 0.012,
                "start_x": 0.85,
                "start_y": -0.1,
                "end_x": 0.15,
                "end_y": 0.65,
                "tail_length": 0.10,
                "width_scale": 0.6,  # 更细的流星
            },
            {
                "progress": -0.2,  # 第三颗再延迟一点
                "speed": 0.018,
                "start_x": 0.75,
                "start_y": 0.0,
                "end_x": 0.25,
                "end_y": 0.85,
                "tail_length": 0.08,
                "width_scale": 0.5,  # 最细的流星
            },
        ]
        self.meteor_active = False
        self.moon_rotation = 0.0
        self.moon_rotating = False
        self.cloud_shake_time = 0.0
        self.cloud_shaking = False
        self.star_twinkle_time = 0.0
        
        # 飞鸟数据（从太阳右下方飞向右上角）
        self.birds = [
            {"progress": 0.0, "speed": 0.008, "wing_phase": 0, "size": 0.8, "offset": 0},
            {"progress": -0.4, "speed": 0.007, "wing_phase": 1.5, "size": 0.6, "offset": 0.08},
            {"progress": -0.8, "speed": 0.009, "wing_phase": 3, "size": 0.7, "offset": -0.05},
        ]
        self.birds_active = False
        
        # 颜色定义
        self.day_color = QColor(135, 206, 250)  # 太阳照耀下的明亮天空蓝色
        self.night_color = QColor(23, 30, 51)
        self.sun_color = QColor(243, 198, 43)
        self.moon_color = QColor(195, 201, 211)
        
    def get_ball_position(self):
        return self._ball_position
        
    def set_ball_position(self, pos):
        self._ball_position = pos
        self.update()
        
    def get_sky_color_progress(self):
        return self._sky_color_progress
        
    def set_sky_color_progress(self, progress):
        self._sky_color_progress = progress
        self.update()
        
    ball_position = Property(float, get_ball_position, set_ball_position)
    sky_color_progress = Property(float, get_sky_color_progress, set_sky_color_progress)
    
    def toggle(self):
        """切换日夜模式"""
        # 直接切换主题配置
        config.theme = "light" if config.theme == "dark" else "dark"
        config.save_config()
        
        # 同步按钮状态
        self._is_night = (config.theme == "dark")
        self.statusChanged.emit(self._is_night)
        
        signal_bus.log_message.emit("SUCCESS", f"已切换到{'深色' if self._is_night else '浅色'}模式", {})
        
        # 停止现有动画
        if self.ball_animation:
            self.ball_animation.stop()
        if self.sky_animation:
            self.sky_animation.stop()
            
        # 创建球移动动画
        self.ball_animation = QPropertyAnimation(self, b"ball_position")
        self.ball_animation.setDuration(1200)
        self.ball_animation.setStartValue(self._ball_position)
        self.ball_animation.setEndValue(1.0 if self._is_night else 0.0)
        self.ball_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
        
        # 创建天空颜色动画
        self.sky_animation = QPropertyAnimation(self, b"sky_color_progress")
        self.sky_animation.setDuration(1200)
        self.sky_animation.setStartValue(self._sky_color_progress)
        self.sky_animation.setEndValue(1.0 if self._is_night else 0.0)
        self.sky_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
        
        # 开始动画
        self.ball_animation.start()
        self.sky_animation.start()
        
    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.toggle()
            event.accept()  # 阻止事件传播到父组件
            
    def enterEvent(self, event):
        self._hovering = True
        self._start_hover_animations()
        
    def leaveEvent(self, event):
        self._hovering = False
        self.cloud_shaking = False
        self.moon_rotating = False
        self.meteor_active = False
        self.birds_active = False
        
    def _start_hover_animations(self):
        if not self._hovering:
            return
        if self._is_night:
            self.meteor_active = True
            # 重置所有流星到初始位置（更接近可见区域）
            initial_positions = [-0.1, -0.15, -0.2]
            for i, meteor in enumerate(self.meteors):
                meteor["progress"] = initial_positions[i]  # 错开出现时间
            self.moon_rotating = True
        else:
            self.cloud_shaking = True
            self.birds_active = True
            
    def _update_animations(self):
        need_update = False
        if self._hovering and not self._is_night:
            for cloud in self.clouds:
                cloud["x"] += cloud["speed"]
                if cloud["x"] > 1.2:
                    cloud["x"] = -0.2
            need_update = True
        if self.cloud_shaking and self._hovering and not self._is_night:
            self.cloud_shake_time += 0.05
            need_update = True
            
        # 飞鸟移动
        if self.birds_active and self._hovering and not self._is_night:
            for bird in self.birds:
                bird["progress"] += bird["speed"]
                bird["wing_phase"] += 0.3
                if bird["progress"] > 1.2:
                    bird["progress"] = -0.2
            need_update = True
        if self._hovering and self._is_night:
            self.star_twinkle_time += 0.1
            for star in self.stars:
                star["brightness"] = 0.5 + 0.5 * abs(math.sin(self.star_twinkle_time + star["phase"]))
            need_update = True
        if self.meteor_active and self._hovering and self._is_night:
            # 更新所有流星
            for meteor in self.meteors:
                meteor["progress"] += meteor["speed"]
                if meteor["progress"] > 1.2:
                    meteor["progress"] = -0.1  # 循环播放
            need_update = True
        if self.moon_rotating and self._hovering and self._is_night:
            self.moon_rotation += 0.5
            if self.moon_rotation >= 360:
                self.moon_rotation = 0
            need_update = True
        if need_update:
            self.update()
    
    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        clip_path = QPainterPath()
        clip_path.addRoundedRect(QRectF(self.rect()), self.height() / 2, self.height() / 2)
        painter.setClipPath(clip_path)
        
        self._draw_sky(painter)
        self._draw_halo(painter)
        if self._sky_color_progress > 0.3:
            self._draw_stars(painter)
        if self._sky_color_progress < 0.7:
            self._draw_clouds(painter)
            if self.birds_active:
                self._draw_birds(painter)
        self._draw_ball(painter)
        self._draw_inner_shadow(painter)
        
    def _draw_sky(self, painter):
        r = int(self.day_color.red() * (1 - self._sky_color_progress) + self.night_color.red() * self._sky_color_progress)
        g = int(self.day_color.green() * (1 - self._sky_color_progress) + self.night_color.green() * self._sky_color_progress)
        b = int(self.day_color.blue() * (1 - self._sky_color_progress) + self.night_color.blue() * self._sky_color_progress)
        painter.setBrush(QBrush(QColor(r, g, b)))
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawRoundedRect(self.rect(), self.height() / 2, self.height() / 2)
        
    def _draw_halo(self, painter):
        ball_size = self.button_size * 0.85
        margin = self.button_size * 0.1
        ball_x = margin + ball_size / 2 + (self.width() - ball_size - 2 * margin) * self._ball_position
        ball_y = self.height() / 2
        halo_sizes = [ball_size * 1.47, ball_size * 1.77, ball_size * 2.07]
        for halo_size in halo_sizes:
            painter.setBrush(QBrush(QColor(255, 255, 255, 20)))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.drawEllipse(QPointF(ball_x, ball_y), halo_size / 2, halo_size / 2)
            
    def _draw_stars(self, painter):
        base_opacity = int(255 * (self._sky_color_progress - 0.3) / 0.7)
        painter.setPen(Qt.PenStyle.NoPen)
        for star in self.stars:
            x = self.width() * star["x"]
            y = self.height() * star["y"]
            size = self.button_size * 0.02 * star["size"]
            opacity = int(base_opacity * star["brightness"])
            painter.setBrush(QBrush(QColor(255, 255, 255, opacity)))
            path = QPainterPath()
            for i in range(5):
                angle = i * 144 * math.pi / 180
                px = x + size * math.cos(angle)
                py = y + size * math.sin(angle)
                if i == 0:
                    path.moveTo(px, py)
                else:
                    path.lineTo(px, py)
            path.closeSubpath()
            painter.drawPath(path)
        if self.meteor_active:
            # 绘制所有流星
            for meteor in self.meteors:
                if -0.1 <= meteor["progress"] <= 1.2:
                    self._draw_single_meteor(painter, meteor)
            
    def _draw_single_meteor(self, painter, meteor):
        """绘制单颗流星，带有长拖尾效果
        
        Args:
            painter: QPainter对象
            meteor: 流星配置字典，包含progress、start_x、start_y、end_x、end_y、tail_length、width_scale
        """
        # 流星运动路径的起点和终点（从胶囊外到胶囊外）
        start_x = self.width() * meteor["start_x"]
        start_y = self.height() * meteor["start_y"]
        end_x = self.width() * meteor["end_x"]
        end_y = self.height() * meteor["end_y"]
        
        # 根据进度计算流星头部当前位置
        head_x = start_x + (end_x - start_x) * meteor["progress"]
        head_y = start_y + (end_y - start_y) * meteor["progress"]
        
        # 拖尾长度
        tail_length = self.width() * meteor["tail_length"]
        width_scale = meteor["width_scale"]
        
        # 计算运动方向向量
        move_dx = end_x - start_x
        move_dy = end_y - start_y
        move_length = math.sqrt(move_dx**2 + move_dy**2)
        
        # 归一化运动方向
        move_dir_x = move_dx / move_length
        move_dir_y = move_dy / move_length
        
        # 拖尾方向与运动方向相反（指向来时的方向）
        tail_dx = -move_dir_x * tail_length
        tail_dy = -move_dir_y * tail_length
        
        painter.save()
        
        # 绘制多层拖尾，创造渐变效果
        tail_segments = [
            (0.0, 2.5, 220),   # 头部：最亮最粗
            (0.15, 2.2, 180),
            (0.3, 1.8, 140),
            (0.45, 1.4, 100),
            (0.6, 1.0, 60),
            (0.75, 0.7, 35),
            (0.9, 0.4, 15),
            (1.0, 0.2, 5),     # 尾部：最暗最细
        ]
        
        for progress, width, alpha in tail_segments:
            # 计算当前段的起点
            seg_x = head_x + tail_dx * progress
            seg_y = head_y + tail_dy * progress
            
            # 下一段的起点
            next_progress = min(progress + 0.15, 1.0)
            next_x = head_x + tail_dx * next_progress
            next_y = head_y + tail_dy * next_progress
            
            # 绘制拖尾段（应用粗细比例）
            pen = QPen(QColor(255, 255, 255, alpha), width * width_scale)
            pen.setCapStyle(Qt.PenCapStyle.RoundCap)
            painter.setPen(pen)
            painter.drawLine(QPointF(seg_x, seg_y), QPointF(next_x, next_y))
        
        # 绘制流星头部（小而亮，根据粗细比例调整大小）
        head_size = 2 * width_scale
        halo_size = 6 * width_scale
        
        painter.setBrush(QBrush(QColor(255, 255, 255, 255)))
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawEllipse(QPointF(head_x, head_y), head_size, head_size)
        
        # 添加流星头部的光晕
        gradient = QRadialGradient(head_x, head_y, halo_size)
        gradient.setColorAt(0, QColor(255, 255, 255, int(180 * width_scale)))
        gradient.setColorAt(0.4, QColor(200, 220, 255, int(100 * width_scale)))
        gradient.setColorAt(1, QColor(200, 220, 255, 0))
        painter.setBrush(QBrush(gradient))
        painter.drawEllipse(QPointF(head_x, head_y), halo_size, halo_size)
        
        painter.restore()
        
    def _draw_clouds(self, painter):
        opacity = int(255 * (1 - self._sky_color_progress / 0.7))
        painter.setPen(Qt.PenStyle.NoPen)
        for cloud in self.clouds:
            x = self.width() * cloud["x"]
            y = self.height() * cloud["y"]
            if self.cloud_shaking:
                shake_offset = math.sin(self.cloud_shake_time * 2) * self.height() * 0.02
                y += shake_offset
            base_size = self.button_size * 0.18 * cloud["size"]
            cloud_circles = [
                (0, 0, 1.2, 1.0), (-0.7, 0.15, 0.9, 0.9), (0.7, 0.15, 0.75, 0.85),
                (-0.4, -0.4, 0.5, 0.75), (0.4, -0.3, 0.55, 0.8), (0, 0.4, 0.6, 0.7),
            ]
            for x_offset, y_offset, size_ratio, opacity_ratio in cloud_circles:
                circle_x = x + base_size * x_offset
                circle_y = y + base_size * y_offset
                circle_size = base_size * size_ratio
                circle_opacity = int(opacity * opacity_ratio)
                painter.setBrush(QBrush(QColor(255, 255, 255, circle_opacity)))
                painter.drawEllipse(QPointF(circle_x, circle_y), circle_size, circle_size)
    
    def _draw_birds(self, painter):
        """绘制飞鸟 - 从太阳右下方飞向右上角"""
        painter.save()
        painter.setPen(Qt.PenStyle.NoPen)
        
        bird_color = QColor(80, 80, 80, 180)
        
        # 计算太阳位置
        ball_size = self.button_size * 0.85
        margin = self.button_size * 0.1
        sun_x = margin + ball_size / 2
        sun_y = self.height() / 2
        
        for bird in self.birds:
            # 飞行路径：从太阳右下方到右上角
            start_x = sun_x + ball_size * 0.7
            start_y = sun_y + ball_size * 0.7
            end_x = self.width() * 1.1
            end_y = self.height() * -0.1
            
            # 根据进度计算当前位置，加上偏移量让鸟分散
            progress = bird["progress"]
            x = start_x + (end_x - start_x) * progress
            y = start_y + (end_y - start_y) * progress + self.height() * bird["offset"]
            
            base_size = self.button_size * 0.4 * bird["size"]  # 5倍大小
            
            # 计算翅膀扇动角度
            wing_angle = math.sin(bird["wing_phase"]) * 25  # -25到25度
            
            painter.setBrush(QBrush(bird_color))
            painter.setPen(Qt.PenStyle.NoPen)
            
            # 计算飞行方向角度（从左下到右上约-45度）
            fly_angle = -45
            
            # 绘制鸟的身体（水滴形状，朝向飞行方向）
            painter.save()
            painter.translate(x, y)
            painter.rotate(fly_angle)
            
            body_path = QPainterPath()
            body_path.moveTo(0, -base_size * 0.2)  # 头部（尖端）
            body_path.quadTo(base_size * 0.12, -base_size * 0.05, base_size * 0.1, base_size * 0.15)  # 右侧
            body_path.quadTo(0, base_size * 0.2, -base_size * 0.1, base_size * 0.15)  # 底部圆弧
            body_path.quadTo(-base_size * 0.12, -base_size * 0.05, 0, -base_size * 0.2)  # 左侧
            painter.drawPath(body_path)
            
            painter.restore()
            
            # 绘制左翅膀
            painter.save()
            painter.translate(x - base_size * 0.1, y)
            painter.rotate(wing_angle)
            
            # 左翅膀路径（更流畅的曲线）
            left_wing = QPainterPath()
            left_wing.moveTo(0, 0)
            left_wing.quadTo(-base_size * 0.35, -base_size * 0.15, -base_size * 0.5, -base_size * 0.05)
            left_wing.quadTo(-base_size * 0.4, 0, -base_size * 0.2, base_size * 0.05)
            left_wing.quadTo(-base_size * 0.1, 0, 0, 0)
            painter.drawPath(left_wing)
            
            painter.restore()
            
            # 绘制右翅膀
            painter.save()
            painter.translate(x + base_size * 0.1, y)
            painter.rotate(-wing_angle)
            
            # 右翅膀路径（镜像）
            right_wing = QPainterPath()
            right_wing.moveTo(0, 0)
            right_wing.quadTo(base_size * 0.35, -base_size * 0.15, base_size * 0.5, -base_size * 0.05)
            right_wing.quadTo(base_size * 0.4, 0, base_size * 0.2, base_size * 0.05)
            right_wing.quadTo(base_size * 0.1, 0, 0, 0)
            painter.drawPath(right_wing)
            
            painter.restore()
        
        painter.restore()
                
    def _draw_ball(self, painter):
        ball_size = self.button_size * 0.85
        margin = self.button_size * 0.1
        ball_x = margin + ball_size / 2 + (self.width() - ball_size - 2 * margin) * self._ball_position
        ball_y = self.height() / 2
        painter.setBrush(QBrush(QColor(0, 0, 0, 100)))
        painter.setPen(Qt.PenStyle.NoPen)
        shadow_offset = ball_size * 0.05
        painter.drawEllipse(QPointF(ball_x + shadow_offset, ball_y + shadow_offset), ball_size / 2, ball_size / 2)
        if self._ball_position < 0.5:
            painter.save()
            painter.setPen(Qt.PenStyle.NoPen)
            corona_layers = [
                (ball_size * 0.65, QColor(255, 255, 220, 80)), (ball_size * 0.75, QColor(255, 245, 180, 60)),
                (ball_size * 0.85, QColor(255, 235, 140, 40)), (ball_size * 0.95, QColor(255, 220, 100, 25)),
                (ball_size * 1.05, QColor(255, 200, 80, 15)), (ball_size * 1.15, QColor(255, 180, 60, 8)),
            ]
            for size, color in corona_layers:
                gradient = QRadialGradient(ball_x, ball_y, size / 2)
                gradient.setColorAt(0, QColor(color.red(), color.green(), color.blue(), color.alpha()))
                gradient.setColorAt(0.7, QColor(color.red(), color.green(), color.blue(), color.alpha() // 2))
                gradient.setColorAt(1, QColor(color.red(), color.green(), color.blue(), 0))
                painter.setBrush(QBrush(gradient))
                painter.drawEllipse(QPointF(ball_x, ball_y), size / 2, size / 2)
            painter.restore()
            gradient = QRadialGradient(ball_x, ball_y - ball_size * 0.1, ball_size * 0.4)
            gradient.setColorAt(0, QColor(255, 255, 200))
            gradient.setColorAt(1, self.sun_color)
            painter.setBrush(QBrush(gradient))
        else:
            gradient = QRadialGradient(ball_x, ball_y - ball_size * 0.1, ball_size * 0.4)
            gradient.setColorAt(0, QColor(220, 220, 230))
            gradient.setColorAt(1, self.moon_color)
            painter.setBrush(QBrush(gradient))
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawEllipse(QPointF(ball_x, ball_y), ball_size / 2, ball_size / 2)
        if self._ball_position >= 0.5:
            painter.save()
            if self.moon_rotating:
                painter.translate(ball_x, ball_y)
                painter.rotate(self.moon_rotation)
                painter.translate(-ball_x, -ball_y)
            crater_positions = [(0.38, 0.15, 0.18), (0.13, 0.46, 0.32), (0.61, 0.61, 0.22)]
            painter.setBrush(QBrush(QColor(145, 151, 165)))
            for x_ratio, y_ratio, size_ratio in crater_positions:
                crater_x = ball_x - ball_size / 2 + ball_size * x_ratio
                crater_y = ball_y - ball_size / 2 + ball_size * y_ratio
                crater_size = ball_size * size_ratio
                painter.drawEllipse(QPointF(crater_x, crater_y), crater_size / 2, crater_size / 2)
            painter.restore()
            
    def _draw_inner_shadow(self, painter):
        pass


class CustomTitleBarButton(QWidget):
    """自定义标题栏按钮 - 带丝滑动效"""
    # 定义clicked信号
    clicked = Signal()
    
    def __init__(self, color, parent=None):
        super().__init__(parent)
        self._color = color
        self._normal_size = 12
        self._hover_size = 16
        self._current_size = self._normal_size
        self.setFixedSize(24, 24)  # 稍微加大按钮区域
        
        # 鼠标悬停状态
        self._is_hovered = False
        
        # 创建动画对象
        self._zoom_animation = QPropertyAnimation(self, b"current_size")
        self._zoom_animation.setDuration(200)
        self._zoom_animation.setEasingCurve(QEasingCurve(QEasingCurve.Type.OutBack))
    
    def get_current_size(self):
        return self._current_size
    
    def set_current_size(self, size):
        self._current_size = size
        self.update()  # 触发重绘
    
    # 使用Property装饰器注册属性
    current_size = Property(float, get_current_size, set_current_size)
    
    def enterEvent(self, event):
        """鼠标移入事件"""
        self._is_hovered = True
        self._zoom_animation.stop()  # 停止当前动画
        self._zoom_animation.setStartValue(self._current_size)
        self._zoom_animation.setEndValue(self._hover_size)
        self._zoom_animation.start()
        super().enterEvent(event)
    
    def leaveEvent(self, event):
        """鼠标移出事件"""
        self._is_hovered = False
        self._zoom_animation.stop()  # 停止当前动画
        self._zoom_animation.setStartValue(self._current_size)
        self._zoom_animation.setEndValue(self._normal_size)
        self._zoom_animation.start()
        super().leaveEvent(event)
    
    def paintEvent(self, event: QPaintEvent):
        """绘制圆点按钮"""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        # 绘制背景（悬停时添加轻微背景色）
        if self._is_hovered:
            bg_color = QColor(self._color)
            bg_color.setAlpha(30)
            painter.setBrush(bg_color)
            painter.setPen(Qt.PenStyle.NoPen)
            painter.drawEllipse(self.rect().adjusted(2, 2, -2, -2))
        
        # 绘制圆点
        center_x = self.width() / 2
        center_y = self.height() / 2
        radius = self._current_size / 2
        
        painter.setBrush(QColor(self._color))
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawEllipse(center_x - radius, center_y - radius,
                            self._current_size, self._current_size)
    
    def mousePressEvent(self, event: QMouseEvent):
        """鼠标点击时添加按下效果"""
        if event.button() == Qt.MouseButton.LeftButton:
            # 临时缩小一点模拟按下效果
            self._zoom_animation.stop()
            self._zoom_animation.setDuration(100)
            self._zoom_animation.setStartValue(self._current_size)
            self._zoom_animation.setEndValue(self._current_size - 2)
            self._zoom_animation.start()
        super().mousePressEvent(event)
    
    def mouseReleaseEvent(self, event: QMouseEvent):
        """鼠标释放时恢复"""
        if event.button() == Qt.MouseButton.LeftButton:
            self._zoom_animation.stop()
            self._zoom_animation.setDuration(100)
            self._zoom_animation.setStartValue(self._current_size)
            self._zoom_animation.setEndValue(self._hover_size if self._is_hovered else self._normal_size)
            self._zoom_animation.start()
            # 发射clicked信号
            self.clicked.emit()
        super().mouseReleaseEvent(event)


class CustomTitleBar(QWidget):
    """自定义标题栏 - macOS风格，按钮在右侧"""
    
    def __init__(self, parent=None, show_theme_toggle=False):
        super().__init__(parent)
        self.parent_window = parent
        self.drag_position = QPoint()
        self.show_theme_toggle = show_theme_toggle
        
        self.setFixedHeight(45)
        self.init_ui()
        self.update_theme()
    
    def init_ui(self):
        """初始化UI"""
        layout = QHBoxLayout(self)
        layout.setContentsMargins(10, 0, 10, 0)
        layout.setSpacing(8)
        
        # 左侧：图标（可点击）
        app = QApplication.instance()
        if app and not app.windowIcon().isNull():
            self.icon_label = QLabel()
            pixmap = app.windowIcon().pixmap(24, 24)
            self.icon_label.setPixmap(pixmap)
            self.icon_label.setFixedSize(24, 24)
            self.icon_label.setCursor(Qt.CursorShape.PointingHandCursor)  # 设置手型光标
            self.icon_label.installEventFilter(self)  # 安装事件过滤器
            layout.addWidget(self.icon_label)
        
        # 应用名称和版本号
        app_name = app.applicationName() if app else "应用程序"
        app_version = app.applicationVersion() if app else ""
        title_text = f"{app_name} v{app_version}" if app_version else app_name
        
        self.title_label = QLabel(title_text)
        self.title_label.setStyleSheet("font-weight: bold; font-size: 14px;")
        layout.addWidget(self.title_label)
        
        layout.addStretch()
        
        # 日月切换按钮（仅在主窗口显示）
        if self.show_theme_toggle:
            self.sun_moon_btn = SunMoonButton(size=30)
            self.sun_moon_btn.statusChanged.connect(self.on_theme_changed)
            layout.addWidget(self.sun_moon_btn)
        
        # 右侧：控制按钮容器（Windows风格圆点）
        button_container = QWidget()
        button_container.setFixedHeight(45)  # 与标题栏相同的高度
        button_layout = QHBoxLayout(button_container)
        button_layout.setContentsMargins(0, 0, 0, 0)
        button_layout.setSpacing(8)
        button_layout.setAlignment(Qt.AlignmentFlag.AlignVCenter)  # 垂直居中对齐
        
        # 最大化按钮 - 绿色
        self.max_btn = CustomTitleBarButton("#27c93f")
        self.max_btn.clicked.connect(self.maximize_restore_window)
        
        # 最小化按钮 - 黄色
        self.min_btn = CustomTitleBarButton("#ffbd2e")
        self.min_btn.clicked.connect(self.minimize_window)
        
        # 关闭按钮 - 红色
        self.close_btn = CustomTitleBarButton("#ff5f56")
        self.close_btn.clicked.connect(self.close_window)
        
        button_layout.addWidget(self.min_btn)
        button_layout.addWidget(self.max_btn)
        button_layout.addWidget(self.close_btn)
        
        layout.addWidget(button_container)
    
    def on_theme_changed(self, is_night):
        """主题切换回调"""
        saved_pos = None
        saved_size = None
        
        if self.parent_window:
            saved_pos = self.parent_window.pos()
            saved_size = self.parent_window.size()
        
        self.update_theme()
        
        # 发送主题切换信号
        signal_bus.theme_changed.emit(config.theme)
        
        if self.parent_window and hasattr(self.parent_window, 'apply_theme'):
            self.parent_window.apply_theme()
        
        if self.parent_window and saved_pos is not None:
            self.parent_window.move(saved_pos)
            if saved_size is not None:
                self.parent_window.resize(saved_size)
    
    def update_theme(self):
        """更新主题样式"""
        if config.theme == "dark":
            # 深色主题 - 使用与对话框相同的背景色
            bg_color = "#3a3a3a"
            text_color = "#e0e0e0"
            btn_hover = "#4d4d4d"
            close_hover = "#c62828"
            
            self.setStyleSheet(f"""
                CustomTitleBar {{
                    background-color: {bg_color} !important;
                    border-bottom: 1px solid #2a2a2a;
                }}
            """)
            
            self.title_label.setStyleSheet(f"""
                QLabel {{
                    color: {text_color};
                    font-weight: bold;
                    font-size: 14px;
                }}
            """)
            
            # 更新按钮颜色
            self.min_btn._color = "#ffbd2e"
            self.max_btn._color = "#27c93f"
            self.close_btn._color = "#ff5f56"
            self.min_btn.update()
            self.max_btn.update()
            self.close_btn.update()
        else:
            # 浅色主题
            bg_color = "#f8f9fa"
            text_color = "#495057"
            btn_hover = "#e9ecef"
            close_hover = "#ff6b6b"
            
            self.setStyleSheet(f"""
                CustomTitleBar {{
                    background-color: {bg_color} !important;
                    border-bottom: 1px solid #dee2e6;
                }}
            """)
            
            self.title_label.setStyleSheet(f"""
                QLabel {{
                    color: {text_color};
                    font-weight: bold;
                    font-size: 14px;
                }}
            """)
            
            # 更新按钮颜色
            self.min_btn._color = "#ffbd2e"
            self.max_btn._color = "#27c93f"
            self.close_btn._color = "#ff5f56"
            self.min_btn.update()
            self.max_btn.update()
            self.close_btn.update()
    
    def minimize_window(self):
        """最小化窗口"""
        if self.parent_window:
            self.parent_window.showMinimized()
    
    def maximize_restore_window(self):
        """最大化/还原窗口"""
        if self.parent_window:
            if self.parent_window.isMaximized():
                self.parent_window.showNormal()
            else:
                self.parent_window.showMaximized()
            # 强制重新应用样式
            self.update_theme()
    
    def close_window(self):
        """关闭窗口"""
        if self.parent_window:
            self.parent_window.close()
    
    def mousePressEvent(self, event: QMouseEvent):
        """鼠标按下事件 - 记录拖动起始位置"""
        if event.button() == Qt.MouseButton.LeftButton:
            self.drag_position = event.globalPosition().toPoint() - self.parent_window.frameGeometry().topLeft()
            event.accept()
    
    def mouseMoveEvent(self, event: QMouseEvent):
        """鼠标移动事件 - 实现窗口拖动"""
        if event.buttons() == Qt.MouseButton.LeftButton and self.parent_window:
            # 如果窗口是最大化状态，先还原
            if self.parent_window.isMaximized():
                self.parent_window.showNormal()
                # 重新计算拖动位置
                self.drag_position = QPoint(self.parent_window.width() // 2, 20)
            
            self.parent_window.move(event.globalPosition().toPoint() - self.drag_position)
            event.accept()
    
    def mouseDoubleClickEvent(self, event: QMouseEvent):
        """双击标题栏最大化/还原"""
        if event.button() == Qt.MouseButton.LeftButton:
            self.maximize_restore_window()
            event.accept()
    
    def open_website(self):
        """打开网站"""
        try:
            import webbrowser
            logger.info("正在打开网站...")
            webbrowser.open("https://space.bilibili.com/3461562331302242")
        except Exception as e:
            logger.error("打开网址失败: %s", e)

    def eventFilter(self, obj, event):
        """事件过滤器，用于处理图标点击"""
        if obj == self.icon_label:
            if event.type() == QEvent.Type.MouseButtonPress:
                return True
            elif event.type() == QEvent.Type.MouseButtonRelease:
                if isinstance(event, QMouseEvent):
                    if event.button() == Qt.MouseButton.LeftButton:
                        self.open_website()
                        return True
        return super().eventFilter(obj, event)