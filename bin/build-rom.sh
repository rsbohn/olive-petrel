#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
romtool="$root_dir/tools/romtool/romtool.csproj"
out_rom="$root_dir/lib.rom"
out_sym="$root_dir/lib.sym"

files=("$@")
if [[ ${#files[@]} -eq 0 ]]; then
  mapfile -t files < <(ls "$root_dir"/lib/*.pa 2>/dev/null || true)
fi

if [[ ${#files[@]} -eq 0 ]]; then
  echo "No library routines found in $root_dir/lib. Provide files explicitly." >&2
  exit 2
fi

dotnet run --project "$romtool" -- build-lib \
  --out "$out_rom" \
  --sym "$out_sym" \
  --files "${files[@]}"

echo "lib.rom -> $out_rom"
echo "lib.sym -> $out_sym"
