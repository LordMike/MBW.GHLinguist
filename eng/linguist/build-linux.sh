#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
manifest_path="$script_dir/native-dependencies.json"
linguist_root="${LINGUIST_ROOT:-$repo_root/extern/linguist}"
native_asset_root="$repo_root/.tmp/artifacts/native/linux-x64"
build_root="$repo_root/.tmp/build/linguist/linux-x64"

fail() {
  echo "$*" >&2
  exit 1
}

require_path() {
  [[ -e "$1" ]] || fail "Required $2 is missing: $1"
}

for command in git ruby gem make cc cmake; do
  command -v "$command" >/dev/null 2>&1 || fail "Required command is unavailable: $command"
done
require_path "$manifest_path" "native dependency manifest"
require_path "$linguist_root/ext/linguist/extconf.rb" "Linguist tokenizer source"
bridge_source="$repo_root/src/MBW.GHLinguist.Native/ruby/ghlinguist/bridge.rb"
require_path "$bridge_source" "GHLinguist Ruby bridge"

manifest_value() {
  ruby -rjson -e 'value = ARGV.shift.split(".").reduce(JSON.parse(File.read(ARGV.shift))) { |item, key| item.fetch(key) }; puts value' "$1" "$manifest_path"
}

ruby_version="$(manifest_value ruby.version)"
ruby_abi_version="$(manifest_value ruby.abiVersion)"
linguist_version="$(manifest_value linguist.version)"
linguist_revision="$(manifest_value linguist.revision)"
[[ "$(ruby -e 'print RUBY_VERSION')" == "$ruby_version" ]] || fail "Expected Ruby $ruby_version."
[[ "$(git -C "$linguist_root" rev-parse HEAD)" == "$linguist_revision" ]] || fail "Expected Linguist revision $linguist_revision."
[[ "$(tr -d '[:space:]' < "$linguist_root/lib/linguist/VERSION")" == "$linguist_version" ]] || fail "Expected Linguist $linguist_version."

ruby_prefix="$(ruby -rrbconfig -e 'print RbConfig::CONFIG.fetch("prefix")')"
require_path "$ruby_prefix/bin/ruby" "Ruby runtime executable"
require_path "$ruby_prefix/lib/ruby" "Ruby standard library"

rm -rf "$build_root" "$native_asset_root"
mkdir -p "$build_root/tokenizer" "$native_asset_root/bin" "$native_asset_root/lib" "$native_asset_root/linguist"

cp -a "$ruby_prefix/bin/ruby" "$native_asset_root/bin/ruby"
shopt -s nullglob
ruby_libraries=("$ruby_prefix/lib"/libruby.so*)
(( ${#ruby_libraries[@]} > 0 )) || fail "Ruby shared runtime library is missing under $ruby_prefix/lib."
cp -a "${ruby_libraries[@]}" "$native_asset_root/lib/"
cp -a "$ruby_prefix/lib/ruby" "$native_asset_root/lib/ruby"

gem_home="$native_asset_root/lib/ruby/gems/$ruby_abi_version"
mkdir -p "$gem_home"
while IFS=$'\t' read -r gem_name gem_version; do
  [[ -n "$gem_name" ]] || continue
  GEM_HOME="$gem_home" GEM_PATH="$gem_home" gem install --no-document --install-dir "$gem_home" "$gem_name" --version "$gem_version"
done < <(ruby -rjson -e 'JSON.parse(File.read(ARGV.fetch(0))).fetch("gems").each { |gem| puts "#{gem.fetch("name")}\t#{gem.fetch("version")}" }' "$manifest_path")

for library in icudata icui18n icuuc; do
  library_path="$(ldconfig -p | awk -v name="lib${library}.so" '$1 ~ ("^" name) { print $NF; exit }')"
  [[ -n "$library_path" ]] || fail "Required ICU library lib${library}.so is unavailable in the Docker image."
  cp -aL "$library_path" "$native_asset_root/lib/"
done

while IFS= read -r path; do
  [[ -n "$path" ]] || continue
  require_path "$linguist_root/$path" "Linguist $path"
  if [[ "$path" == "lib" ]]; then
    cp -a "$linguist_root/$path/." "$native_asset_root/lib/"
  else
    cp -a "$linguist_root/$path" "$native_asset_root/linguist/$path"
  fi
done < <(ruby -rjson -e 'JSON.parse(File.read(ARGV.fetch(0))).fetch("linguist").fetch("paths").each { |path| puts path }' "$manifest_path")
mkdir -p "$native_asset_root/ghlinguist"
cp -a "$bridge_source" "$native_asset_root/ghlinguist/bridge.rb"

tokenizer_source="$build_root/tokenizer"
cp -a "$linguist_root/ext/linguist/." "$tokenizer_source/"
(
  cd "$tokenizer_source"
  ruby extconf.rb
  make --jobs "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1)"
)
require_path "$tokenizer_source/linguist.so" "built Linguist tokenizer"
mkdir -p "$native_asset_root/lib/linguist"
cp -a "$tokenizer_source/linguist.so" "$native_asset_root/lib/linguist/linguist.so"
cp -a "$linguist_root/samples" "$native_asset_root/samples"
RUBYLIB="$native_asset_root/lib" GEM_HOME="$gem_home" GEM_PATH="$gem_home" \
  "$native_asset_root/bin/ruby" "$script_dir/generate-samples.rb" "$native_asset_root/lib/linguist/samples_data.rb"
rm -rf "$native_asset_root/samples"

bridge_build="$build_root/bridge"
cmake -S "$repo_root/src/MBW.GHLinguist.Native" -B "$bridge_build"
cmake --build "$bridge_build" --parallel "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1)"
bridge_path="$(find "$bridge_build" -type f -name 'ghlinguist.so' -print -quit)"
[[ -n "$bridge_path" ]] || fail "ghlinguist bridge build completed without producing ghlinguist.so."
cp -a "$bridge_path" "$native_asset_root/ghlinguist.so"

LD_LIBRARY_PATH="$native_asset_root/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  RUBYLIB="$native_asset_root/lib" \
  GEM_HOME="$gem_home" GEM_PATH="$gem_home" \
  GHL_DEPENDENCY_MANIFEST="$manifest_path" \
  "$native_asset_root/bin/ruby" "$script_dir/validate.rb"

NATIVE_ASSET_ROOT="$native_asset_root" MANIFEST_PATH="$manifest_path" ruby -rjson -rdigest -e '
  root = ENV.fetch("NATIVE_ASSET_ROOT")
  files = Dir.chdir(root) do
    Dir.glob("**/*", File::FNM_DOTMATCH).select { |path| File.file?(path) && path != "provenance.json" }.sort.map do |path|
      { "path" => path, "sha256" => Digest::SHA256.file(path).hexdigest }
    end
  end
  manifest = JSON.parse(File.read(ENV.fetch("MANIFEST_PATH")))
  output = {
    "schemaVersion" => 1,
    "platform" => "linux-x64",
    "manifestSha256" => Digest::SHA256.file(ENV.fetch("MANIFEST_PATH")).hexdigest,
    "rubyVersion" => manifest.fetch("ruby").fetch("version"),
    "linguistVersion" => manifest.fetch("linguist").fetch("version"),
    "linguistRevision" => manifest.fetch("linguist").fetch("revision"),
    "files" => files
  }
  File.write(File.join(root, "provenance.json"), JSON.pretty_generate(output) + "\n")
'

echo "Staged complete Linux native closure: $native_asset_root"
