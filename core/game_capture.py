"""
游戏窗口检测与截图模块
通过进程名定位鸣潮窗口，使用 mss 进行前台截图
"""
import ctypes
import ctypes.wintypes
import numpy as np
import mss

from core.config import get_ocr_config as _get_ocr_config
_cfg = _get_ocr_config()

# 鸣潮游戏进程名（从 config.ini 加载）
GAME_PROCESS_NAMES = _cfg["game_process_names"]

# 期望的游戏分辨率（从 config.ini 加载）
EXPECTED_WIDTH = _cfg["expected_width"]
EXPECTED_HEIGHT = _cfg["expected_height"]


def _get_pids_by_name(process_name):
    """通过进程名获取所有匹配的 PID 列表"""
    import subprocess
    try:
        output = subprocess.check_output(
            ["tasklist", "/FI", f"IMAGENAME eq {process_name}", "/FO", "CSV", "/NH"],
            text=True, encoding="gbk", errors="replace"
        )
        pids = []
        for line in output.strip().splitlines():
            parts = line.strip('"').split('","')
            if len(parts) >= 2:
                try:
                    pids.append(int(parts[1]))
                except ValueError:
                    pass
        return pids
    except Exception:
        return []


def find_game_window():
    """
    通过游戏进程名找到对应的窗口句柄 (HWND)。
    1. 查找 Wuthering Waves.exe 的 PID
    2. 枚举所有窗口，找到属于该 PID 的可见主窗口

    Returns:
        int: 窗口句柄 (HWND)
    Raises:
        RuntimeError: 未找到游戏窗口
    """
    pids = []
    for proc_name in GAME_PROCESS_NAMES:
        pids.extend(_get_pids_by_name(proc_name))
    if not pids:
        raise RuntimeError(
            f"未找到鸣潮游戏进程。请确保游戏已启动。\n"
            f"尝试查找的进程: {', '.join(GAME_PROCESS_NAMES)}"
        )

    pid_set = set(pids)
    user32 = ctypes.windll.user32
    found_hwnd = None

    @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)
    def enum_callback(hwnd, lparam):
        nonlocal found_hwnd
        if not user32.IsWindowVisible(hwnd):
            return True

        # 获取窗口所属进程 PID
        pid = ctypes.wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value not in pid_set:
            return True

        # 检查是否有合理大小的客户区域（排除小窗口/通知）
        class RECT(ctypes.Structure):
            _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                        ("right", ctypes.c_long), ("bottom", ctypes.c_long)]
        rect = RECT()
        user32.GetClientRect(hwnd, ctypes.byref(rect))
        w = rect.right - rect.left
        h = rect.bottom - rect.top
        if w >= 800 and h >= 600:
            found_hwnd = hwnd
            return False  # 找到了，停止枚举

        return True

    user32.EnumWindows(enum_callback, 0)

    if found_hwnd:
        return found_hwnd

    raise RuntimeError(
        f"找到了鸣潮进程 (PID: {pids})，但未找到对应的游戏窗口。\n"
        "请确保游戏已完全启动且窗口可见。"
    )


def get_window_rect(hwnd):
    """
    获取窗口的位置和大小（客户区域）。

    Args:
        hwnd: 窗口句柄
    Returns:
        tuple: (x, y, width, height) 客户区域的屏幕坐标和大小
    Raises:
        RuntimeError: 获取失败或分辨率不匹配
    """
    user32 = ctypes.windll.user32

    # 获取客户区域在屏幕上的坐标
    class POINT(ctypes.Structure):
        _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]

    class RECT(ctypes.Structure):
        _fields_ = [
            ("left", ctypes.c_long),
            ("top", ctypes.c_long),
            ("right", ctypes.c_long),
            ("bottom", ctypes.c_long),
        ]

    # 获取客户区域大小
    client_rect = RECT()
    if not user32.GetClientRect(hwnd, ctypes.byref(client_rect)):
        raise RuntimeError("获取窗口客户区域失败")

    width = client_rect.right - client_rect.left
    height = client_rect.bottom - client_rect.top

    # 将客户区域左上角转换为屏幕坐标
    pt = POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))

    if width != EXPECTED_WIDTH or height != EXPECTED_HEIGHT:
        raise RuntimeError(
            f"游戏窗口分辨率不匹配: {width}x{height}，"
            f"请将游戏设置为 {EXPECTED_WIDTH}x{EXPECTED_HEIGHT}"
        )

    return pt.x, pt.y, width, height


def capture_window(hwnd):
    """
    截取游戏窗口的客户区域图像。

    Args:
        hwnd: 窗口句柄
    Returns:
        numpy.ndarray: BGR 格式的截图图像 (H, W, 3)
    """
    x, y, width, height = get_window_rect(hwnd)

    with mss.mss() as sct:
        monitor = {"left": x, "top": y, "width": width, "height": height}
        screenshot = sct.grab(monitor)
        # mss 返回 BGRA，转换为 BGR
        img = np.array(screenshot)[:, :, :3]
        return img
