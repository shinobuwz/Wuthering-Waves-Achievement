"""
成就 OCR 识别与匹配模块
通过模板匹配定位成就图标，基于相对偏移裁剪名称和状态区域进行 OCR 识别
"""
import os
import re
import time
import difflib
import ctypes
import ctypes.wintypes

import cv2
import numpy as np

# ============================================
# 模板匹配参数
# ============================================

# 图标模板目录
_TEMPLATE_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "resources", "ocr_templates"
)

# 模板匹配阈值
MATCH_THRESHOLD = 0.75

# NMS 合并距离（像素）
NMS_DISTANCE = 40

# 名称区域相对于图标左上角的偏移和大小
NAME_DX = 122
NAME_DY = -39
NAME_W = 503
NAME_H = 40

# 状态区域相对于图标左上角的偏移和大小
STATUS_DX = 878
STATUS_DY = 15
STATUS_W = 163
STATUS_H = 47

# 滚动区域中心点（相对于游戏窗口）
SCROLL_CENTER_X = 1200
SCROLL_CENTER_Y = 600

# 每次滚动的条目数
SCROLL_ITEMS = 3

# 滚动后等待时间（秒）
SCROLL_DELAY = 0.8

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
            return "".join(results).strip()
    except Exception as e:
        print(f"[OCR] 识别异常: {e}")
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

    best_match = None
    best_ratio = 0
    best_name = None

    for achievement in achievements_db:
        name = achievement.get("名称", "")
        ratio = difflib.SequenceMatcher(None, ocr_name, name).ratio()
        if ratio > best_ratio:
            best_ratio = ratio
            best_match = achievement.get("编号")
            best_name = name

    if best_ratio >= 0.7:
        return best_match, best_name, best_ratio
    return None, None, 0


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

        results.append({
            "编号": achievement_id,
            "ocr_name": ocr_name,
            "matched_name": matched_name,
            "状态": status,
            "置信度": confidence,
        })

    return results


# ============================================
# 自动滚动扫描
# ============================================

def simulate_scroll(hwnd, scroll_amount=-3):
    """
    模拟鼠标滚轮向下滚动。

    Args:
        hwnd: 游戏窗口句柄
        scroll_amount: 滚动量（负数为向下），每单位约 120 像素
    """
    from core.game_capture import get_window_rect

    x, y, _, _ = get_window_rect(hwnd)
    screen_x = x + SCROLL_CENTER_X
    screen_y = y + SCROLL_CENTER_Y

    ctypes.windll.user32.SetCursorPos(screen_x, screen_y)
    time.sleep(0.1)

    MOUSEEVENTF_WHEEL = 0x0800
    WHEEL_DELTA = 120

    for _ in range(abs(scroll_amount)):
        direction = -1 if scroll_amount < 0 else 1
        ctypes.windll.user32.mouse_event(
            MOUSEEVENTF_WHEEL, 0, 0, direction * WHEEL_DELTA, 0
        )
        time.sleep(0.05)


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
            break

        if not current_names and round_num > 1:
            break

        prev_names.update(current_names)

        simulate_scroll(hwnd, scroll_amount=-SCROLL_ITEMS)
        time.sleep(SCROLL_DELAY)

    return list(all_results.values()) + unmatched_results
