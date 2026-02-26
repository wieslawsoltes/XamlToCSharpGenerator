#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[2]
BINDING_DIR = REPO_ROOT / "src" / "XamlToCSharpGenerator.Avalonia" / "Binding"
SOURCE_PATH = BINDING_DIR / "AvaloniaSemanticBinder.cs"

SPLITS = [
    (
        "AvaloniaSemanticBinder.StylesTemplates.cs",
        "private static ImmutableArray<ResolvedStyleDefinition> BindStyles(",
    ),
    (
        "AvaloniaSemanticBinder.BindingSemantics.cs",
        "private static bool TryParseBindingMarkup(string value, out BindingMarkup bindingMarkup)",
    ),
    (
        "AvaloniaSemanticBinder.TypeResolution.cs",
        "private static INamedTypeSymbol? ResolveTypeFromTypeExpression(",
    ),
]


def find_line_index(lines: list[str], needle: str) -> int:
    for index, line in enumerate(lines):
        if needle in line:
            return index
    return -1


def main() -> int:
    if not SOURCE_PATH.exists():
        print(f"Source file not found: {SOURCE_PATH}", file=sys.stderr)
        return 1

    lines = SOURCE_PATH.read_text(encoding="utf-8").splitlines(keepends=True)

    class_decl_index = find_line_index(
        lines,
        "public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder",
    )
    if class_decl_index < 0:
        print("Class declaration not found.", file=sys.stderr)
        return 1

    class_open_index = -1
    for index in range(class_decl_index, min(class_decl_index + 10, len(lines))):
        if lines[index].strip() == "{":
            class_open_index = index
            break

    if class_open_index < 0:
        print("Class opening brace not found.", file=sys.stderr)
        return 1

    class_close_index = -1
    for index in range(len(lines) - 1, class_open_index, -1):
        if lines[index].strip() == "}":
            class_close_index = index
            break

    if class_close_index < 0:
        print("Class closing brace not found.", file=sys.stderr)
        return 1

    anchor_indexes: list[int] = []
    for _, anchor in SPLITS:
        index = find_line_index(lines, anchor)
        if index < 0:
            split_targets_exist = all((BINDING_DIR / file_name).exists() for file_name, _ in SPLITS)
            if split_targets_exist:
                print("Split anchors are not present. Binder appears to be already split; no changes applied.")
                return 0

            print(f"Split anchor not found: {anchor}", file=sys.stderr)
            return 1
        anchor_indexes.append(index)

    if not anchor_indexes == sorted(anchor_indexes):
        print("Split anchors are out of order.", file=sys.stderr)
        return 1

    header = "".join(lines[: class_open_index + 1])
    class_footer = "".join(lines[class_close_index:])

    split_ranges: list[tuple[str, int, int]] = []
    for idx, (file_name, _) in enumerate(SPLITS):
        start = anchor_indexes[idx]
        end = anchor_indexes[idx + 1] if idx + 1 < len(anchor_indexes) else class_close_index
        split_ranges.append((file_name, start, end))

    for file_name, start, end in split_ranges:
        chunk = "".join(lines[start:end]).strip("\n")
        out_text = (
            header
            + "\n"
            + chunk
            + "\n"
            + class_footer
        )
        (BINDING_DIR / file_name).write_text(out_text, encoding="utf-8")

    core_start = class_open_index + 1
    core_end = anchor_indexes[0]
    core_body = "".join(lines[core_start:core_end]).rstrip("\n")

    core_text = (
        header
        + "\n"
        + core_body
        + "\n"
        + class_footer
    )
    SOURCE_PATH.write_text(core_text, encoding="utf-8")

    print("Split completed:")
    for file_name, start, end in split_ranges:
        print(f"  - {file_name}: lines {start + 1}-{end}")
    print(f"  - AvaloniaSemanticBinder.cs core: lines {core_start + 1}-{core_end}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
