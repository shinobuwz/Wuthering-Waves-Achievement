"""成就表格组件"""
import logging

from PySide6.QtWidgets import QTableWidget, QTableWidgetItem, QComboBox, QStyledItemDelegate
from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QColor
from core.styles import BaseStyles

logger = logging.getLogger(__name__)


class CustomComboBox(QComboBox):
    """自定义下拉框，用于分类编辑"""
    
    def __init__(self, items=None, parent=None):
        super().__init__(parent)
        if items:
            self.addItems(items)
        # 设置初始状态，避免闪烁
        self.setSizeAdjustPolicy(QComboBox.SizeAdjustPolicy.AdjustToContents)
        
    def showEvent(self, event):
        """重写showEvent，确保平滑显示"""
        super().showEvent(event)
        # 不在这里自动显示下拉框，由外部控制


class ComboBoxDelegate(QStyledItemDelegate):
    """下拉框委托，用于表格中的下拉编辑"""
    
    def __init__(self, items=None, parent=None):
        super().__init__(parent)
        self.items = items or []
        
    def createEditor(self, parent, option, index):
        """创建编辑器"""
        editor = CustomComboBox(self.items, parent)
        
        # 获取当前主题
        try:
            from core.config import config
            theme = config.theme
        except:
            theme = "light"
            
        # 根据主题设置样式，确保与表格整体风格一致
        if theme == "dark":
            editor.setStyleSheet("""
                QComboBox {
                    border: none;
                    border-radius: 3px;
                    padding: 2px;
                    background: #2b2b2b;
                    color: #ffffff;
                }
                QComboBox::drop-down {
                    border: none;
                    width: 20px;
                }
                QComboBox::down-arrow {
                    image: none;
                    border-left: 4px solid transparent;
                    border-right: 4px solid transparent;
                    border-top: 4px solid #cccccc;
                    margin-right: 4px;
                }
                QComboBox QAbstractItemView {
                    border: 1px solid #444444;
                    background: #2b2b2b;
                    color: #ffffff;
                    selection-background-color: #444444;
                }
            """)
        else:
            editor.setStyleSheet("""
                QComboBox {
                    border: none;
                    border-radius: 3px;
                    padding: 2px;
                    background: #ffffff;
                }
                QComboBox::drop-down {
                    border: none;
                    width: 20px;
                }
                QComboBox::down-arrow {
                    image: none;
                    border-left: 4px solid transparent;
                    border-right: 4px solid transparent;
                    border-top: 4px solid #666666;
                    margin-right: 4px;
                }
                QComboBox QAbstractItemView {
                    border: 1px solid #cccccc;
                    background: #ffffff;
                    selection-background-color: #e6f3ff;
                }
            """)
        
        return editor
        
    def setEditorData(self, editor, index):
        """设置编辑器数据"""
        current_text = index.model().data(index, Qt.DisplayRole)
        if current_text:
            index = editor.findText(current_text)
            if index >= 0:
                editor.setCurrentIndex(index)
                
    def setModelData(self, editor, model, index):
        """设置模型数据"""
        model.setData(index, editor.currentText(), Qt.DisplayRole)
        
    def editorEvent(self, event, model, option, index):
        """处理编辑事件"""
        if event.type() == event.Type.MouseButtonDblClick:
            # 双击时立即显示编辑器
            return False  # 让表格处理双击事件
        return super().editorEvent(event, model, option, index)
        
    def updateEditorGeometry(self, editor, option, index):
        """更新编辑器几何形状"""
        editor.setGeometry(option.rect)
        
    def setEditorData(self, editor, index):
        """设置编辑器数据"""
        current_text = index.model().data(index, Qt.DisplayRole)
        if current_text:
            idx = editor.findText(current_text)
            if idx >= 0:
                editor.setCurrentIndex(idx)


class AchievementTable(QTableWidget):
    """成就表格（带状态管理功能）"""
    
    def __init__(self):
        super().__init__()
        self.setup_table()
        self.long_press_timer = QTimer()
        self.long_press_timer.setSingleShot(True)
        self.long_press_timer.timeout.connect(self.on_long_press)
        self.pressed_row = -1
        
        # 加载分类配置
        self.load_category_config()
        
        # 创建委托
        self.first_category_delegate = None
        self.second_category_delegate = None
        self.setup_delegates()
        
    def setup_table(self):
        """设置表格"""
        # 设置列
        headers = ['状态', '名称', '描述', '奖励', '版本', '隐藏', '第一分类', '第二分类']
        self.setColumnCount(len(headers))
        self.setHorizontalHeaderLabels(headers)
        
        # 设置表格属性
        self.setAlternatingRowColors(True)
        self.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.setSelectionMode(QTableWidget.SelectionMode.SingleSelection)
        # 改为单击编辑，但只对分类列有效
        self.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)  # 先禁用所有编辑触发器
        self.horizontalHeader().setStretchLastSection(True)
        
        # 设置垂直表头（序号列）样式
        self.verticalHeader().setVisible(True)
        self.verticalHeader().setDefaultSectionSize(25)
        self.verticalHeader().setMinimumWidth(40)
        
        # 去掉选中框和焦点
        self.setShowGrid(False)
        self.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.setSelectionMode(QTableWidget.SelectionMode.NoSelection)  # 完全禁止选择
# 动态获取主题颜色
        from core.styles import BaseStyles, get_scrollbar_style
        from core.config import config
        table_style = BaseStyles.get_text_input_style(config.theme)
        scrollbar = get_scrollbar_style(config.theme)
        self.setStyleSheet(table_style + scrollbar)
        
        # 设置列宽
        self.setColumnWidth(0, 100)  # 状态
        self.setColumnWidth(1, 200)  # 名称
        self.setColumnWidth(2, 300)  # 描述
        self.setColumnWidth(3, 100)  # 奖励
        self.setColumnWidth(4, 100)  # 版本
        self.setColumnWidth(5, 80)   # 隐藏
        self.setColumnWidth(6, 120)  # 第一分类
        self.setColumnWidth(7, 120)  # 第二分类
        
        # 启用鼠标追踪以支持悬浮提示
        self.setMouseTracking(True)
        
    def load_category_config(self):
        """加载分类配置"""
        try:
            from core.config import config
            self.category_config = config.load_category_config()
            self.first_categories = list(self.category_config.get("first_categories", {}).keys())
            self.second_categories = self.category_config.get("second_categories", {})
        except Exception as e:
            logger.error("加载分类配置失败: %s", e)
            self.category_config = {}
            self.first_categories = []
            self.second_categories = {}
            
    def setup_delegates(self):
        """设置委托"""
        # 第一分类委托
        self.first_category_delegate = ComboBoxDelegate(self.first_categories, self)
        self.setItemDelegateForColumn(6, self.first_category_delegate)  # 第一分类列
        
        # 第二分类委托（初始为空，会在加载数据时更新）
        self.second_category_delegate = ComboBoxDelegate([], self)
        self.setItemDelegateForColumn(7, self.second_category_delegate)  # 第二分类列
        
    def load_data(self, achievements):
        """加载数据"""
        self.setRowCount(len(achievements))
        self.achievements = achievements  # 保存数据引用
        
        for row, achievement in enumerate(achievements):
            # 状态
            status = achievement.get('获取状态', '')
            if not status:
                status = '未完成'
            status_item = QTableWidgetItem(status)
            
            # 设置状态文字颜色
            if status == '已完成':
                status_item.setForeground(QColor(255, 140, 0))  # 橙色
            elif status == '未完成':
                status_item.setForeground(QColor(128, 128, 128))  # 灰色
            elif status == '暂不可获取':
                status_item.setForeground(QColor(255, 0, 0))  # 红色
            elif status == '已占用':
                status_item.setForeground(QColor(255, 69, 0))  # 红橙色
                
            self.setItem(row, 0, status_item)

            # 名称（完整显示，但保留悬浮提示）
            name = achievement.get('名称', '')
            name_item = QTableWidgetItem(name)
            name_item.setToolTip(name)  # 悬浮显示完整名称
            
            # 设置字体加粗
            font = name_item.font()
            font.setBold(True)
            name_item.setFont(font)
            
            if achievement.get('是否隐藏') == '隐藏':
                name_item.setForeground(QColor(255, 165, 0))  # 橙黄色文字
            self.setItem(row, 1, name_item)

            # 描述（完整显示，但保留悬浮提示）
            desc = achievement.get('描述', '')
            desc_item = QTableWidgetItem(desc)
            desc_item.setToolTip(desc)  # 悬浮显示完整描述
            self.setItem(row, 2, desc_item)

            # 奖励
            reward_item = QTableWidgetItem(achievement.get('奖励', ''))
            reward_text = achievement.get('奖励', '')
            if '20' in reward_text:
                reward_item.setForeground(QColor(255, 107, 53))  # 橙色
            elif '10' in reward_text:
                reward_item.setForeground(QColor(78, 205, 196))  # 青色
            elif '5' in reward_text:
                reward_item.setForeground(QColor(69, 183, 209))  # 蓝色
            self.setItem(row, 3, reward_item)

            # 版本
            self.setItem(row, 4, QTableWidgetItem(achievement.get('版本', '')))

            # 隐藏
            hidden_item = QTableWidgetItem("是" if achievement.get('是否隐藏') == '隐藏' else "否")
            if achievement.get('是否隐藏') == '隐藏':
                hidden_item.setForeground(QColor(255, 165, 0))  # 橙黄色文字
            self.setItem(row, 5, hidden_item)

            # 第一分类
            self.setItem(row, 6, QTableWidgetItem(achievement.get('第一分类', '')))

            # 第二分类
            self.setItem(row, 7, QTableWidgetItem(achievement.get('第二分类', '')))

        # 不调整列宽，保持设定的宽度
        
        # 初始化第二分类委托，显示所有第二分类
        all_second_categories = set()
        for achievement in achievements:
            second_cat = achievement.get('第二分类', '')
            if second_cat:
                all_second_categories.add(second_cat)
        
        self.second_category_delegate = ComboBoxDelegate(sorted(list(all_second_categories)), self)
        self.setItemDelegateForColumn(7, self.second_category_delegate)  # 第二分类列
        
    def update_second_category_delegate(self):
        """更新第二分类委托的选项"""
        if not hasattr(self, 'achievements'):
            return
            
        # 获取所有第二分类
        all_second_categories = set()
        for achievement in self.achievements:
            second_cat = achievement.get('第二分类', '')
            if second_cat:
                all_second_categories.add(second_cat)
        
        # 创建新的第二分类委托
        self.second_category_delegate = ComboBoxDelegate(sorted(list(all_second_categories)), self)
        self.setItemDelegateForColumn(7, self.second_category_delegate)  # 第二分类列
    
    
    
    def mouseReleaseEvent(self, event):
        """鼠标释放事件 - 处理长按"""
        if event.button() == Qt.MouseButton.LeftButton:
            # 停止长按定时器
            self.long_press_timer.stop()
            self.pressed_row = -1
        
        super().mouseReleaseEvent(event)
    
    def on_long_press(self):
        """长按事件处理"""
        if self.pressed_row >= 0:
            item = self.item(self.pressed_row, 0)
            if item:
                # 将状态设置为"暂不可获取"
                item.setText("暂不可获取")
                item.setForeground(QColor(255, 0, 0))  # 红色
                
                # 更新数据
                if 0 <= self.pressed_row < len(self.achievements):
                    self.achievements[self.pressed_row]['获取状态'] = '暂不可获取'
                    # 立即保存数据（兼容不同的父组件）
                    parent = self.parent()
                    if hasattr(parent, 'save_local_data'):
                        parent.save_local_data()
                    elif hasattr(parent, 'save_to_json'):
                        parent.save_to_json()
                
                # 强制重绘
                self.viewport().update()
                self.update()
    
    def cellChanged(self, row, column):
        """单元格内容改变时的处理"""
        # 这个方法现在主要用于单元格文本更新
        # 实际的数据保存由_saveEditorData方法处理
        pass
    
    def update_second_category_delegate_for_first_category(self, first_category):
        """根据第一分类更新第二分类委托的选项"""
        if not first_category:
            # 如果第一分类为空，显示所有第二分类
            all_second_categories = set()
            for achievement in self.achievements:
                second_cat = achievement.get('第二分类', '')
                if second_cat:
                    all_second_categories.add(second_cat)
            second_categories = sorted(list(all_second_categories))
        else:
            # 只显示属于该第一分类的第二分类
            second_categories = []
            if first_category in self.second_categories:
                # 按配置顺序排序
                category_order = self.second_categories[first_category]
                ordered_categories = sorted(category_order.keys(), key=lambda x: int(category_order.get(x, 999)))
                second_categories = ordered_categories
            
            # 添加当前数据中存在但配置中没有的第二分类（仅限属于该第一分类的）
            existing_categories = set()
            for achievement in self.achievements:
                if achievement.get('第一分类', '') == first_category:
                    second_cat = achievement.get('第二分类', '')
                    if second_cat and second_cat not in second_categories:
                        existing_categories.add(second_cat)
            
            second_categories.extend(sorted(existing_categories))
        
        # 创建新的第二分类委托
        self.second_category_delegate = ComboBoxDelegate(second_categories, self)
        self.setItemDelegateForColumn(7, self.second_category_delegate)  # 第二分类列
        
        logger.debug("更新第二分类委托，第一分类: %s, 可选第二分类: %s", first_category, second_categories)
    
    def is_valid_second_category(self, first_category, second_category):
        """验证第二分类是否属于第一分类"""
        if not first_category or not second_category:
            return True
            
        # 检查配置中是否存在该组合
        if first_category in self.second_categories:
            return second_category in self.second_categories[first_category]
        
        # 如果配置中没有，检查现有数据中是否存在该组合
        for achievement in self.achievements:
            if (achievement.get('第一分类', '') == first_category and 
                achievement.get('第二分类', '') == second_category):
                return True
        
        return False
    
    def save_data(self):
        """保存数据"""
        try:
            # 保存到父组件
            parent = self.parent()
            if hasattr(parent, 'save_local_data'):
                parent.save_local_data()
            elif hasattr(parent, 'save_to_json'):
                parent.save_to_json()
        except Exception as e:
            logger.error("保存数据失败: %s", e)

    def mousePressEvent(self, event):
        """重写鼠标点击事件，处理分类列的单击编辑"""
        if event.button() == Qt.MouseButton.LeftButton:
            item = self.itemAt(event.pos())
            if item and item.column() in [6, 7]:  # 第一分类和第二分类列
                row = item.row()
                col = item.column()
                
                # 关闭所有其他分类列的编辑器
                self._closeAllCategoryEditors()
                
                # 对于第二分类，在打开编辑器前先根据第一分类更新选项
                if col == 7:  # 第二分类列
                    first_category_item = self.item(row, 6)
                    if first_category_item:
                        current_first_category = first_category_item.text()
                        self.update_second_category_delegate_for_first_category(current_first_category)
                
                # 检查是否已经有编辑器
                editor = self.cellWidget(row, col)
                if editor and isinstance(editor, CustomComboBox):
                    # 如果已有编辑器，显示下拉框
                    from PySide6.QtCore import QTimer
                    QTimer.singleShot(10, editor.showPopup)
                else:
                    # 如果没有编辑器，直接打开编辑器
                    self.openPersistentEditor(self.item(row, col))
                    # 延迟显示下拉框，增加延迟时间避免闪烁
                    from PySide6.QtCore import QTimer
                    QTimer.singleShot(100, lambda: self._showComboBoxPopup(row, col))
                return
            else:
                # 点击非分类列时，关闭所有分类编辑器
                self._closeAllCategoryEditors()
        
        # 其他列或状态列的处理
        if event.button() == Qt.MouseButton.LeftButton:
            item = self.itemAt(event.pos())
            if item and item.column() == 0:  # 状态列
                row = item.row()
                current_status = item.text()
                
                # 立即切换状态
                if current_status == '未完成':
                    new_status = '已完成'
                elif current_status == '已完成':
                    new_status = '未完成'
                elif current_status == '已占用':
                        # 已占用状态不能直接切换，需要解锁
                    new_status = '未完成'
                else:
                    new_status = '未完成'
                
                # 立即更新表格和颜色
                item.setText(new_status)
                if new_status == '已完成':
                    item.setForeground(QColor(255, 140, 0))  # 橙色
                elif new_status == '未完成':
                    item.setForeground(QColor(128, 128, 128))  # 灰色
                elif new_status == '已占用':
                    item.setForeground(QColor(255, 69, 0))  # 红橙色
                
                # 强制重绘该单元格和viewport
                self.viewport().update()
                self.update()
                
                # 立即更新数据
                if 0 <= row < len(self.achievements):
                    achievement = self.achievements[row]
                    achievement['获取状态'] = new_status
                    
                    # 成就组逻辑处理
                    if new_status == '已完成':
                        self._handle_achievement_group_completion(row, achievement)
                    elif new_status == '未完成' and (current_status == '已占用' or current_status == '已完成'):
                        # 从已占用或已完成状态切换到未完成，解锁同组其他成就
                        self._unlock_group_achievements(achievement)
                    
                    # 立即保存数据（兼容不同的父组件）
                    parent = self.parent()
                    if hasattr(parent, 'save_local_data'):
                        parent.save_local_data()
                    elif hasattr(parent, 'save_to_json'):
                        parent.save_to_json()
                    
                    # 即时刷新统计信息
                    self._refresh_statistics()
                
                self.pressed_row = row
                # 启动长按定时器（1秒）
                self.long_press_timer.start(1000)
        
        super().mousePressEvent(event)
        
    def edit(self, index, trigger, event):
        """重写编辑方法，确保下拉框正确显示"""
        if index.column() in [6, 7]:  # 只处理第一分类和第二分类列
            return super().edit(index, trigger, event)
        return super().edit(index, trigger, event)
        
    def _showComboBoxPopup(self, row, col):
        """显示ComboBox下拉框"""
        # 尝试多种方式获取编辑器
        editor = self.cellWidget(row, col)
        if not editor:
            # 如果cellWidget没有获取到，尝试从索引获取
            index = self.model().index(row, col)
            editor = self.indexWidget(index)
        
        if editor and isinstance(editor, CustomComboBox):
            editor.showPopup()
        else:
            # 如果还是没有编辑器，尝试重新创建
            logger.debug("无法获取编辑器，尝试重新创建 row=%s, col=%s", row, col)
            # 关闭持久编辑器
            self.closePersistentEditor(self.item(row, col))
            # 重新打开
            self.openPersistentEditor(self.item(row, col))
            # 再次尝试获取
            from PySide6.QtCore import QTimer
            QTimer.singleShot(50, lambda: self._retryShowPopup(row, col))
    
    def _saveEditorData(self, row, col):
        """保存编辑器数据"""
        if not hasattr(self, 'achievements') or row >= len(self.achievements):
            return
            
        # 获取编辑器
        editor = self.cellWidget(row, col)
        if not editor:
            index = self.model().index(row, col)
            editor = self.indexWidget(index)
        
        if editor and isinstance(editor, CustomComboBox):
            new_value = editor.currentText()
            achievement = self.achievements[row]
            
            # 更新数据
            if col == 6:  # 第一分类
                old_value = achievement.get('第一分类', '')
                achievement['第一分类'] = new_value
                logger.info("第一分类已更新: %s -> %s", old_value, new_value)
                
                # 只有当第一分类真正改变时，才清空第二分类
                if old_value != new_value and old_value != '':
                    achievement['第二分类'] = ''
                    second_item = self.item(row, 7)
                    if second_item:
                        second_item.setText('')
                    logger.info("第一分类已改变，清空第二分类")
                
                # 更新第二分类委托选项
                self.update_second_category_delegate_for_first_category(new_value)
                
                # 显示通知
                if old_value != new_value:
                    achievement_name = achievement.get('名称', '')
                    if achievement_name:
                        from .manage_tab import show_notification
                        show_notification(self.parent(), f"{achievement_name}：第一分类 {old_value} -> {new_value}，已保存")
                    
            elif col == 7:  # 第二分类
                old_value = achievement.get('第二分类', '')
                achievement['第二分类'] = new_value
                logger.info("第二分类已更新: %s -> %s", old_value, new_value)
                
                # 显示通知
                if old_value != new_value:
                    achievement_name = achievement.get('名称', '')
                    if achievement_name:
                        from .manage_tab import show_notification
                        show_notification(self.parent(), f"{achievement_name}：第二分类 {old_value} -> {new_value}，已保存")
            
            # 保存数据
            self.save_data()
            
            # 立即更新表格显示
            should_clear_second = (col == 6 and old_value != new_value and old_value != '')
            self._updateTableDisplay(row, col, new_value, should_clear_second)
            
            # 即时刷新统计信息
            self._refresh_statistics()
    
    def _refresh_statistics(self):
        """即时刷新统计信息"""
        try:
            # 获取父级ManageTab对象
            parent = self.parent()
            while parent and not hasattr(parent, 'update_statistics'):
                parent = parent.parent()
            
            if parent and hasattr(parent, 'update_statistics'):
                # 调用父级的update_statistics方法
                parent.update_statistics()
                logger.info("统计信息已即时刷新")
        except Exception as e:
            logger.error("刷新统计信息失败: %s", e)
    
    def _updateTableDisplay(self, row, col, new_value, should_clear_second=False):
        """更新表格显示"""
        # 更新对应的表格项
        item = self.item(row, col)
        if item:
            item.setText(new_value)
        
        # 如果需要清空第二分类的显示
        if should_clear_second:
            second_item = self.item(row, 7)
            if second_item:
                second_item.setText('')
        
        # 强制重绘整个表格，确保显示更新
        self.viewport().update()
        self.update()
        
        # 如果是第一分类改变，还需要更新第二分类委托
        if col == 6:  # 第一分类列
            self.update_second_category_delegate()
    
    def _retryShowPopup(self, row, col):
        """重试显示下拉框"""
        editor = self.cellWidget(row, col)
        if not editor:
            index = self.model().index(row, col)
            editor = self.indexWidget(index)
        
        if editor and isinstance(editor, CustomComboBox):
            editor.showPopup()
            logger.debug("重试成功，下拉框已显示")
        else:
            logger.debug("重试失败，仍无法获取编辑器")
    
    def _closeAllCategoryEditors(self):
        """关闭所有分类列的编辑器"""
        if not hasattr(self, 'achievements'):
            return
            
        # 保存当前的滚动位置
        horizontal_scroll = self.horizontalScrollBar().value()
        vertical_scroll = self.verticalScrollBar().value()
        
        # 暂时禁用滚动条更新
        self.horizontalScrollBar().blockSignals(True)
        self.verticalScrollBar().blockSignals(True)
        
        # 记录当前打开的编辑器位置
        open_editors = []
        for row in range(len(self.achievements)):
            for col in [6, 7]:  # 第一分类和第二分类列
                if self.item(row, col) and self.isPersistentEditorOpen(self.item(row, col)):
                    open_editors.append((row, col))
        
        # 关闭所有打开的编辑器
        for row, col in open_editors:
            try:
                # 在关闭编辑器前保存数据
                self._saveEditorData(row, col)
                self.closePersistentEditor(self.item(row, col))
            except:
                # 如果关闭失败，忽略错误
                pass
        
        # 恢复滚动位置
        self.horizontalScrollBar().setValue(horizontal_scroll)
        self.verticalScrollBar().setValue(vertical_scroll)
        
        # 重新启用滚动条更新
        self.horizontalScrollBar().blockSignals(False)
        self.verticalScrollBar().blockSignals(False)
        
    def focusInEvent(self, event):
        """焦点进入事件"""
        super().focusInEvent(event)
        # 不显示焦点框，保持美观
        
    def focusOutEvent(self, event):
        """焦点离开事件"""
        super().focusOutEvent(event)
        # 确保编辑完成后立即保存
        self.clearSelection()
        
    def _handle_achievement_group_completion(self, completed_row, completed_achievement):
        """处理成就组完成逻辑"""
        group_id = completed_achievement.get('成就组ID')
        if not group_id:
            return
        
        mutex_achievements = completed_achievement.get('互斥成就', [])
        if not mutex_achievements:
            return
        
        completed_code = completed_achievement.get('编号', '')
        
        # 锁定同组其他成就
        for i, achievement in enumerate(self.achievements):
            if i == completed_row:
                continue  # 跳过已完成的成就
            
            achievement_code = achievement.get('编号', '')
            if achievement_code in mutex_achievements:
                # 设置为已占用状态
                achievement['获取状态'] = '已占用'
                
                # 更新表格显示
                status_item = self.item(i, 0)
                if status_item:
                    status_item.setText('已占用')
                    status_item.setForeground(QColor(255, 69, 0))  # 红橙色
                
                logger.info("成就组 %s：已占用成就 %s", group_id, achievement.get('名称', ''))
        
                # 强制重绘
                self.viewport().update()
                self.update()
                # 即时刷新统计信息
                self._refresh_statistics()
        
    def _unlock_group_achievements(self, unlocked_achievement):
        """解锁同组其他成就 - 将同组所有成就都设为未完成"""
        group_id = unlocked_achievement.get('成就组ID')
        if not group_id:
            return
        
        unlocked_code = unlocked_achievement.get('编号', '')
        
        # 将同组所有成就都设为未完成状态
        for i, achievement in enumerate(self.achievements):
            if achievement.get('成就组ID') == group_id:
                achievement_code = achievement.get('编号', '')
                if achievement_code != unlocked_code:  # 跳过当前成就
                    # 不管当前状态是什么，都设置为未完成
                    old_status = achievement.get('获取状态', '')
                    achievement['获取状态'] = '未完成'
                    
                    # 更新表格显示
                    status_item = self.item(i, 0)
                    if status_item:
                        status_item.setText('未完成')
                        status_item.setForeground(QColor(128, 128, 128))  # 灰色
                    
                    logger.info("成就组 %s：成就 %s 从 %s 变为未完成", group_id, achievement.get('名称', ''), old_status)
        
        # 强制重绘
        self.viewport().update()
        self.update()
        
        # 即时刷新统计信息
        self._refresh_statistics()
    
    def apply_theme(self, theme):
        """应用主题"""
        # 更新表格样式
        table_style = BaseStyles.get_text_input_style(theme)
        self.setStyleSheet(table_style)
        
        # 更新委托样式
        if hasattr(self, 'first_category_delegate') and self.first_category_delegate:
            # 重新创建委托以应用新主题
            self.setup_delegates()