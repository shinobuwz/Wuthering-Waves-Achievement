"""
Nuitka 打包脚本
打包为单个可执行文件，onnxocr 和 ocr_templates 内置，resources 外置
"""
import subprocess
import sys
import shutil
from pathlib import Path
from version import VERSION


def build():
    """执行 Nuitka 打包"""

    project_root = Path(__file__).parent
    main_file = project_root / "main.py"
    icon_file = project_root / "resources" / "icons" / "logo.ico"
    output_filename = f"WutheringWavesAchievement-{VERSION}.exe"
    dist_dir = project_root / "dist"

    # 清理 dist 目录
    if dist_dir.exists():
        print("=" * 60)
        print("正在清理 dist 目录...")
        shutil.rmtree(dist_dir)
        print("dist 目录已清理")
        print("=" * 60)

    cmd = [
        sys.executable,
        "-m", "nuitka",
        "--standalone",
        "--onefile",
        "--windows-disable-console",
        "--enable-plugin=pyside6",
        f"--windows-icon-from-ico={icon_file}",
        "--output-dir=dist",
        f"--output-filename={output_filename}",
        "--company-name=Silence",
        "--product-name=Wuthering Waves Achievement Manager",
        f"--file-version={VERSION}",
        f"--product-version={VERSION}",
        "--file-description=Wuthering Waves Achievement Tool",
        "--assume-yes-for-downloads",
        "--show-progress",
        # 包含 onnxocr 模型文件和 OCR 模板
        "--include-data-dir=onnxocr=onnxocr",
        "--include-data-dir=resources/ocr_templates=resources/ocr_templates",
        # 排除不必要的模块
        "--nofollow-import-to=matplotlib",
        "--nofollow-import-to=scipy",
        "--nofollow-import-to=pandas",
        str(main_file)
    ]

    print("=" * 60)
    print("开始打包...")
    print(f"版本: {VERSION}")
    print(f"输出: dist/{output_filename}")
    print("=" * 60)

    try:
        result = subprocess.run(cmd, cwd=project_root, check=True)
        print("\n" + "=" * 60)
        print("打包完成!")
        print(f"可执行文件: dist/{output_filename}")
        print("=" * 60)
        return result.returncode
    except subprocess.CalledProcessError as e:
        print("\n" + "=" * 60)
        print(f"打包失败: {e}")
        print("=" * 60)
        return e.returncode


if __name__ == "__main__":
    sys.exit(build())
