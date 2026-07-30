# Copyright (c) 2026 Terrafa Limited. All rights reserved.

from pathlib import Path

LICENSE = "Copyright (c) 2026 Terrafa Limited. All rights reserved."
PREFIXES = {".cs": "//", ".py": "#"}

def add_header(file_path: Path, prefix: str) -> None:
    content = file_path.read_text(encoding="utf-8")
    header = f"{prefix} {LICENSE}"
    if content.startswith(header):
        return
    file_path.write_text(f"{header}\n\n{content}", encoding="utf-8")

def main() -> None:
    for extension, prefix in PREFIXES.items():
        for file_path in Path.cwd().rglob(f"*{extension}"):
            add_header(file_path, prefix)

if __name__ == "__main__":
    main()