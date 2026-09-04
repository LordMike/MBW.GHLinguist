#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
native_asset_root="${1:-$repo_root/.tmp/artifacts/native/linux-x64}"
header_path="${2:-$repo_root/src/MBW.GHLinguist.Native/include/ghlinguist.h}"
native_library="$native_asset_root/ghlinguist.so"

for command in awk diff grep ldd nm readelf realpath sed sort tr wc; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command is unavailable: $command" >&2; exit 1; }
done
[[ -f "$native_library" ]] || { echo "The Linux native bridge is missing: $native_library" >&2; exit 1; }
[[ -f "$header_path" ]] || { echo "The public C ABI header is missing: $header_path" >&2; exit 1; }

machine="$(readelf -h "$native_library")"
grep -q 'Class:[[:space:]]*ELF64' <<<"$machine" || { echo 'ghlinguist.so is not an ELF64 image.' >&2; exit 1; }
grep -q 'Machine:[[:space:]]*Advanced Micro Devices X86-64' <<<"$machine" || { echo 'ghlinguist.so is not an x86-64 ELF image.' >&2; exit 1; }

expected_exports="$(sed -nE 's/.*GHL_CALL[[:space:]]+(ghl_[A-Za-z0-9_]+)[[:space:]]*\(.*/\1/p' "$header_path" | sort -u)"
actual_exports="$(nm -D --defined-only --format=posix "$native_library" | awk '$1 ~ /^ghl_/ { print $1 }' | sort -u)"
[[ -n "$expected_exports" ]] || { echo 'The public header does not declare any exported ghl_* functions.' >&2; exit 1; }
if ! diff -u <(printf '%s\n' "$expected_exports") <(printf '%s\n' "$actual_exports"); then
  echo 'Linux bridge exports do not match the public C ABI.' >&2
  exit 1
fi

native_asset_root="$(realpath "$native_asset_root")"
ldd_output="$(LD_LIBRARY_PATH="$native_asset_root/lib:$native_asset_root" ldd "$native_library" 2>&1)"
if grep -q 'not found' <<<"$ldd_output"; then
  echo "The Linux bridge has unresolved dependencies: $ldd_output" >&2
  exit 1
fi
grep -q 'libruby\.so\.4\.0' <<<"$ldd_output" || { echo 'ghlinguist.so does not depend on the pinned CRuby runtime library.' >&2; exit 1; }

while read -r dependency _ resolved _; do
  [[ -n "${dependency:-}" && "${resolved:-}" == /* ]] || continue
  case "$dependency" in
    libc.so.*|libm.so.*|libdl.so.*|libpthread.so.*|libresolv.so.*|librt.so.*|libutil.so.*)
      continue
      ;;
  esac
  resolved="$(realpath "$resolved")"
  [[ "$resolved" == "$native_asset_root/"* ]] || {
    echo "Linux bridge dependency resolved outside the native closure: $dependency => $resolved" >&2
    exit 1
  }
done <<<"$ldd_output"

export_count="$(wc -l <<<"$actual_exports" | tr -d '[:space:]')"
dependency_count="$(grep -c '=>' <<<"$ldd_output" || true)"
echo "Validated $export_count Linux C ABI exports and $dependency_count resolved native dependencies."
