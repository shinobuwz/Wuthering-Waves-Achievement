"""
成就 OCR 识别与匹配模块
通过模板匹配定位成就图标，基于相对偏移裁剪名称和状态区域进行 OCR 识别
"""
import os
import re
import time
import logging
import difflib
import ctypes

import cv2
import numpy as np
import pyautogui

pyautogui.PAUSE = 0.1
pyautogui.FAILSAFE = False

logger = logging.getLogger(__name__)

# ============================================
# OCR 扫描参数（集中管理所有可调常量）
# ============================================

# --- 模板匹配 ---
MATCH_THRESHOLD = 0.75          # 模板匹配置信度阈值
NMS_DISTANCE = 40               # 非极大值抑制合并距离（像素）

# --- 名称区域（相对于图标左上角） ---
NAME_DX = 122
NAME_DY = -39
NAME_W = 503
NAME_H = 40

# --- 状态区域（相对于图标左上角） ---
STATUS_DX = 878
STATUS_DY = 15
STATUS_W = 163
STATUS_H = 47

# --- 滚动参数 ---
SCROLL_LENGTH = -160            # 每次 pyautogui.scroll 的值（负数=列表向下）
SCROLL_TIMES = 15               # 连续发送 scroll 的次数（利用惯性叠加）- 成就列表
SCROLL_TIMES_TAB = 16           # 连续发送 scroll 的次数 - 二级 Tab 列表
SCROLL_DELAY = 0.8              # 滚动后等待时间（秒）

# --- 界面布局参数（百分比，基于截图宽高，由模板匹配确定） ---
# 一级 Tab 名称区域（用于 OCR 识别当前选中的一级分类名称）
PRIMARY_TAB_X1_PCT = 0.053
PRIMARY_TAB_Y1_PCT = 0.047
PRIMARY_TAB_X2_PCT = 0.114
PRIMARY_TAB_Y2_PCT = 0.083

# 一级 Tab 图标点击坐标（百分比，由模板匹配确定，x 固定 0.0417）
# 顺序：索拉漫行、铿锵刃鸣、长路留迹、诸音声轨
PRIMARY_TAB_ICON_X_PCT = 0.0417
PRIMARY_TAB_ICON_Y_PCTS = [0.1778, 0.2981, 0.4343, 0.5537]

# 二级 Tab 列表区域
SECONDARY_TAB_X1_PCT = 0.1005
SECONDARY_TAB_Y1_PCT = 0.1796
SECONDARY_TAB_X2_PCT = 0.3479
SECONDARY_TAB_Y2_PCT = 1.0000

# --- 图标模板目录 ---
_TEMPLATE_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "resources", "ocr_templates"
)

# ============================================
# 图标模板加载
# ============================================

_icon_templates = None


def _read_image(path):
    """读取图片，兼容中文路径"""
    with open(path, 'rb') as f:
        data = np.frombuffer(f.read(), dtype=np.uint8)
    return cv2.imdecode(data, cv2.IMREAD_COLOR)


def _load_icon_templates():
    """延迟加载图标模板（首次调用时加载）"""
    global _icon_templates
    if _icon_templates is not None:
        return _icon_templates

    templates = []
    template_files = [
        ("1star", "icon_1star.png"),
        ("2star", "icon_2star.png"),
        ("3star", "icon_3star.png"),
    ]
    for label, filename in template_files:
        path = os.path.join(_TEMPLATE_DIR, filename)
        if os.path.exists(path):
            img = _read_image(path)
            if img is not None:
                templates.append((label, img))

    if not templates:
        raise RuntimeError(
            f"未找到成就图标模板文件，请检查目录: {_TEMPLATE_DIR}"
        )

    logger.info("已加载 %d 个图标模板: %s", len(templates), [t[0] for t in templates])
    _icon_templates = templates
    return _icon_templates


# ============================================
# 模板匹配定位
# ============================================

def find_achievement_icons(screenshot, threshold=MATCH_THRESHOLD):
    """
    在截图中通过模板匹配找到所有成就图标位置。

    Args:
        screenshot: 1920x1080 BGR 截图
        threshold: 匹配阈值
    Returns:
        list[tuple]: [(x, y, icon_label, confidence), ...] 按 y 坐标排序
    """
    templates = _load_icon_templates()

    all_matches = []
    for label, icon in templates:
        result = cv2.matchTemplate(screenshot, icon, cv2.TM_CCOEFF_NORMED)
        locations = np.where(result >= threshold)
        for pt_y, pt_x in zip(*locations):
            all_matches.append((int(pt_x), int(pt_y), label, float(result[pt_y, pt_x])))

    # 非极大值抑制：按置信度降序，合并距离过近的匹配
    all_matches.sort(key=lambda m: -m[3])
    filtered = []
    for match in all_matches:
        x, y, label, conf = match
        too_close = any(
            abs(x - fx) < NMS_DISTANCE and abs(y - fy) < NMS_DISTANCE
            for fx, fy, _, _ in filtered
        )
        if not too_close:
            filtered.append(match)

    # 按 y 坐标排序（从上到下）
    filtered.sort(key=lambda m: m[1])
    logger.debug("模板匹配找到 %d 个图标 (阈值=%.2f)", len(filtered), threshold)
    for x, y, label, conf in filtered:
        logger.debug("  图标 %s at (%d,%d) conf=%.3f", label, x, y, conf)
    return filtered


def crop_name_region(screenshot, icon_x, icon_y):
    """
    基于图标位置裁剪成就名称区域。

    Args:
        screenshot: 完整截图
        icon_x, icon_y: 图标左上角坐标
    Returns:
        numpy.ndarray: 名称区域图像，裁剪失败返回 None
    """
    h, w = screenshot.shape[:2]
    x1 = max(0, icon_x + NAME_DX)
    y1 = max(0, icon_y + NAME_DY)
    x2 = min(w, x1 + NAME_W)
    y2 = min(h, y1 + NAME_H)
    if x2 - x1 < 20 or y2 - y1 < 10:
        return None
    return screenshot[y1:y2, x1:x2].copy()


def crop_status_region(screenshot, icon_x, icon_y):
    """
    基于图标位置裁剪状态区域。

    Args:
        screenshot: 完整截图
        icon_x, icon_y: 图标左上角坐标
    Returns:
        numpy.ndarray: 状态区域图像，裁剪失败返回 None
    """
    h, w = screenshot.shape[:2]
    x1 = max(0, icon_x + STATUS_DX)
    y1 = max(0, icon_y + STATUS_DY)
    x2 = min(w, x1 + STATUS_W)
    y2 = min(h, y1 + STATUS_H)
    if x2 - x1 < 20 or y2 - y1 < 10:
        return None
    return screenshot[y1:y2, x1:x2].copy()


# ============================================
# 图像预处理与 OCR
# ============================================

def preprocess_image(image):
    """
    对图像进行预处理以提高 OCR 识别率。

    Args:
        image: BGR 图像
    Returns:
        numpy.ndarray: 预处理后的 BGR 图像
    """
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
    enhanced = clahe.apply(gray)
    return cv2.cvtColor(enhanced, cv2.COLOR_GRAY2BGR)


def recognize_text(image, ocr_model):
    """
    OCR 识别图像中的文字。

    Args:
        image: BGR 图像
        ocr_model: ONNXPaddleOcr 实例
    Returns:
        str: 识别结果，失败返回空字符串
    """
    processed = preprocess_image(image)
    try:
        results = ocr_model.ocr(processed)
        if results:
            text = "".join(results).strip()
            logger.debug("OCR 识别结果: %s", text)
            return text
    except Exception as e:
        logger.warning("OCR 识别异常: %s", e)
    return ""


def parse_status(text):
    """
    解析状态文字。

    Args:
        text: OCR 识别出的状态文字
    Returns:
        str: "已完成" | "未完成" | "未知"
    """
    if not text:
        return "未知"
    if re.search(r'\d{4}/\d{2}/\d{2}', text):
        return "已完成"
    if "进行中" in text or "进行" in text:
        return "未完成"
    return "未知"


# ============================================
# 模糊匹配
# ============================================

# 各类中点/间隔号变体，统一替换为标准中文间隔号 ·（U+00B7）
_MIDDLE_DOT_VARIANTS = str.maketrans('・•∙･', '····')


def _normalize_name(text):
    """归一化名称：统一中点变体，去除首尾空格"""
    return text.translate(_MIDDLE_DOT_VARIANTS).strip()


def match_achievement(ocr_name, achievements_db):
    """
    将 OCR 识别的名称模糊匹配到成就数据库。

    Args:
        ocr_name: OCR 识别出的成就名称
        achievements_db: list[dict], base_achievements.json 数据
    Returns:
        tuple: (编号, 匹配到的名称, 置信度) 或 (None, None, 0)
    """
    if not ocr_name:
        return None, None, 0

    ocr_norm = _normalize_name(ocr_name)
    best_match = None
    best_dist = float('inf')
    best_name = None

    for achievement in achievements_db:
        name = achievement.get("名称", "")
        name_norm = _normalize_name(name)
        if ocr_norm == name_norm:
            return achievement.get("编号"), name, 1.0
        dist = _edit_distance(ocr_norm, name_norm)
        if dist < best_dist:
            best_dist = dist
            best_match = achievement.get("编号")
            best_name = name

    # 允许的最大编辑距离：名称长度的 40%
    max_dist = max(len(ocr_norm), len(_normalize_name(best_name or ""))) * 0.4
    if best_dist <= max_dist:
        confidence = 1 - best_dist / max(len(ocr_norm), len(_normalize_name(best_name)), 1)
        return best_match, best_name, confidence
    return None, None, 0


def _edit_distance(s1, s2):
    """计算两个字符串的编辑距离（Levenshtein distance）"""
    if len(s1) < len(s2):
        return _edit_distance(s2, s1)
    if not s2:
        return len(s1)
    prev = list(range(len(s2) + 1))
    for i, c1 in enumerate(s1):
        curr = [i + 1]
        for j, c2 in enumerate(s2):
            curr.append(min(prev[j + 1] + 1, curr[j] + 1, prev[j] + (c1 != c2)))
        prev = curr
    return prev[-1]


# ============================================
# 单页扫描
# ============================================

def scan_single_page(screenshot, ocr_model, achievements_db):
    """
    扫描当前页面的所有可见成就条目。
    通过模板匹配定位图标，基于偏移裁剪名称和状态区域进行 OCR。

    Args:
        screenshot: 1920x1080 BGR 截图
        ocr_model: ONNXPaddleOcr 实例
        achievements_db: 成就数据库列表
    Returns:
        list[dict]: 每条成就的识别结果
    """
    icons = find_achievement_icons(screenshot)

    results = []
    for icon_x, icon_y, icon_label, icon_conf in icons:
        # 裁剪并识别名称
        name_img = crop_name_region(screenshot, icon_x, icon_y)
        ocr_name = recognize_text(name_img, ocr_model) if name_img is not None else ""

        # 裁剪并识别状态
        status_img = crop_status_region(screenshot, icon_x, icon_y)
        status_text = recognize_text(status_img, ocr_model) if status_img is not None else ""
        status = parse_status(status_text)

        # 模糊匹配
        achievement_id, matched_name, confidence = match_achievement(
            ocr_name, achievements_db
        )

        if matched_name:
            logger.info("成就识别: '%s' -> '%s' (%.0f%%) 状态=%s",
                        ocr_name, matched_name, confidence * 100, status)
        elif ocr_name:
            logger.warning("成就未匹配: '%s' 状态=%s", ocr_name, status)

        results.append({
            "编号": achievement_id,
            "ocr_name": ocr_name,
            "matched_name": matched_name,
            "状态": status,
            "置信度": confidence,
        })

    logger.info("单页扫描完成: 找到 %d 个图标, 匹配 %d 条成就",
                len(icons), sum(1 for r in results if r["编号"]))
    return results


# ============================================
# 自动滚动扫描
# ============================================

def simulate_scroll(hwnd):
    """
    使用 pyautogui.scroll 滚动成就列表。
    先将鼠标移到屏幕中心并点击激活，再连续发送滚轮事件。

    Args:
        hwnd: 游戏窗口句柄
    """
    user32 = ctypes.windll.user32

    # 确保游戏窗口在前台
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)

    # 移动到屏幕中心并点击激活
    sx, sy = pyautogui.size()
    pyautogui.moveTo(sx / 2, sy / 2, duration=0.1)
    pyautogui.click()
    time.sleep(0.3)

    # 连续发送滚轮事件
    for _ in range(SCROLL_TIMES):
        pyautogui.scroll(SCROLL_LENGTH)

    logger.debug("滚轮滚动完成: scroll(%d) x %d", SCROLL_LENGTH, SCROLL_TIMES)


def scan_with_scroll(hwnd, ocr_model, achievements_db, callback=None, stop_flag=None):
    """
    循环执行截图→模板匹配→OCR→滚动，直到扫描完所有成就。

    Args:
        hwnd: 游戏窗口句柄
        ocr_model: ONNXPaddleOcr 实例
        achievements_db: 成就数据库列表
        callback: 可选回调函数 callback(results_so_far, round_num)
        stop_flag: 可选，callable 返回 True 时停止扫描
    Returns:
        list[dict]: 去重后的所有成就识别结果
    """
    from core.game_capture import capture_window

    all_results = {}  # 编号 -> result dict，用于去重
    unmatched_results = []
    prev_names = set()
    round_num = 0

    while True:
        if stop_flag and stop_flag():
            break

        round_num += 1
        logger.info("=== 第 %d 轮扫描 ===", round_num)
        screenshot = capture_window(hwnd)
        page_results = scan_single_page(screenshot, ocr_model, achievements_db)

        current_names = set()
        for result in page_results:
            if result["编号"]:
                current_names.add(result["matched_name"])
                if result["编号"] not in all_results:
                    all_results[result["编号"]] = result
            elif result["ocr_name"]:
                unmatched_results.append(result)

        if callback:
            merged = list(all_results.values()) + unmatched_results
            callback(merged, round_num)

        # 检测是否到底
        if current_names and current_names.issubset(prev_names):
            logger.info("检测到底部（本页成就与上页完全重复），停止滚动")
            break

        if not current_names and round_num > 1:
            logger.info("本页未识别到成就，停止滚动")
            break

        prev_names.update(current_names)

        logger.debug("滚动 scroll(%d)x%d, 等待 %.1fs", SCROLL_LENGTH, SCROLL_TIMES, SCROLL_DELAY)
        simulate_scroll(hwnd)
        time.sleep(SCROLL_DELAY)

    total = list(all_results.values()) + unmatched_results
    logger.info("扫描完成: 共 %d 轮, 匹配 %d 条, 未匹配 %d 条",
                round_num, len(all_results), len(unmatched_results))
    return total


# ============================================
# 二级 Tab 识别与切换
# ============================================

def recognize_primary_tab(screenshot, ocr_model):
    """
    识别左上角一级 Tab 名称。

    Args:
        screenshot: 完整截图
        ocr_model: ONNXPaddleOcr 实例
    Returns:
        str: 识别到的一级 Tab 名称
    """
    h, w = screenshot.shape[:2]
    x1 = int(w * PRIMARY_TAB_X1_PCT)
    y1 = int(h * PRIMARY_TAB_Y1_PCT)
    x2 = int(w * PRIMARY_TAB_X2_PCT)
    y2 = int(h * PRIMARY_TAB_Y2_PCT)
    region = screenshot[y1:y2, x1:x2].copy()
    text = recognize_text(region, ocr_model)
    logger.debug("一级Tab OCR: '%s'", text)
    return text.strip()


def recognize_secondary_tabs(screenshot, ocr_model, known_tabs):
    """
    识别左侧面板中当前可见的所有二级 Tab。
    使用 OCR detect box 的 y 坐标确定每个 Tab 的位置。

    Args:
        screenshot: 完整截图
        ocr_model: ONNXPaddleOcr 实例
        known_tabs: 已知的二级 Tab 名称列表（顺序一致，来自数据库）
    Returns:
        list[(已知名称, center_y_pct, ocr原文)] 按 y 坐标排序
    """
    h, w = screenshot.shape[:2]
    sx1 = int(w * SECONDARY_TAB_X1_PCT)
    sy1 = int(h * SECONDARY_TAB_Y1_PCT)
    sx2 = int(w * SECONDARY_TAB_X2_PCT)
    sy2 = int(h * SECONDARY_TAB_Y2_PCT)

    region = screenshot[sy1:sy2, sx1:sx2].copy()
    processed = preprocess_image(region)

    dt_boxes, rec_res = ocr_model(processed, cls=False)
    logger.debug("二级Tab OCR 检测到 %d 个文本框", len(rec_res))

    # 提取每个检测框的 y 中心（转换为截图坐标百分比）
    raw_tabs = []
    for box, (text, conf) in zip(dt_boxes, rec_res):
        text = text.strip()
        if not text or conf < 0.5:
            continue
        y_center_in_region = (min(p[1] for p in box) + max(p[1] for p in box)) / 2
        center_y_pct = (sy1 + y_center_in_region) / h
        raw_tabs.append((text, center_y_pct, conf))
        logger.debug("  检测框: y=%.4f text='%s' conf=%.3f", center_y_pct, text, conf)

    raw_tabs.sort(key=lambda x: x[1])

    # 编辑距离匹配到已知 Tab 列表
    matched = []
    used_known = set()
    for text, cy_pct, conf in raw_tabs:
        best_ki, best_dist = -1, float('inf')
        for ki, known_name in enumerate(known_tabs):
            if ki in used_known:
                continue
            dist = _edit_distance(text, known_name)
            if dist < best_dist:
                best_dist = dist
                best_ki = ki
        if best_ki >= 0:
            threshold = max(len(text), len(known_tabs[best_ki])) * 0.4
            if best_dist <= threshold:
                matched.append((best_ki, cy_pct, text))
                used_known.add(best_ki)
                logger.debug("  匹配: known[%d]='%s' ← ocr='%s'",
                             best_ki, known_tabs[best_ki], text)

    if not matched:
        logger.warning("二级Tab无法匹配已知列表，返回原始OCR结果")
        return [(t, cy, t) for t, cy, _ in raw_tabs]

    matched.sort(key=lambda x: x[1])
    return [(known_tabs[ki], cy_pct, ocr_text) for ki, cy_pct, ocr_text in matched]


def click_secondary_tab(hwnd, center_y_pct):
    """
    点击指定百分比 y 坐标的二级 Tab。

    Args:
        hwnd: 游戏窗口句柄
        center_y_pct: Tab 中心 y 坐标（占截图高度的百分比）
    """
    from core.game_capture import get_window_rect
    user32 = ctypes.windll.user32
    wx, wy, ww, wh = get_window_rect(hwnd)
    click_x = wx + int(ww * (SECONDARY_TAB_X1_PCT + SECONDARY_TAB_X2_PCT) / 2)
    click_y = wy + int(wh * center_y_pct)

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)
    pyautogui.moveTo(click_x, click_y, duration=0.1)
    time.sleep(0.1)
    pyautogui.click()
    time.sleep(0.5)
    logger.debug("点击二级Tab: (%d,%d) y_pct=%.4f", click_x, click_y, center_y_pct)


def scroll_secondary_tabs(hwnd):
    """
    在二级 Tab 列表区域向下滚动一屏。

    Args:
        hwnd: 游戏窗口句柄
    """
    from core.game_capture import get_window_rect
    user32 = ctypes.windll.user32
    wx, wy, ww, wh = get_window_rect(hwnd)
    scroll_x = wx + int(ww * (SECONDARY_TAB_X1_PCT + SECONDARY_TAB_X2_PCT) / 2)
    scroll_y = wy + int(wh * (SECONDARY_TAB_Y1_PCT + SECONDARY_TAB_Y2_PCT) / 2)

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)
    pyautogui.moveTo(scroll_x, scroll_y, duration=0.1)
    pyautogui.click()
    time.sleep(0.3)
    for _ in range(SCROLL_TIMES_TAB):
        pyautogui.scroll(SCROLL_LENGTH)
    time.sleep(SCROLL_DELAY)
    logger.debug("二级Tab滚动完成: scroll(%d) x %d", SCROLL_LENGTH, SCROLL_TIMES_TAB)


def click_primary_tab(hwnd, tab_index):
    """
    点击左侧一级 Tab 图标。

    Args:
        hwnd: 游戏窗口句柄
        tab_index: 0=索拉漫行, 1=铿锵刃鸣, 2=长路留迹, 3=诸音声轨
    """
    from core.game_capture import get_window_rect
    user32 = ctypes.windll.user32
    wx, wy, ww, wh = get_window_rect(hwnd)
    click_x = wx + int(ww * PRIMARY_TAB_ICON_X_PCT)
    click_y = wy + int(wh * PRIMARY_TAB_ICON_Y_PCTS[tab_index])

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)
    pyautogui.moveTo(click_x, click_y, duration=0.1)
    time.sleep(0.1)
    pyautogui.click()
    time.sleep(0.8)
    logger.debug("点击一级Tab[%d]: (%d,%d)", tab_index, click_x, click_y)


def _switch_to_secondary_tab(hwnd, ocr_model, known_tabs, target_name):
    """
    切换到指定二级 Tab。在当前可见列表中查找，找不到则向下滚动一次再试。
    不回顶——假设按顺序切换，目标 Tab 要么在当前视野，要么在下方。

    Args:
        hwnd: 游戏窗口句柄
        ocr_model: ONNXPaddleOcr 实例
        known_tabs: 该一级分类的全部二级 Tab 名称列表
        target_name: 目标二级 Tab 名称
    Returns:
        bool: 是否成功找到并点击
    """
    from core.game_capture import capture_window

    for attempt in range(4):  # 最多向下滚3次
        screenshot = capture_window(hwnd)
        visible = recognize_secondary_tabs(screenshot, ocr_model, known_tabs)
        for name, cy_pct, _ in visible:
            if name == target_name:
                click_secondary_tab(hwnd, cy_pct)
                return True
        logger.info("  未找到二级Tab '%s'（第%d次），向下滚动", target_name, attempt + 1)
        scroll_secondary_tabs(hwnd)

    logger.warning("  无法找到二级Tab '%s'", target_name)
    return False


# ============================================
# 全量自动扫描（遍历所有一级/二级Tab）
# ============================================

# 一级 Tab 顺序（与 PRIMARY_TAB_ICON_Y_PCTS 对应）
PRIMARY_TAB_NAMES = ["索拉漫行", "铿锵刃鸣", "长路留迹", "诸音声轨"]


def scan_all_tabs(hwnd, ocr_model, achievements_db, category_map,
                  callback=None, stop_flag=None):
    """
    自动遍历所有一级 Tab → 二级 Tab → 扫描成就列表，汇总结果。

    Args:
        hwnd: 游戏窗口句柄
        ocr_model: ONNXPaddleOcr 实例
        achievements_db: base_achievements.json 数据列表
        category_map: {一级分类: [二级分类, ...]} 字典
        callback: 可选，callback(progress_dict, primary, secondary) 每完成一个二级Tab时调用
        stop_flag: 可选，callable 返回 True 时中止
    Returns:
        dict: {编号: {"获取状态": "已完成"|"未完成"}}，可直接写入 user_progress
    """
    from core.game_capture import capture_window

    progress = {}  # 编号 -> {"获取状态": ...}

    for pi_idx, primary_name in enumerate(PRIMARY_TAB_NAMES):
        if stop_flag and stop_flag():
            break

        secondary_list = category_map.get(primary_name, [])
        if not secondary_list:
            logger.info("跳过一级Tab '%s'（无二级分类数据）", primary_name)
            continue

        logger.info("=== 切换一级Tab [%d] '%s' (%d 个二级Tab) ===",
                    pi_idx, primary_name, len(secondary_list))
        click_primary_tab(hwnd, pi_idx)

        for sec_name in secondary_list:
            if stop_flag and stop_flag():
                break

            logger.info("  -> 二级Tab '%s'", sec_name)

            ok = _switch_to_secondary_tab(
                hwnd, ocr_model, secondary_list, sec_name
            )
            if not ok:
                logger.warning("  跳过二级Tab '%s'（无法切换）", sec_name)
                continue

            time.sleep(0.5)  # 等待成就列表加载

            # 扫描当前二级Tab下的所有成就（含自动滚动）
            page_results = scan_with_scroll(hwnd, ocr_model, achievements_db,
                                            stop_flag=stop_flag)

            # 合并到总进度，不降级已完成状态
            new_count = 0
            for r in page_results:
                aid = r.get("编号")
                if not aid:
                    continue
                status = r.get("状态", "未知")
                ocr_name = r.get("ocr_name", "")
                if status == "已完成":
                    prev = progress.get(aid, {}).get("获取状态")
                    if prev != "已完成":
                        progress[aid] = {"获取状态": "已完成", "ocr_name": ocr_name}
                        new_count += 1
                elif aid not in progress:
                    progress[aid] = {"获取状态": "未完成", "ocr_name": ocr_name}

            logger.info("  '%s' 扫描完成，新增已完成 %d 条，累计 %d 条",
                        sec_name, new_count, sum(
                            1 for v in progress.values() if v["获取状态"] == "已完成"
                        ))

            if callback:
                callback(progress, primary_name, sec_name)

    completed = sum(1 for v in progress.values() if v["获取状态"] == "已完成")
    logger.info("全量扫描完成：共 %d 条进度，其中已完成 %d 条", len(progress), completed)
    return progress
