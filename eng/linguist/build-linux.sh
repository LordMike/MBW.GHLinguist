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

for command in git ruby gem make cc cmake ldd patchelf readelf; do
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
ruby_include_dir="$(ruby -rrbconfig -e 'print RbConfig::CONFIG.fetch("rubyhdrdir")')"
ruby_arch_include_dir="$(ruby -rrbconfig -e 'print RbConfig::CONFIG.fetch("rubyarchhdrdir")')"
ruby_shared_library="$(ruby -rrbconfig -e 'print File.join(RbConfig::CONFIG.fetch("libdir"), RbConfig::CONFIG.fetch("LIBRUBY_SO"))')"
require_path "$ruby_prefix/bin/ruby" "Ruby runtime executable"
require_path "$ruby_prefix/lib/ruby" "Ruby standard library"
require_path "$ruby_include_dir/ruby.h" "Ruby public headers"
require_path "$ruby_arch_include_dir/ruby/config.h" "Ruby architecture headers"
require_path "$ruby_shared_library" "Ruby shared runtime library"

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
find "$gem_home/gems" -mindepth 2 -maxdepth 2 -type d -name ext -prune -exec rm -rf {} +

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
classifier_sha256="$(ruby -rdigest -e 'print Digest::SHA256.file(ARGV.fetch(0)).hexdigest' "$native_asset_root/lib/linguist/samples_data.rb")"

bridge_build="$build_root/bridge"
cmake -S "$repo_root/src/MBW.GHLinguist.Native" -B "$bridge_build" \
  -DGHL_ENABLE_RUBY_EMBEDDING=ON \
  -DGHL_BUILD_SMOKE=ON \
  -DGHL_RUBY_ROOT="$ruby_prefix" \
  -DGHL_RUBY_INCLUDE_DIR="$ruby_include_dir" \
  -DGHL_RUBY_ARCH_INCLUDE_DIR="$ruby_arch_include_dir" \
  -DGHL_RUBY_LIBRARY="$ruby_shared_library" \
  -DGHL_LINGUIST_REVISION="$linguist_revision" \
  -DGHL_CLASSIFIER_SHA256="$classifier_sha256"
cmake --build "$bridge_build" --parallel "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1)"
bridge_path="$(find "$bridge_build" -type f -name 'libghlinguist.so' -print -quit)"
[[ -n "$bridge_path" ]] || fail "ghlinguist bridge build completed without producing libghlinguist.so."
cp -a "$bridge_path" "$native_asset_root/ghlinguist.so"

mapfile -d '' dependency_queue < <(find "$native_asset_root" -type f \( -name '*.so' -o -name '*.so.*' \) -print0)
dependency_queue+=("$native_asset_root/bin/ruby")
declare -A inspected_dependencies=()
dependency_index=0
while (( dependency_index < ${#dependency_queue[@]} )); do
  elf_path="${dependency_queue[$dependency_index]}"
  ((dependency_index += 1))
  [[ -z "${inspected_dependencies[$elf_path]:-}" ]] || continue
  inspected_dependencies["$elf_path"]=1
  readelf -h "$elf_path" >/dev/null 2>&1 || continue

  ldd_output="$(LD_LIBRARY_PATH="$native_asset_root/lib" ldd "$elf_path" 2>&1)"
  if grep -q 'not found' <<<"$ldd_output"; then
    fail "Unresolved ELF dependency for $elf_path: $ldd_output"
  fi
  while IFS= read -r dependency; do
    [[ -n "$dependency" ]] || continue
    dependency_name="$(basename "$dependency")"
    case "$dependency_name" in
      libc.so.*|libm.so.*|libdl.so.*|libpthread.so.*|libresolv.so.*|librt.so.*|libutil.so.*|ld-linux*.so.*|linux-vdso.so.*)
        continue
        ;;
    esac
    destination="$native_asset_root/lib/$dependency_name"
    if [[ ! -e "$destination" ]]; then
      cp -aL "$dependency" "$destination"
      dependency_queue+=("$destination")
    fi
  done < <(awk '$2 == "=>" && $3 ~ /^\// { print $3 }' <<<"$ldd_output")
done

find "$native_asset_root" -type f -name '.*' -delete
while IFS= read -r -d '' link_path; do
  if [[ ! -e "$link_path" ]]; then
    rm "$link_path"
    continue
  fi
  materialized_path="${link_path}.materialized"
  cp -aL "$link_path" "$materialized_path"
  rm "$link_path"
  mv "$materialized_path" "$link_path"
done < <(find "$native_asset_root" -type l -print0)

while IFS= read -r -d '' elf_path; do
  if ! readelf -h "$elf_path" >/dev/null 2>&1; then
    continue
  fi
  elf_directory="$(dirname "$elf_path")"
  relative_lib="$(realpath --relative-to="$elf_directory" "$native_asset_root/lib")"
  if [[ "$relative_lib" == "." ]]; then
    rpath='$ORIGIN'
  else
    rpath="\$ORIGIN/$relative_lib"
  fi
  patchelf --set-rpath "$rpath" "$elf_path"
done < <(find "$native_asset_root" -type f \( -name '*.so' -o -name '*.so.*' \) -print0)

smoke_path="$(find "$bridge_build" -type f -name 'ghlinguist_smoke' -print -quit)"
[[ -n "$smoke_path" ]] || fail "ghlinguist smoke build completed without producing ghlinguist_smoke."
mkdir -p "$native_asset_root/smoke"
cp -a "$smoke_path" "$native_asset_root/smoke/ghlinguist_smoke"
ln -s ../ghlinguist.so "$native_asset_root/smoke/libghlinguist.so"
ln -s ghlinguist.so "$native_asset_root/libghlinguist.so"
"$native_asset_root/smoke/ghlinguist_smoke" "$native_asset_root"
rm -rf "$native_asset_root/smoke"
rm -f "$native_asset_root/libghlinguist.so"

RUBYLIB="$native_asset_root/lib:$native_asset_root" \
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
