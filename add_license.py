# Copyright (c) 2026 Terrafa Limited. All rights reserved.

from pathlib import Path

LICENSE_LINE = "Copyright (c) 2026 Terrafa Limited. All rights reserved."
PREFIXES = {".cs": "//", ".py": "#"}

LICENSE_FILE_TEXT = """Copyright (c) 2026 Terrafa Limited. All rights reserved.

This software and its source code are the proprietary and confidential
property of Terrafa Limited, a company registered in England and Wales.

No licence, express or implied, is granted to any person to use, copy,
modify, merge, publish, distribute, sublicense, or sell copies of this
software, in whole or in part, without the prior written consent of
Terrafa Limited.

Unauthorised copying, distribution, or use of this software, via any
medium, is strictly prohibited.
"""

def write_license_file() -> None:
    license_path = Path.cwd() / "LICENSE"
    if license_path.exists():
        return
    license_path.write_text(LICENSE_FILE_TEXT, encoding="utf-8")

def add_header(file_path: Path, prefix: str) -> None:
    content = file_path.read_text(encoding="utf-8")
    header = f"{prefix} {LICENSE_LINE}"
    if content.startswith(header):
        return
    file_path.write_text(f"{header}\n\n{content}", encoding="utf-8")

def main() -> None:
    write_license_file()
    for extension, prefix in PREFIXES.items():
        for file_path in Path.cwd().rglob(f"*{extension}"):
            add_header(file_path, prefix)

if __name__ == "__main__":
    main()