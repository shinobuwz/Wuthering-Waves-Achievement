"""
成就 OCR 识别与匹配模块
裁剪成就列表区域，OCR 识别成就名称和状态，模糊匹配到数据库
"""
import re
import time
import difflib
import ctypes
import ctypes.wintypes

import cv2
import numpy as np

# ============================================
# 像素坐标常量 (1920x1080)
# ============================================

# 右侧成就列表区域（相对于游戏窗口）
LIST_LEFT = 540
LIST_TOP = 200
LIST_RIGHT = 1900
LIST_BOTTOM = 1050

# 单条成就条目高度
ITEM_HEIGHT = 148

# 成就名称区域（相对于单条成就条目）
NAME_LEFT = 130
NAME_TOP = 5
NAME_RIGHT = 750
NAME_BOTTOM = 55

# 状态区域（相对于单条成就条目）——"进行中" 或日期
STATUS_LEFT = 780
STATUS_TOP = 5
STATUS_RIGHT = 1050
STATUS_BOTTOM = 55

# 滚动区域中心点（相对于游戏窗口）
SCROLL_CENTER_X = 1000
SCROLL_CENTER_Y = 600

# 每次滚动的条目数
SCROLL_ITEMS = 3

# 滚动后等待时间（秒）
SCROLL_DELAY = 0.8


def crop_achievement_list(screenshot):
    """
    从完整截图裁剪右侧成就列表区域。

    Args:
        screenshot: numpy.ndarray, 1920x1080 BGR 截图
    Returns:
        numpy.ndarray: 裁剪后的成就列表区域
    """
    return screenshot[LIST_TOP:LIST_BOTTOM, LIST_LEFT:LIST_RIGHT].copy()


def split_achievement_items(list_image):
    """
    按固定高度将成就列表切割为单条成就图像列表。

    Args:
        list_image: 裁剪后的成就列表区域图像
    Returns:
        list[numpy.ndarray]: 成就条目图像列表
    """
    height = list_image.shape[0]
    items = []
    y = 0
    while y + ITEM_HEIGHT <= height:
        item = list_image[y:y + ITEM_HEIGHT, :].copy()
        # 检查是否为有效条目（不是空白区域）
        if _is_valid_item(item):
            items.append(item)
        y += ITEM_HEIGHT
    return items


def _is_valid_item(item_image):
    """
    判断切割出的区域是否包含有效的成就条目。
    通过检查图像的平均亮度和方差来判断。
    """
    gray = cv2.cvtColor(item_image, cv2.COLOR_BGR2GRAY)
    std = np.std(gray)
    # 如果标准差太低，说明是纯色背景
    return std > 15


def preprocess_image(image):
    """
    对图像进行预处理以提高 OCR 识别率。
    包括对比度增强和锐化。

    Args:
        image: BGR 图像
    Returns:
        numpy.ndarray: 预处理后的 BGR 图像
    """
    # 转灰度
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

    # CLAHE 对比度增强
    clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
    enhanced = clahe.apply(gray)

    # 转回 BGR（onnxocr 需要 BGR 输入）
    return cv2.cvtColor(enhanced, cv2.COLOR_GRAY2BGR)


def recognize_achievement_name(item_image, ocr_model):
    """
    裁剪成就名称区域并 OCR 识别。

    Args:
        item_image: 单条成就条目图像
        ocr_model: ONNXPaddleOcr 实例
    Returns:
        str: 识别出的成就名称，识别失败返回空字符串
    """
    name_region = item_image[NAME_TOP:NAME_BOTTOM, NAME_LEFT:NAME_RIGHT].copy()
    processed = preprocess_image(name_region)

    try:
        results = ocr_model.ocr(processed)
        if results:
            # 合并所有识别到的文本
            return "".join(results).strip()
    except Exception as e:
        print(f"[OCR] 名称识别异常: {e}")

    return ""


def recognize_achievement_status(item_image, ocr_model):
    """
    裁剪状态区域，OCR 识别"进行中"或日期。

    Args:
        item_image: 单条成就条目图像
        ocr_model: ONNXPaddleOcr 实例
    Returns:
        str: "已完成" | "未完成" | "未知"
    """
    status_region = item_image[STATUS_TOP:STATUS_BOTTOM, STATUS_LEFT:STATUS_RIGHT].copy()
    processed = preprocess_image(status_region)

    try:
        results = ocr_model.ocr(processed)
        if results:
            text = "".join(results).strip()
            # 判断日期格式 YYYY/MM/DD
            if re.search(r'\d{4}/\d{2}/\d{2}', text):
                return "已完成"
            # 判断"进行中"
            if "进行中" in text or "进行" in text:
                return "未完成"
    except Exception as e:
        print(f"[OCR] 状态识别异常: {e}")

    return "未知"


def match_achievement(ocr_name, achievements_db):
    """
    将 OCR 识别的名称模糊匹配到成就数据库。

    Args:
        ocr_name: OCR 识别出的成就名称
        achievements_db: list[dict], base_achievements.json 数据
    Returns:
        tuple: (编号, 匹配到的名称, 置信度) 或 (None, None, 0) 匹配失败
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


def scan_single_page(screenshot, ocr_model, achievements_db):
    """
    扫描当前页面的所有可见成就条目。

    Args:
        screenshot: 1920x1080 BGR 截图
        ocr_model: ONNXPaddleOcr 实例
        achievements_db: 成就数据库列表
    Returns:
        list[dict]: 每条成就的识别结果
            [{
                "编号": str or None,
                "ocr_name": str,
                "matched_name": str or None,
                "状态": str,
                "置信度": float
            }]
    """
    list_image = crop_achievement_list(screenshot)
    items = split_achievement_items(list_image)

    results = []
    for item in items:
        ocr_name = recognize_achievement_name(item, ocr_model)
        status = recognize_achievement_status(item, ocr_model)
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
    # 计算滚动位置（屏幕绝对坐标）
    screen_x = x + SCROLL_CENTER_X
    screen_y = y + SCROLL_CENTER_Y

    # 移动鼠标到滚动区域
    ctypes.windll.user32.SetCursorPos(screen_x, screen_y)
    time.sleep(0.1)

    # 模拟鼠标滚轮
    MOUSEEVENTF_WHEEL = 0x0800
    WHEEL_DELTA = 120

    # 构造 lparam（鼠标位置）
    lparam = (screen_y << 16) | (screen_x & 0xFFFF)

    for _ in range(abs(scroll_amount)):
        direction = -1 if scroll_amount < 0 else 1
        ctypes.windll.user32.mouse_event(
            MOUSEEVENTF_WHEEL, 0, 0, direction * WHEEL_DELTA, 0
        )
        time.sleep(0.05)


def scan_with_scroll(hwnd, ocr_model, achievements_db, callback=None, stop_flag=None):
    """
    循环执行截图→OCR→滚动，直到扫描完所有成就。

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
    unmatched_results = []  # 未匹配的结果
    prev_names = set()
    round_num = 0

    while True:
        # 检查停止标志
        if stop_flag and stop_flag():
            break

        round_num += 1
        screenshot = capture_window(hwnd)
        page_results = scan_single_page(screenshot, ocr_model, achievements_db)

        # 提取当前页面的成就名称
        current_names = set()
        for result in page_results:
            if result["编号"]:
                current_names.add(result["matched_name"])
                # 去重：只保留首次识别的结果
                if result["编号"] not in all_results:
                    all_results[result["编号"]] = result
            elif result["ocr_name"]:
                # 未匹配的也记录
                unmatched_results.append(result)

        # 回调报告进度
        if callback:
            merged = list(all_results.values()) + unmatched_results
            callback(merged, round_num)

        # 检测是否到底：如果当前页面识别到的名称完全在之前的集合中
        if current_names and current_names.issubset(prev_names):
            break

        # 如果没有识别到任何名称（可能是空页面），也停止
        if not current_names and round_num > 1:
            break

        # 更新已见名称集合
        prev_names.update(current_names)

        # 滚动
        simulate_scroll(hwnd, scroll_amount=-SCROLL_ITEMS)
        time.sleep(SCROLL_DELAY)

    return list(all_results.values()) + unmatched_results
