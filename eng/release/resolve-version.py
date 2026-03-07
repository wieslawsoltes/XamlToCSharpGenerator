#!/usr/bin/env python3
from __future__ import annotations

import argparse
import xml.etree.ElementTree as ET
from pathlib import Path


def read_version_parts(path: Path) -> tuple[str, str]:
    tree = ET.parse(path)
    root = tree.getroot()
    prefix = ""
    suffix = ""
    for group in root.findall("PropertyGroup"):
        for child in group:
            value = (child.text or "").strip()
            if child.tag == "VersionPrefix" and value:
                prefix = value
            elif child.tag == "VersionSuffix" and value:
                suffix = value

    if not prefix:
        prefix = "1.0.0"

    return prefix, suffix


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--props", default="Directory.Build.props")
    parser.add_argument("--ci-run-number")
    args = parser.parse_args()

    prefix, suffix = read_version_parts(Path(args.props))

    if args.ci_run_number:
        base = prefix
        if suffix:
            base = f"{base}-{suffix}"
        print(f"{base}-ci.{args.ci_run_number}")
        return 0

    if suffix:
        print(f"{prefix}-{suffix}")
        return 0

    print(prefix)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
