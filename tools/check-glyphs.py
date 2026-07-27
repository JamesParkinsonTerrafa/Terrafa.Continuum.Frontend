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

import hashlib
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


def _gid_lookup(data: bytes, off: int, codes: set[int]) -> dict[int, int]:
    """Resolve codepoint -> glyph id through one cmap subtable. 0 means absent."""
    fmt = struct.unpack_from(">H", data, off)[0]
    out: dict[int, int] = {}
    if fmt == 4:
        seg_count = struct.unpack_from(">H", data, off + 6)[0] // 2
        ends = off + 14
        starts = ends + seg_count * 2 + 2
        deltas = starts + seg_count * 2
        range_offsets = deltas + seg_count * 2
        for code in codes:
            for seg in range(seg_count):
                end = struct.unpack_from(">H", data, ends + seg * 2)[0]
                start = struct.unpack_from(">H", data, starts + seg * 2)[0]
                if not start <= code <= end:
                    continue
                delta = struct.unpack_from(">h", data, deltas + seg * 2)[0]
                range_offset = struct.unpack_from(">H", data, range_offsets + seg * 2)[0]
                if range_offset == 0:
                    out[code] = (code + delta) & 0xFFFF
                else:
                    idx = range_offsets + seg * 2 + range_offset + (code - start) * 2
                    if idx + 2 <= len(data):
                        raw = struct.unpack_from(">H", data, idx)[0]
                        out[code] = 0 if raw == 0 else (raw + delta) & 0xFFFF
                break
    elif fmt == 12:
        num_groups = struct.unpack_from(">I", data, off + 12)[0]
        for code in codes:
            for i in range(num_groups):
                start, end, start_glyph = struct.unpack_from(">III", data, off + 16 + i * 12)
                if start <= code <= end:
                    out[code] = start_glyph + (code - start)
                    break
    return out


def _read_index(data: bytes, pos: int) -> tuple[list[bytes], int]:
    """CFF INDEX -> (items, offset just past the structure)."""
    count = struct.unpack_from(">H", data, pos)[0]
    if count == 0:
        return [], pos + 2
    off_size = data[pos + 2]
    base = pos + 3
    offsets = []
    for i in range(count + 1):
        value = 0
        for byte in data[base + i * off_size: base + (i + 1) * off_size]:
            value = (value << 8) | byte
        offsets.append(value)
    data_base = base + (count + 1) * off_size - 1
    items = [data[data_base + offsets[i]: data_base + offsets[i + 1]] for i in range(count)]
    return items, data_base + offsets[-1]


def _cff_charstrings_offset(top_dict: bytes) -> int | None:
    """Operator 17 in the Top DICT holds the CharStrings offset."""
    operands: list[int] = []
    i = 0
    while i < len(top_dict):
        b0 = top_dict[i]
        if b0 <= 21:
            op = b0
            i += 1
            if b0 == 12:
                op = 1200 + top_dict[i]
                i += 1
            if op == 17 and operands:
                return operands[0]
            operands = []
        elif b0 == 28:
            operands.append(struct.unpack_from(">h", top_dict, i + 1)[0]); i += 3
        elif b0 == 29:
            operands.append(struct.unpack_from(">i", top_dict, i + 1)[0]); i += 5
        elif b0 == 30:  # real — operand value is irrelevant here, just skip it
            i += 1
            while i < len(top_dict) and not (top_dict[i] & 0x0F == 0x0F or top_dict[i] >> 4 == 0x0F):
                i += 1
            i += 1
            operands.append(0)
        elif 32 <= b0 <= 246:
            operands.append(b0 - 139); i += 1
        elif 247 <= b0 <= 250:
            operands.append((b0 - 247) * 256 + top_dict[i + 1] + 108); i += 2
        elif 251 <= b0 <= 254:
            operands.append(-((b0 - 251) * 256) - top_dict[i + 1] - 108); i += 2
        else:
            i += 1
    return None


def outline_digests(path: pathlib.Path, codes: set[int]) -> dict[int, str]:
    """codepoint -> hash of the outline the font actually draws for it.

    Covers both flavours of outline: TrueType glyf/loca and CFF CharStrings. A cmap entry
    only promises *a* glyph; this is what checks *which* glyph.
    """
    data = path.read_bytes()
    tables = _table_offsets(data)
    cmap = tables.get("cmap")
    if cmap is None:
        return {}

    gids: dict[int, int] = {}
    num_subtables = struct.unpack_from(">H", data, cmap + 2)[0]
    for i in range(num_subtables):
        _platform, _encoding, sub_off = struct.unpack_from(">HHI", data, cmap + 4 + i * 8)
        for code, gid in _gid_lookup(data, cmap + sub_off, codes).items():
            if gid:
                gids.setdefault(code, gid)

    charstrings: list[bytes] = []
    if "CFF " in tables:
        cff = tables["CFF "]
        pos = cff + data[cff + 2]
        _names, pos = _read_index(data, pos)
        top_dicts, pos = _read_index(data, pos)
        cs_offset = _cff_charstrings_offset(top_dicts[0]) if top_dicts else None
        if cs_offset is not None:
            charstrings, _ = _read_index(data, cff + cs_offset)

    def truetype_outline(gid: int) -> bytes:
        loca, glyf = tables.get("loca"), tables.get("glyf")
        if loca is None or glyf is None:
            return b""
        long_form = struct.unpack_from(">h", data, tables["head"] + 50)[0]
        if long_form:
            start, end = struct.unpack_from(">II", data, loca + gid * 4)
        else:
            start, end = (v * 2 for v in struct.unpack_from(">HH", data, loca + gid * 2))
        return data[glyf + start: glyf + end]

    digests: dict[int, str] = {}
    for code, gid in gids.items():
        raw = charstrings[gid] if charstrings and gid < len(charstrings) else truetype_outline(gid)
        if raw:
            digests[code] = hashlib.sha1(raw).hexdigest()
    return digests


def _equivalent(a: str, b: str) -> bool:
    """True when two characters are meant to be the same letter (µ/μ, ﬁ/fi)."""
    return unicodedata.normalize("NFKC", a).casefold() == unicodedata.normalize("NFKC", b).casefold()


def wrong_case_outlines(path: pathlib.Path, required: set[int]) -> list[tuple[int, int]]:
    """Characters the font draws using a glyph belonging to the *other* case.

    Bergoom ships a U+03BC whose charstring is byte-identical to Latin capital M, so
    every mu in the app rendered as an M while cmap coverage still looked perfect.
    Greek and Latin capitals legitimately share outlines (Α/A, Μ/M), so only a case
    disagreement counts — that is the signature of a substituted glyph, not a homoglyph.
    """
    probe = set(required) | set(range(0x20, 0x7F))
    digests = outline_digests(path, probe)
    by_digest: dict[str, list[int]] = {}
    for code, digest in digests.items():
        by_digest.setdefault(digest, []).append(code)

    hits: list[tuple[int, int]] = []
    for codes in by_digest.values():
        if len(codes) < 2:
            continue
        for code in sorted(c for c in codes if c in required):
            char = chr(code)
            for other in sorted(codes):
                if other == code:
                    continue
                twin = chr(other)
                same_case = char.isupper() == twin.isupper() and char.islower() == twin.islower()
                if not same_case and not _equivalent(char, twin):
                    hits.append((code, other))
                    break
    return hits


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

    substituted = {p.name: wrong_case_outlines(p, set(required)) for p in fonts}
    substituted = {name: hits for name, hits in substituted.items() if hits}
    if substituted:
        sys.stdout.flush()
        print("\nThese are covered but drawn with the wrong glyph:", file=sys.stderr)
        for name, hits in substituted.items():
            for code, twin in hits:
                label = unicodedata.name(chr(code), "?")
                twin_label = unicodedata.name(chr(twin), "?")
                print(f"  {name}: U+{code:04X} {chr(code)} {label}", file=sys.stderr)
                print(f"      draws the outline of U+{twin:04X} {chr(twin)} {twin_label}", file=sys.stderr)
                for w in required.get(code, []):
                    print(f"      used in {w}", file=sys.stderr)
        print(
            "\nThe cmap promises the character but the outline belongs to another letter."
            "\nUse a codepoint the font draws correctly, or drop the mapping so a fallback answers.",
            file=sys.stderr,
        )

    if not missing:
        return 1 if substituted else 0

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
