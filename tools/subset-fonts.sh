#!/usr/bin/env bash
# Regenerates the two fallback fonts in Assets/Fonts.
#
# Bergoom covers the Latin text but not every symbol these views draw. DejaVu Sans and
# Noto Sans Math fill the gaps. Shipping either one whole would cost ~1.7 MB in a WASM
# bundle to answer sixteen characters, so both are subset to the symbol blocks the app
# draws from — with enough headroom that reaching for a new arrow or operator does not
# mean regenerating anything.
#
# Run this after widening the ranges, then re-run tools/check-glyphs.py.
#
# Requires fonttools:  pip install fonttools
set -euo pipefail

cd "$(dirname "$0")/.."
FONTS=src/Terrafa.Continuum.Frontend.Ui/Assets/Fonts
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

DEJAVU_VERSION=2.37
NOTO_MATH_URL=https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSansMath/hinted/ttf/NotoSansMath-Regular.ttf

# Latin-1, Greek, phonetic modifiers, punctuation, super/subscripts, arrows, operators,
# misc technical (floor/ceiling), box drawing, geometric shapes, misc symbols, dingbats, braille.
DEJAVU_RANGES="U+00A0-00FF,U+0370-03FF,U+1D2C-1D6A,U+2000-206F,U+2070-209F,\
U+2190-21FF,U+2200-22FF,U+2300-23FF,U+2500-257F,U+25A0-25FF,U+2600-26FF,U+2700-27BF,U+2800-28FF"

# Mathematical Alphanumeric Symbols — the script capitals in the transfer-function notation.
NOTO_MATH_RANGES="U+1D400-1D7FF"

echo "fetching sources into $WORK"
curl -sL -o "$WORK/dejavu.zip" \
  "https://github.com/dejavu-fonts/dejavu-fonts/releases/download/version_${DEJAVU_VERSION//./_}/dejavu-fonts-ttf-${DEJAVU_VERSION}.zip"
unzip -qo "$WORK/dejavu.zip" -d "$WORK"
curl -sL -o "$WORK/NotoSansMath.ttf" "$NOTO_MATH_URL"

echo "subsetting"
pyftsubset "$WORK/dejavu-fonts-ttf-${DEJAVU_VERSION}/ttf/DejaVuSans.ttf" \
  --output-file="$FONTS/DejaVuSans-Symbols.ttf" \
  --unicodes="$DEJAVU_RANGES" --name-IDs='*' --layout-features='' --drop-tables+=DSIG

pyftsubset "$WORK/NotoSansMath.ttf" \
  --output-file="$FONTS/NotoSansMath-Alphanumeric.ttf" \
  --unicodes="$NOTO_MATH_RANGES" --name-IDs='*' --layout-features='' --drop-tables+=DSIG

cp "$WORK/dejavu-fonts-ttf-${DEJAVU_VERSION}/LICENSE" "$FONTS/DejaVuSans-LICENSE.txt"

ls -l "$FONTS"
echo
echo "now run: python3 tools/check-glyphs.py"
