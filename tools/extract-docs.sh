#!/usr/bin/env bash
# Extract plain text from the ShopFlow .docx source documents into docs/source/.
# Re-runnable on any machine that has bash + unzip + python3 (or python).
# Works on Linux, macOS, Windows (via Git Bash / WSL).

set -euo pipefail

# Resolve repo root (parent of this script's dir).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DEST="$ROOT/docs/source"
mkdir -p "$DEST"

# Pick a python interpreter.
if command -v python3 >/dev/null 2>&1; then
  PY=python3
elif command -v python >/dev/null 2>&1; then
  PY=python
else
  echo "ERROR: python3 (or python) is required to extract .docx text." >&2
  exit 1
fi

if ! command -v unzip >/dev/null 2>&1; then
  echo "ERROR: unzip is required (Git Bash on Windows ships with it)." >&2
  exit 1
fi

extract_one() {
  local docx="$1"
  local out="$2"
  local tmp
  tmp="$(mktemp -d)"
  trap "rm -rf '$tmp'" RETURN

  unzip -o -q "$docx" -d "$tmp"

  if [[ ! -f "$tmp/word/document.xml" ]]; then
    echo "ERROR: $docx does not contain word/document.xml" >&2
    return 1
  fi

  "$PY" - "$tmp/word/document.xml" "$out" <<'PYEOF'
import re, html, sys
src, dst = sys.argv[1], sys.argv[2]
with open(src, "r", encoding="utf-8") as fh:
    x = fh.read()
x = re.sub(r"</w:p>", "\n", x)
x = re.sub(r"<w:br[^/]*/>", "\n", x)
x = re.sub(r"<w:tab[^/]*/>", "\t", x)
x = re.sub(r"<[^>]+>", "", x)
x = html.unescape(x)
x = re.sub(r"\n\n+", "\n\n", x)
with open(dst, "w", encoding="utf-8") as fh:
    fh.write(x)
print(f"  wrote {dst} ({len(x)} chars)")
PYEOF
}

shopt -s nullglob
docx_files=( "$ROOT"/*.docx )
shopt -u nullglob

if [[ ${#docx_files[@]} -eq 0 ]]; then
  echo "No .docx files found at $ROOT/" >&2
  exit 1
fi

for docx in "${docx_files[@]}"; do
  base="$(basename "$docx")"
  txt_name="${base%.docx}.txt"
  out="$DEST/$txt_name"
  echo "Extracting $base ..."
  extract_one "$docx" "$out"
done

echo "Done. Extracted text under $DEST/"
