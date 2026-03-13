#!/usr/bin/env python3
"""
Print the slowest tests and slowest test classes from a Visual Studio TRX file.

Usage:
  python3 tools/report_slow_trx.py path/to/results.trx
  python3 tools/report_slow_trx.py path/to/results.trx --top 25
"""

from __future__ import annotations

import argparse
import collections
import pathlib
import xml.etree.ElementTree as ET


TRX_NS = {"trx": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def parse_duration_seconds(raw: str | None) -> float:
    if not raw:
        return 0.0

    parts = raw.split(":")
    if len(parts) != 3:
        return 0.0

    hours = int(parts[0])
    minutes = int(parts[1])
    seconds = float(parts[2])
    return hours * 3600 + minutes * 60 + seconds


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("trx", type=pathlib.Path)
    parser.add_argument("--top", type=int, default=20)
    args = parser.parse_args()

    root = ET.parse(args.trx).getroot()
    results = root.findall(".//trx:UnitTestResult", TRX_NS)

    test_rows: list[tuple[float, str, str]] = []
    class_totals: dict[str, list[float | int]] = collections.defaultdict(lambda: [0.0, 0])

    for result in results:
        duration = parse_duration_seconds(result.get("duration"))
        name = result.get("testName", "<unknown>")
        class_name = name.rsplit(".", 1)[0] if "." in name else "<global>"
        test_rows.append((duration, name, class_name))
        class_totals[class_name][0] += duration
        class_totals[class_name][1] += 1

    test_rows.sort(key=lambda item: item[0], reverse=True)
    class_rows = sorted(
        ((float(total), int(count), class_name) for class_name, (total, count) in class_totals.items()),
        key=lambda item: item[0],
        reverse=True,
    )

    print(f"TRX: {args.trx}")
    print(f"Results: {len(test_rows)}")
    print()
    print(f"Top {min(args.top, len(test_rows))} slowest tests")
    for duration, name, _ in test_rows[: args.top]:
        print(f"{duration:8.3f}s  {name}")

    print()
    print(f"Top {min(args.top, len(class_rows))} slowest classes")
    for total, count, class_name in class_rows[: args.top]:
        average = total / count if count else 0.0
        print(f"{total:8.3f}s  {count:4d} tests  {average:7.3f}s avg  {class_name}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
