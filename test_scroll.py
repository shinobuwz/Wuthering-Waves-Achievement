"""
Tab 切换与全量扫描测试脚本
支持手动切换一/二级Tab，以及全量自动扫描所有成就并保存进度。
"""
import json
import time
import ctypes
import sys
import os

import pyautogui as pi

# 添加项目根目录到 path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from core.game_capture import find_game_window, get_window_rect, capture_window
from core.achievement_ocr import (
    _edit_distance,
    click_primary_tab,
    recognize_secondary_tabs as _recognize_secondary_tabs,
    click_secondary_tab as _click_secondary_tab,
    scroll_secondary_tabs as _scroll_secondary_tabs,
    scan_all_tabs,
)

pi.PAUSE = 0.1
pi.FAILSAFE = False

user32 = ctypes.windll.user32


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
# 本地别名（使用 achievement_ocr 中的实现）
# ============================================

recognize_secondary_tabs = _recognize_secondary_tabs
click_secondary_tab = _click_secondary_tab
scroll_secondary_tabs = _scroll_secondary_tabs


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


# ============================================
# 主程序
# ============================================

def main():
    import logging
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s [%(levelname)s] %(message)s',
        datefmt='%H:%M:%S',
    )

    from onnxocr import ONNXPaddleOcr
    from core.achievement_ocr import recognize_primary_tab

    print("加载 OCR 模型...")
    ocr_model = ONNXPaddleOcr(use_angle_cls=False, use_gpu=False)

    hwnd = find_game_window()
    wx, wy, ww, wh = get_window_rect(hwnd)
    print(f"窗口: ({wx},{wy}) {ww}x{wh}")

    category_map = load_category_map()
    db_path = os.path.join(os.path.dirname(__file__), "resources", "base_achievements.json")
    achievements_db = json.load(open(db_path, encoding='utf-8'))

    print(f"\n分类概览:")
    for i, (name, subs) in enumerate(category_map.items()):
        print(f"  p{i}: {name} ({len(subs)} 个二级Tab)")

    print(f"\n命令说明:")
    print(f"  scan     - 全量扫描所有Tab，完成后保存进度")
    print(f"  p<n>     - 手动切换一级Tab (p0~p3)")
    print(f"  <序号>   - 点击当前可见的二级Tab")
    print(f"  s        - 滚动二级Tab列表")
    print(f"  r        - 刷新截图")
    print(f"  q        - 退出")

    print(f"\n5秒后开始，请确保游戏在前台的成就页面！")
    time.sleep(5)

    user32.SetForegroundWindow(hwnd)
    time.sleep(0.5)

    # 识别当前一级 Tab
    screenshot = capture_window(hwnd)
    primary_name = recognize_primary_tab(screenshot, ocr_model)
    matched_primary = None
    for cat_name in category_map:
        if _edit_distance(primary_name, cat_name) <= len(cat_name) * 0.4:
            matched_primary = cat_name
            break
    if not matched_primary:
        matched_primary = list(category_map.keys())[0]
    secondary_list = category_map[matched_primary]
    print(f"\n当前一级Tab: '{matched_primary}'")

    # 主交互循环
    while True:
        screenshot = capture_window(hwnd)
        visible_tabs = recognize_secondary_tabs(screenshot, ocr_model, secondary_list)

        print(f"\n[{matched_primary}] 可见二级Tab ({len(visible_tabs)} 个):")
        for i, (name, cy_pct, ocr_raw) in enumerate(visible_tabs):
            print(f"  [{i}] {name} (y={cy_pct:.1%})")

        cmd = input("\n> ").strip()

        if cmd == 'q':
            break

        elif cmd == 'scan':
            print("\n开始全量扫描...")

            def on_progress(progress, primary, secondary):
                completed = sum(1 for v in progress.values() if v["获取状态"] == "已完成")
                print(f"  [{primary}] '{secondary}' 完成 - 累计已完成: {completed} 条")

            progress = scan_all_tabs(
                hwnd, ocr_model, achievements_db, category_map,
                callback=on_progress
            )

            # 保存结果
            out_path = os.path.join(os.path.dirname(__file__), "resources", "ocr_scan_result.json")
            with open(out_path, 'w', encoding='utf-8') as f:
                json.dump(progress, f, ensure_ascii=False, indent=2)

            completed = sum(1 for v in progress.values() if v["获取状态"] == "已完成")
            print(f"\n扫描完成！共 {len(progress)} 条，已完成 {completed} 条")
            print(f"结果已保存到: {out_path}")

        elif cmd == 's':
            scroll_secondary_tabs(hwnd)

        elif cmd == 'r':
            continue

        elif cmd.startswith('p') and cmd[1:].isdigit():
            pi_idx = int(cmd[1:])
            pri_names = list(category_map.keys())
            if 0 <= pi_idx < len(pri_names):
                matched_primary = pri_names[pi_idx]
                print(f"切换一级Tab: '{matched_primary}'")
                click_primary_tab(hwnd, pi_idx)
                secondary_list = category_map[matched_primary]
            else:
                print(f"超出范围 0~{len(category_map)-1}")

        elif cmd.isdigit():
            idx = int(cmd)
            if 0 <= idx < len(visible_tabs):
                name, cy_pct, _ = visible_tabs[idx]
                print(f"点击: '{name}'")
                click_secondary_tab(hwnd, cy_pct)
            else:
                print(f"超出范围 0~{len(visible_tabs)-1}")

        else:
            target_idx, dist = find_best_tab_match(cmd, visible_tabs)
            if target_idx >= 0:
                name, cy_pct, _ = visible_tabs[target_idx]
                print(f"匹配到: '{name}', 点击...")
                click_secondary_tab(hwnd, cy_pct)
            else:
                print(f"未识别命令: '{cmd}'")

    print("测试结束！")


if __name__ == "__main__":
    main()
