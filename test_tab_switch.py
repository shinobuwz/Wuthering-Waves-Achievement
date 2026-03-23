"""
测试一级/二级 Tab 切换逻辑
在游戏成就页面打开的状态下运行，验证能否正确识别并切换所有一级和二级 Tab。
不执行 OCR 扫描，仅测试 Tab 切换和识别。

用法：
    conda run -n ww-achievement python test_tab_switch.py
"""
import sys
import time
import json
import logging

logging.basicConfig(
    level=logging.DEBUG,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)
logger = logging.getLogger("test_tab_switch")


def load_category_map():
    """从 base_achievements.json 构建 category_map"""
    from core.config import Config
    cfg = Config()
    achievements = cfg.load_base_achievements()
    category_map = {}
    for a in achievements:
        c1 = a.get("第一分类", "")
        c2 = a.get("第二分类", "")
        if c1 and c2:
            if c1 not in category_map:
                category_map[c1] = []
            if c2 not in category_map[c1]:
                category_map[c1].append(c2)
    return category_map


def test_tab_switching():
    """测试所有一级/二级 Tab 的切换与识别"""
    from core.game_capture import find_game_window, capture_window
    from core.achievement_ocr import (
        click_primary_tab,
        recognize_primary_tab,
        _iterate_secondary_tabs,
        PRIMARY_TAB_NAMES,
        DELAY_LIST_LOAD,
    )
    from onnxocr import ONNXPaddleOcr

    logger.info("=" * 60)
    logger.info("初始化 OCR 模型...")
    ocr_model = ONNXPaddleOcr(use_angle_cls=False, use_gpu=False)

    logger.info("查找游戏窗口...")
    hwnd = find_game_window()
    logger.info("游戏窗口句柄: %s", hwnd)

    category_map = load_category_map()
    logger.info("分类映射:")
    for k, v in category_map.items():
        logger.info("  %s: %s", k, v)

    results = {}  # {primary: {secondary: bool}}
    total_ok = 0
    total_fail = 0

    for pi_idx, primary_name in enumerate(PRIMARY_TAB_NAMES):
        logger.info("")
        logger.info("=" * 60)
        logger.info("切换一级Tab [%d] '%s'", pi_idx, primary_name)
        logger.info("=" * 60)

        click_primary_tab(hwnd, pi_idx)

        # 验证一级Tab切换
        max_retries = 3
        tab_ok = False
        for attempt in range(max_retries):
            screenshot = capture_window(hwnd)
            recognized = recognize_primary_tab(screenshot, ocr_model)
            logger.info("  一级Tab识别结果: '%s' (期望: '%s')", recognized, primary_name)
            if primary_name in recognized or recognized in primary_name:
                tab_ok = True
                break
            logger.warning("  不匹配，重试点击...")
            click_primary_tab(hwnd, pi_idx)

        if not tab_ok:
            logger.error("  一级Tab '%s' 切换失败!", primary_name)
            results[primary_name] = {"__primary_switch__": False}
            total_fail += 1
            continue

        logger.info("  一级Tab '%s' 切换成功 ✓", primary_name)
        results[primary_name] = {"__primary_switch__": True}

        # 测试二级Tab
        secondary_list = category_map.get(primary_name, [])
        if not secondary_list:
            logger.info("  无二级Tab数据，跳过")
            continue

        logger.info("  共 %d 个二级Tab: %s", len(secondary_list), secondary_list)

        for sec_name, cy_pct in _iterate_secondary_tabs(hwnd, ocr_model, secondary_list):
            logger.info("")
            logger.info("  -> 二级Tab '%s' (y=%.4f) 切换成功 ✓", sec_name, cy_pct)
            results[primary_name][sec_name] = True
            total_ok += 1
            time.sleep(DELAY_LIST_LOAD)

        # 检查未访问到的二级Tab
        for sec_name in secondary_list:
            if sec_name not in results.get(primary_name, {}):
                logger.error("     二级Tab '%s' 未被访问到 ✗", sec_name)
                results[primary_name][sec_name] = False
                total_fail += 1

    # 打印汇总报告
    logger.info("")
    logger.info("=" * 60)
    logger.info("测试报告")
    logger.info("=" * 60)

    for primary_name in PRIMARY_TAB_NAMES:
        pri_results = results.get(primary_name, {})
        pri_ok = pri_results.get("__primary_switch__", False)
        status = "✓" if pri_ok else "✗"
        logger.info("[%s] 一级Tab: %s", status, primary_name)

        secondary_list = category_map.get(primary_name, [])
        for sec_name in secondary_list:
            sec_ok = pri_results.get(sec_name)
            if sec_ok is None:
                s = "⊘ 未测试"
            elif sec_ok:
                s = "✓"
            else:
                s = "✗"
            logger.info("    [%s] %s", s, sec_name)

    logger.info("")
    logger.info("总计: 成功 %d, 失败 %d", total_ok, total_fail)
    logger.info("=" * 60)

    return total_fail == 0


if __name__ == "__main__":
    success = test_tab_switching()
    sys.exit(0 if success else 1)
