#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: bin/link.sh <app.pa>" >&2
  exit 2
fi

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
romtool="$root_dir/tools/romtool/romtool.csproj"
lib_rom="$root_dir/lib.rom"
lib_sym="$root_dir/lib.sym"
app_path="$1"

if [[ ! -f "$lib_rom" || ! -f "$lib_sym" ]]; then
  echo "Missing $lib_rom or $lib_sym. Run bin/build-rom.sh first." >&2
  exit 2
fi

app_base="$(basename "$app_path")"
app_dir="$(cd "$(dirname "$app_path")" && pwd)"
out_rom="$app_dir/${app_base%.pa}.rom"

dotnet run --project "$romtool" -- link \
  --lib "$lib_rom" \
  --sym "$lib_sym" \
  --app "$app_path" \
  --out "$out_rom"

echo "app.rom -> $out_rom"
