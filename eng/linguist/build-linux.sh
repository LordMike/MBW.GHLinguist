#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

# shellcheck source=versions.env
source "$script_dir/versions.env"

linguist_root="$repo_root/extern/linguist"
build_root="$repo_root/.tmp/build/linguist/linux-x64"
artifact_root="$repo_root/.tmp/artifacts/linguist/linux-x64"
extension_source="$build_root/extension"

for command in git ruby make cc; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $command" >&2
    exit 1
  fi
done

if [[ ! -f "$linguist_root/ext/linguist/extconf.rb" ]]; then
  echo "Linguist is not checked out at extern/linguist." >&2
  echo "Run: git submodule update --init extern/linguist" >&2
  exit 1
fi

actual_linguist_revision="$(git -C "$linguist_root" rev-parse HEAD)"
if [[ "$actual_linguist_revision" != "$LINGUIST_REVISION" ]]; then
  echo "Expected Linguist revision $LINGUIST_REVISION, found $actual_linguist_revision." >&2
  exit 1
fi

actual_linguist_version="$(tr -d '[:space:]' < "$linguist_root/lib/linguist/VERSION")"
if [[ "$actual_linguist_version" != "$LINGUIST_VERSION" ]]; then
  echo "Expected Linguist $LINGUIST_VERSION, found $actual_linguist_version." >&2
  exit 1
fi

actual_ruby_version="$(ruby -e 'print RUBY_VERSION')"
if [[ "$actual_ruby_version" != "$RUBY_VERSION" ]]; then
  echo "Expected Ruby $RUBY_VERSION, found $actual_ruby_version." >&2
  exit 1
fi

rm -rf "$build_root" "$artifact_root"
mkdir -p "$extension_source" "$artifact_root/lib/linguist"
cp -R "$linguist_root/ext/linguist/." "$extension_source/"
cp -R "$linguist_root/lib/." "$artifact_root/lib/"

(
  cd "$extension_source"
  ruby extconf.rb
  make --jobs "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1)"
)

extension_path="$(find "$extension_source" -maxdepth 1 -type f -name 'linguist.so' -print -quit)"
if [[ -z "$extension_path" ]]; then
  echo "The Linguist extension was not produced." >&2
  exit 1
fi

cp "$extension_path" "$artifact_root/lib/linguist/linguist.so"

RUBYLIB="$artifact_root/lib" ruby "$script_dir/validate.rb"

echo "Linguist Linux artifacts: $artifact_root"
