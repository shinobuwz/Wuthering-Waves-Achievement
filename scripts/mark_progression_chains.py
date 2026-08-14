#!/usr/bin/env python3
"""标记共用累计进度的成就链。

Wiki 当前只提供成就名称和描述，没有单独的“进阶/共用进度”字段。
本脚本根据以下保守规则推断成就链：

* 名称位于同一第一/第二分类；
* 名称主体相同，末尾是“一/二/三…”，“Ⅰ/Ⅱ/Ⅲ…”或数字等级；
* 至少包含两个不同等级。

脚本复用现有“成就组ID”字段，不新增成就组页或额外字段。为区分语义，
累计进度链使用 `progression-` 前缀；现有 Wiki 的 `wiki-choice:` 和手工 `group_`
仍保留为原来的成就组。进阶等级继续从名称末尾的一/二/三推断，不写入额外字段。

默认只预览，不修改文件。确认报告后使用 --write 写回，并自动生成 .bak 备份。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
from collections import defaultdict
from pathlib import Path
from typing import Any

GROUP_KEY = "成就组ID"
PROGRESSION_PREFIX = "progression-"

_CHINESE_DIGITS = {
    "一": 1,
    "二": 2,
    "三": 3,
    "四": 4,
    "五": 5,
    "六": 6,
    "七": 7,
    "八": 8,
    "九": 9,
    "十": 10,
}
_ROMAN_DIGITS = {
    "Ⅰ": 1,
    "Ⅱ": 2,
    "Ⅲ": 3,
    "Ⅳ": 4,
    "Ⅴ": 5,
    "Ⅵ": 6,
    "Ⅶ": 7,
    "Ⅷ": 8,
    "Ⅸ": 9,
    "Ⅹ": 10,
}
# 只把带明显分隔符的末尾数字当作等级，避免把“第3区域”等普通名称误判。
_TIER_SUFFIX = re.compile(
    r"^(?P<base>.+?)(?:[·•・\s]+)(?P<tier>[一二三四五六七八九十ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]|[0-9]+)$"
)


def normalize(value: str) -> str:
    value = str(value or "").replace("・", "·").replace("•", "·")
    return " ".join(value.strip().split())


def parse_tier(name: str) -> tuple[str, int] | None:
    match = _TIER_SUFFIX.match(normalize(name))
    if not match:
        return None

    suffix = match.group("tier")
    if suffix in _CHINESE_DIGITS:
        tier = _CHINESE_DIGITS[suffix]
    elif suffix in _ROMAN_DIGITS:
        tier = _ROMAN_DIGITS[suffix]
    else:
        tier = int(suffix)

    if tier <= 0:
        return None
    return normalize(match.group("base")).rstrip("·•・ "), tier


def make_group_id(first_category: str, second_category: str, base_name: str) -> str:
    # 使用规范化后的完整键生成稳定 ID，不依赖当前行顺序或绝对编号。
    key = "\x1f".join(map(normalize, (first_category, second_category, base_name)))
    digest = hashlib.sha1(key.encode("utf-8")).hexdigest()[:12]
    return f"progression-{digest}"


def find_progression_chains(
    achievements: list[dict[str, Any]], min_members: int = 2
) -> list[tuple[str, list[dict[str, Any]]]]:
    candidates: dict[tuple[str, str, str], list[tuple[int, dict[str, Any]]]] = defaultdict(list)

    for achievement in achievements:
        parsed = parse_tier(achievement.get("名称", ""))
        if parsed is None:
            continue
        base_name, tier = parsed
        key = (
            normalize(achievement.get("第一分类", "")),
            normalize(achievement.get("第二分类", "")),
            base_name,
        )
        candidates[key].append((tier, achievement))

    chains: list[tuple[str, list[dict[str, Any]]]] = []
    for (first, second, base), members in candidates.items():
        # 同一等级出现两次时不自动处理，避免错误覆盖。
        tiers = [tier for tier, _ in members]
        if len(members) < min_members or len(set(tiers)) != len(tiers):
            continue
        ordered = [item for _, item in sorted(members, key=lambda item: item[0])]
        chains.append((make_group_id(first, second, base), ordered))

    chains.sort(key=lambda item: min(int(row.get("绝对编号", 0) or 0) for row in item[1]))
    return chains


def apply_progression_chains(
    achievements: list[dict[str, Any]],
) -> list[tuple[str, list[dict[str, Any]]]]:
    chains = find_progression_chains(achievements)

    # 清理旧版本脚本生成的字段，保证脚本可重复运行。
    for achievement in achievements:
        if str(achievement.get(GROUP_KEY, "")).startswith(PROGRESSION_PREFIX):
            achievement.pop(GROUP_KEY, None)
        achievement.pop("进阶组ID", None)
        achievement.pop("进阶等级", None)

    for chain_id, members in chains:
        for achievement in members:
            # 不覆盖已有的 Wiki 二选一组或手工成就组。
            if not achievement.get(GROUP_KEY):
                achievement[GROUP_KEY] = chain_id

    return chains


def load_rows(path: Path) -> list[dict[str, Any]]:
    with path.open("r", encoding="utf-8-sig") as handle:
        value = json.load(handle)
    if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
        raise ValueError(f"输入文件必须是成就对象数组: {path}")
    return value


def write_rows(path: Path, rows: list[dict[str, Any]]) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(rows, handle, ensure_ascii=False, indent=2)
        handle.write("\n")


def report_lines(chains: list[tuple[str, list[dict[str, Any]]]]) -> list[str]:
    total = sum(len(members) for _, members in chains)
    lines = [f"发现 {len(chains)} 个进阶链，共 {total} 条成就。"]
    for index, (chain_id, members) in enumerate(chains, start=1):
        first = members[0].get("第一分类", "")
        second = members[0].get("第二分类", "")
        lines.append(f"[{index:02d}] {first} / {second} · {chain_id}")
        for member in members:
            parsed = parse_tier(member.get("名称", ""))
            tier = parsed[1] if parsed else "?"
            lines.append(
                f"      {tier}级  {member.get('编号', '')}  "
                f"{member.get('名称', '')}  |  {member.get('描述', '')}"
            )
    return lines


def main() -> int:
    parser = argparse.ArgumentParser(description="标记共用累计进度的成就链")
    parser.add_argument(
        "input",
        nargs="?",
        type=Path,
        default=Path("resources/base_achievements.json"),
        help="输入的成就 JSON（默认 resources/base_achievements.json）",
    )
    parser.add_argument(
        "--write",
        action="store_true",
        help="将推断出的 progression-* 成就组ID写回输入文件；默认只预览",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="输出到另一个 JSON 文件；指定后无需 --write",
    )
    parser.add_argument(
        "--report",
        type=Path,
        help="额外保存一份可读的文本报告",
    )
    args = parser.parse_args()

    input_path = args.input.resolve()
    rows = load_rows(input_path)
    chains = apply_progression_chains(rows)
    lines = report_lines(chains)
    print("\n".join(lines))

    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text("\n".join(lines) + "\n", encoding="utf-8")

    destination = args.output.resolve() if args.output else (input_path if args.write else None)
    if destination is None:
        print("预览模式：未修改任何文件。确认后加 --write 写回。")
        return 0

    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination == input_path:
        backup = input_path.with_suffix(input_path.suffix + ".bak")
        shutil.copy2(input_path, backup)
        print(f"已备份原文件: {backup}")
    write_rows(destination, rows)
    print(f"已写入: {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
