"""
测试二级 Tab 切换 - OCR 识别 + 点击切换
流程：截图 → 识别一级Tab → 加载二级Tab列表 → 识别可见二级Tab → 点击/滚动切换
"""
import json
import time
import ctypes
import sys
import os

import cv2
import numpy as np
import pyautogui as pi

# 添加项目根目录到 path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from core.game_capture import find_game_window, get_window_rect, capture_window
from core.achievement_ocr import (
    recognize_text, preprocess_image, _edit_distance,
    click_primary_tab, PRIMARY_TAB_ICON_Y_PCTS,
)

pi.PAUSE = 0.1
pi.FAILSAFE = False

user32 = ctypes.windll.user32

# ============================================
# 界面布局参数（百分比，基于截图宽高）
# ============================================

# 一级 Tab 名称区域（百分比，由模板匹配确定）
PRIMARY_TAB_X1_PCT = 0.053
PRIMARY_TAB_Y1_PCT = 0.047
PRIMARY_TAB_X2_PCT = 0.114
PRIMARY_TAB_Y2_PCT = 0.083

# 二级 Tab 列表区域（百分比，由模板匹配确定）
SECONDARY_TAB_X1_PCT = 0.1005
SECONDARY_TAB_Y1_PCT = 0.1796
SECONDARY_TAB_X2_PCT = 0.3479
SECONDARY_TAB_Y2_PCT = 1.0000

# 二级 Tab 单项高度（占截图高度的百分比）


# ============================================
# 分类数据加载
# ============================================

def load_category_map():
    """从 base_achievements.json 加载一级→二级分类映射"""
    db_path = os.path.join(os.path.dirname(__file__), "resources", "base_achievements.json")
    with open(db_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    cats = {}
    for a in data:
        c1 = a.get('第一分类', '')
        c2 = a.get('第二分类', '')
        if c1 not in cats:
            cats[c1] = []
        if c2 not in cats[c1]:
            cats[c1].append(c2)
    return cats

# ============================================
# OCR 识别
# ============================================

def pct_to_px(screenshot, x_pct, y_pct):
    """百分比 → 像素坐标"""
    h, w = screenshot.shape[:2]
    return int(w * x_pct), int(h * y_pct)


def recognize_primary_tab(screenshot, ocr_model, debug=True):
    """识别左上角一级 Tab 名称"""
    x1, y1 = pct_to_px(screenshot, PRIMARY_TAB_X1_PCT, PRIMARY_TAB_Y1_PCT)
    x2, y2 = pct_to_px(screenshot, PRIMARY_TAB_X2_PCT, PRIMARY_TAB_Y2_PCT)
    region = screenshot[y1:y2, x1:x2].copy()
    if debug:
        cv2.imwrite("debug_primary_tab.png", region)
        print(f"  一级Tab裁剪: ({x1},{y1})-({x2},{y2}) size={region.shape}")
    text = recognize_text(region, ocr_model)
    return text.strip()


def recognize_secondary_tabs(screenshot, ocr_model, known_tabs, debug=True):
    """
    识别左侧面板中当前可见的所有二级 Tab。
    使用 OCR detect box 的 y 坐标来确定每个 Tab 的位置，而非固定槽位切割。

    Args:
        screenshot: 截图
        ocr_model: OCR 模型
        known_tabs: 已知的二级 Tab 名称列表（顺序一致）
        debug: 是否保存调试图片
    Returns:
        list[(已知名称, center_y_pct, ocr原文)] 按位置排序
    """
    h, w = screenshot.shape[:2]
    sx1, sy1 = pct_to_px(screenshot, SECONDARY_TAB_X1_PCT, SECONDARY_TAB_Y1_PCT)
    sx2, sy2 = pct_to_px(screenshot, SECONDARY_TAB_X2_PCT, SECONDARY_TAB_Y2_PCT)

    # 裁剪二级 Tab 区域
    region = screenshot[sy1:sy2, sx1:sx2].copy()
    if debug:
        cv2.imwrite("debug_secondary_tabs.png", region)
        print(f"  二级Tab区域: ({sx1},{sy1})-({sx2},{sy2}) size={region.shape}")

    # 预处理
    processed = preprocess_image(region)

    # 调用 OCR，获取 detect box 坐标和识别结果
    dt_boxes, rec_res = ocr_model(processed, cls=False)

    if debug:
        print(f"  OCR 检测到 {len(rec_res)} 个文本框:")
        for i, (box, (text, conf)) in enumerate(zip(dt_boxes, rec_res)):
            y_min = min(p[1] for p in box)
            y_max = max(p[1] for p in box)
            x_min = min(p[0] for p in box)
            x_max = max(p[0] for p in box)
            print(f"    [{i}] text='{text}' conf={conf:.3f} "
                  f"box=({x_min:.0f},{y_min:.0f})-({x_max:.0f},{y_max:.0f})")

    # 提取每个检测结果的 y 中心（相对于截图的百分比）
    raw_tabs = []  # [(text, center_y_pct_in_screenshot, conf)]
    rh = region.shape[0]  # 裁剪区域高度
    for box, (text, conf) in zip(dt_boxes, rec_res):
        text = text.strip()
        if not text or conf < 0.5:
            continue
        # box 坐标是相对于裁剪区域的，转换为截图坐标
        y_center_in_region = (min(p[1] for p in box) + max(p[1] for p in box)) / 2
        y_center_in_screenshot = sy1 + y_center_in_region
        center_y_pct = y_center_in_screenshot / h
        raw_tabs.append((text, center_y_pct, conf))

    # 按 y 坐标排序
    raw_tabs.sort(key=lambda x: x[1])

    if debug:
        print(f"  排序后的 OCR 结果 ({len(raw_tabs)} 个):")
        for i, (text, cy_pct, conf) in enumerate(raw_tabs):
            print(f"    [{i}] y={cy_pct:.4f} text='{text}' conf={conf:.3f}")

    # 用编辑距离将每个 OCR 结果匹配到已知 Tab
    matched = []  # [(known_idx, center_y_pct, ocr_text)]
    used_known = set()
    for text, cy_pct, conf in raw_tabs:
        best_ki = -1
        best_dist = float('inf')
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

    if not matched:
        # 无法匹配，返回原始 OCR 结果
        return [(t, cy, t) for t, cy, _ in raw_tabs]

    # 按 y 坐标排序
    matched.sort(key=lambda x: x[1])

    if debug:
        print(f"  匹配结果 ({len(matched)} 个):")
        for ki, cy_pct, text in matched:
            print(f"    known[{ki}]={known_tabs[ki]} ← ocr='{text}' y={cy_pct:.4f}")

    return [(known_tabs[ki], cy_pct, ocr_text) for ki, cy_pct, ocr_text in matched]


def find_best_tab_match(target_name, visible_tabs):
    """用编辑距离找到最匹配的可见 Tab"""
    best_idx = -1
    best_dist = float('inf')
    for i, (name, _, _) in enumerate(visible_tabs):
        dist = _edit_distance(target_name, name)
        if dist < best_dist:
            best_dist = dist
            best_idx = i
    if best_idx >= 0:
        max_dist = max(len(target_name), len(visible_tabs[best_idx][0])) * 0.4
        if best_dist <= max_dist:
            return best_idx, best_dist
    return -1, best_dist


def click_secondary_tab(hwnd, center_y_pct):
    """点击指定百分比 y 坐标的二级 Tab"""
    wx, wy, ww, wh = get_window_rect(hwnd)
    click_x = wx + int(ww * (SECONDARY_TAB_X1_PCT + SECONDARY_TAB_X2_PCT) / 2)
    click_y = wy + int(wh * center_y_pct)

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)
    pi.moveTo(click_x, click_y, duration=0.1)
    time.sleep(0.1)
    pi.click()
    time.sleep(0.5)


def scroll_secondary_tabs(hwnd):
    """在二级 Tab 列表区域滚动"""
    wx, wy, ww, wh = get_window_rect(hwnd)
    scroll_x = wx + int(ww * (SECONDARY_TAB_X1_PCT + SECONDARY_TAB_X2_PCT) / 2)
    scroll_y = wy + int(wh * (SECONDARY_TAB_Y1_PCT + SECONDARY_TAB_Y2_PCT) / 2)

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.3)
    pi.moveTo(scroll_x, scroll_y, duration=0.1)
    pi.click()
    time.sleep(0.3)
    for _ in range(16):
        pi.scroll(-160)
    time.sleep(0.8)


# ============================================
# 主程序
# ============================================

def main():
    # 初始化
    from onnxocr import ONNXPaddleOcr
    print("加载 OCR 模型...")
    ocr_model = ONNXPaddleOcr(use_angle_cls=False, use_gpu=False)

    hwnd = find_game_window()
    wx, wy, ww, wh = get_window_rect(hwnd)
    print(f"窗口: ({wx},{wy}) {ww}x{wh}")

    category_map = load_category_map()
    primary_names = list(category_map.keys())
    print(f"已加载分类:")
    for i, name in enumerate(primary_names):
        print(f"  p{i}: {name}")

    print(f"\n5秒后开始，请确保游戏在前台的成就页面！")
    time.sleep(5)

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.5)

    # 1. 识别一级 Tab
    screenshot = capture_window(hwnd)
    cv2.imwrite("debug_full_screenshot.png", screenshot)
    print(f"完整截图已保存: debug_full_screenshot.png ({screenshot.shape})")
    primary_name = recognize_primary_tab(screenshot, ocr_model)
    print(f"\n识别到一级 Tab: '{primary_name}'")

    # 模糊匹配一级分类
    matched_primary = None
    for cat_name in category_map:
        dist = _edit_distance(primary_name, cat_name)
        if dist <= len(cat_name) * 0.3:
            matched_primary = cat_name
            break

    if not matched_primary:
        print(f"无法匹配一级分类！OCR 结果: '{primary_name}'")
        print(f"可选分类: {list(category_map.keys())}")
        return

    secondary_list = category_map[matched_primary]
    print(f"匹配到: '{matched_primary}'，共 {len(secondary_list)} 个二级 Tab:")
    for i, name in enumerate(secondary_list):
        print(f"  {i}: {name}")

    # 2. 交互式循环
    while True:
        # 截图识别可见的二级 Tab
        screenshot = capture_window(hwnd)
        visible_tabs = recognize_secondary_tabs(screenshot, ocr_model, secondary_list)

        print(f"\n当前可见的二级 Tab ({len(visible_tabs)} 个):")
        for i, (name, cy_pct, ocr_raw) in enumerate(visible_tabs):
            print(f"  [{i}] {name} (y={cy_pct:.1%}, ocr='{ocr_raw}')")

        cmd = input("\n输入序号点击, p<n>=切换一级Tab, s=滚动, r=刷新, q=退出: ").strip()
        if cmd == 'q':
            break
        elif cmd == 's':
            scroll_secondary_tabs(hwnd)
            continue
        elif cmd == 'r':
            continue
        elif cmd.startswith('p') and cmd[1:].isdigit():
            pi_idx = int(cmd[1:])
            if 0 <= pi_idx < len(primary_names):
                print(f"切换一级Tab[{pi_idx}]: '{primary_names[pi_idx]}'")
                click_primary_tab(hwnd, pi_idx)
                # 重新识别一级Tab对应的二级列表
                matched_primary = primary_names[pi_idx]
                secondary_list = category_map[matched_primary]
                print(f"  二级Tab列表 ({len(secondary_list)} 个): {secondary_list}")
            else:
                print(f"一级Tab序号超出范围 0~{len(primary_names)-1}")
            continue
        elif cmd.isdigit():
            idx = int(cmd)
            if 0 <= idx < len(visible_tabs):
                name, cy_pct, _ = visible_tabs[idx]
                print(f"点击: '{name}' (y={cy_pct:.1%})")
                click_secondary_tab(hwnd, cy_pct)
            else:
                print(f"序号超出范围 0~{len(visible_tabs)-1}")
        else:
            # 尝试用名称匹配
            target_idx, dist = find_best_tab_match(cmd, visible_tabs)
            if target_idx >= 0:
                name, cy_pct, _ = visible_tabs[target_idx]
                print(f"匹配到: '{name}' (dist={dist}), 点击...")
                click_secondary_tab(hwnd, cy_pct)
            else:
                print(f"未匹配到 Tab: '{cmd}'")

    print("测试结束！")


if __name__ == "__main__":
    main()
