#!/usr/bin/env python3
"""Fail if the UI draws a character no embedded font can render.

On the desktop this did not matter: when Verdana came up short the OS quietly supplied a
glyph from somewhere else in the system font stack. A browser has nothing behind the font,
so an uncovered character renders as a tofu box. Every glyph the app draws has to be in a
font we ship.

Reads the cmap tables directly so it runs anywhere python3 does, with no dependencies.

    python3 tools/check-glyphs.py
"""

from __future__ import annotations

import pathlib
import struct
import sys
import unicodedata

ROOT = pathlib.Path(__file__).resolve().parent.parent
FONT_DIR = ROOT / "src/Terrafa.Continuum.Frontend.Ui/Assets/Fonts"
SOURCE_DIR = ROOT / "src"
SOURCE_SUFFIXES = {".cs", ".axaml"}
SKIP_DIRS = {"obj", "bin"}


def _table_offsets(data: bytes) -> dict[str, int]:
    """Map table tag -> file offset from the sfnt header."""
    num_tables = struct.unpack_from(">H", data, 4)[0]
    tables = {}
    for i in range(num_tables):
        tag, _checksum, offset, _length = struct.unpack_from(">4sIII", data, 12 + i * 16)
        tables[tag.decode("latin-1")] = offset
    return tables


def _parse_format4(data: bytes, off: int) -> set[int]:
    """Segment-mapped subtable. Holes inside a segment map to glyph 0, so resolve
    the glyph id rather than assuming every code in a segment is present."""
    seg_count = struct.unpack_from(">H", data, off + 6)[0] // 2
    ends = off + 14
    starts = ends + seg_count * 2 + 2
    deltas = starts + seg_count * 2
    range_offsets = deltas + seg_count * 2

    covered: set[int] = set()
    for seg in range(seg_count):
        end = struct.unpack_from(">H", data, ends + seg * 2)[0]
        start = struct.unpack_from(">H", data, starts + seg * 2)[0]
        delta = struct.unpack_from(">h", data, deltas + seg * 2)[0]
        range_offset = struct.unpack_from(">H", data, range_offsets + seg * 2)[0]
        if start == 0xFFFF:
            continue
        for code in range(start, end + 1):
            if range_offset == 0:
                glyph = (code + delta) & 0xFFFF
            else:
                idx = range_offsets + seg * 2 + range_offset + (code - start) * 2
                if idx + 2 > len(data):
                    continue
                glyph = struct.unpack_from(">H", data, idx)[0]
                if glyph:
                    glyph = (glyph + delta) & 0xFFFF
            if glyph:
                covered.add(code)
    return covered


def _parse_format12(data: bytes, off: int) -> set[int]:
    """Segmented coverage, used for anything above the BMP."""
    num_groups = struct.unpack_from(">I", data, off + 12)[0]
    covered: set[int] = set()
    for i in range(num_groups):
        start, end, _start_glyph = struct.unpack_from(">III", data, off + 16 + i * 12)
        covered.update(range(start, end + 1))
    return covered


def font_coverage(path: pathlib.Path) -> set[int]:
    data = path.read_bytes()
    cmap = _table_offsets(data).get("cmap")
    if cmap is None:
        return set()
    num_subtables = struct.unpack_from(">H", data, cmap + 2)[0]
    covered: set[int] = set()
    for i in range(num_subtables):
        _platform, _encoding, sub_off = struct.unpack_from(">HHI", data, cmap + 4 + i * 8)
        off = cmap + sub_off
        fmt = struct.unpack_from(">H", data, off)[0]
        if fmt == 4:
            covered |= _parse_format4(data, off)
        elif fmt == 12:
            covered |= _parse_format12(data, off)
    return covered


def required_glyphs() -> dict[int, list[str]]:
    """Every non-ASCII character appearing in a source file, and where it came from."""
    found: dict[int, list[str]] = {}
    for path in sorted(SOURCE_DIR.rglob("*")):
        if path.suffix not in SOURCE_SUFFIXES or SKIP_DIRS & set(path.parts):
            continue
        for ch in path.read_text(encoding="utf-8"):
            if ord(ch) > 127:
                where = found.setdefault(ord(ch), [])
                rel = str(path.relative_to(ROOT))
                if rel not in where:
                    where.append(rel)
    return found


def main() -> int:
    fonts = sorted(p for p in FONT_DIR.iterdir() if p.suffix in {".ttf", ".otf"})
    if not fonts:
        print(f"no fonts found in {FONT_DIR}", file=sys.stderr)
        return 1

    coverage = {p.name: font_coverage(p) for p in fonts}
    union: set[int] = set().union(*coverage.values())
    required = required_glyphs()

    missing = {c: w for c, w in required.items() if c not in union}
    for name, covered in coverage.items():
        answered = len([c for c in required if c in covered])
        print(f"  {name:34} {len(covered):6} glyphs, answers {answered:3} of the app's")

    print(f"\n{len(required)} non-ASCII characters used, {len(missing)} uncovered")
    if not missing:
        return 0

    print("\nThese would render as tofu in the browser:", file=sys.stderr)
    for code, where in sorted(missing.items()):
        name = unicodedata.name(chr(code), "?")
        print(f"  U+{code:04X}  {chr(code)}  {name}", file=sys.stderr)
        for w in where:
            print(f"            {w}", file=sys.stderr)
    print(
        "\nAdd a font covering these to Assets/Fonts and list it in AppFonts.Options,"
        "\nor widen the subset ranges in tools/subset-fonts.sh.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
