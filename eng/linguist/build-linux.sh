#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
manifest_path="$script_dir/native-dependencies.json"
license_inventory_path="$script_dir/third-party-redistribution.json"
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

for command in git ruby gem make cc cmake ldd patchelf readelf dpkg-query sha256sum; do
  command -v "$command" >/dev/null 2>&1 || fail "Required command is unavailable: $command"
done
require_path "$manifest_path" "native dependency manifest"
require_path "$license_inventory_path" "third-party redistribution inventory"
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
ruby_description="$(ruby -e 'print RUBY_DESCRIPTION')"

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
mkdir -p "$build_root/tokenizer" "$build_root/gems" "$native_asset_root/bin" "$native_asset_root/lib" "$native_asset_root/linguist" "$native_asset_root/licenses"

copy_required_license() {
  local destination="$1"
  shift
  local source
  for source in "$@"; do
    if [[ -f "$source" ]]; then
      mkdir -p "$(dirname "$destination")"
      cp -a "$source" "$destination"
      return
    fi
  done
  fail "Required license text is missing. Expected one of: $*"
}

download_locked_file() {
  local destination="$1"
  local url="$2"
  local expected_sha256="$3"
  [[ "$expected_sha256" =~ ^[a-f0-9]{64}$ ]] || fail "Invalid SHA-256 for locked file $url."
  mkdir -p "$(dirname "$destination")"
  DOWNLOAD_URL="$url" DOWNLOAD_DESTINATION="$destination" ruby -ropen-uri -e 'URI.open(ENV.fetch("DOWNLOAD_URL"), "rb") { |source| File.open(ENV.fetch("DOWNLOAD_DESTINATION"), "wb") { |target| IO.copy_stream(source, target) } }'
  [[ "$(sha256sum "$destination" | awk '{ print $1 }')" == "$expected_sha256" ]] || fail "SHA-256 mismatch for locked file $url."
}

copy_gem_licenses() {
  local gem_name="$1"
  local gem_version="$2"
  local gem_root="$gem_home/gems/$gem_name-$gem_version"
  local destination="$native_asset_root/licenses/gems/$gem_name-$gem_version"
  local license_files=()
  mapfile -d '' license_files < <(find "$gem_root" -maxdepth 2 -type f \( -iname 'license*' -o -iname 'copying*' \) -print0)
  if (( ${#license_files[@]} == 0 )); then
    if [[ "$gem_name" == "charlock_holmes" && "$gem_version" == "0.7.9" ]]; then
      license_files=("$script_dir/licenses/charlock_holmes-0.7.9-LICENSE")
    else
      fail "Required license or copying file is missing from gem $gem_name $gem_version."
    fi
  fi
  mkdir -p "$destination"
  local license_file
  for license_file in "${license_files[@]}"; do
    cp -a "$license_file" "$destination/$(basename "$license_file")"
  done
}

cp -a "$ruby_prefix/bin/ruby" "$native_asset_root/bin/ruby"
shopt -s nullglob
ruby_libraries=("$ruby_prefix/lib"/libruby.so*)
(( ${#ruby_libraries[@]} > 0 )) || fail "Ruby shared runtime library is missing under $ruby_prefix/lib."
cp -a "${ruby_libraries[@]}" "$native_asset_root/lib/"
cp -a "$ruby_prefix/lib/ruby" "$native_asset_root/lib/ruby"

gem_home="$native_asset_root/lib/ruby/gems/$ruby_abi_version"
rm -rf "$native_asset_root/lib/ruby/gems"
mkdir -p "$gem_home"
while IFS=$'\t' read -r gem_name gem_version gem_artifact gem_sha256 gem_url; do
  [[ -n "$gem_name" ]] || continue
  [[ "$gem_artifact" == "$gem_name-$gem_version.gem" ]] || fail "Unexpected artifact name for gem $gem_name $gem_version: $gem_artifact"
  [[ "$gem_url" == "https://rubygems.org/downloads/$gem_artifact" ]] || fail "Unexpected artifact URL for gem $gem_name $gem_version."
  [[ "$gem_sha256" =~ ^[a-f0-9]{64}$ ]] || fail "Invalid SHA-256 for gem $gem_name $gem_version."
  gem_artifact_path="$build_root/gems/$gem_artifact"
  GEM_URL="$gem_url" GEM_ARTIFACT_PATH="$gem_artifact_path" ruby -ropen-uri -e 'URI.open(ENV.fetch("GEM_URL"), "rb") { |source| File.open(ENV.fetch("GEM_ARTIFACT_PATH"), "wb") { |destination| IO.copy_stream(source, destination) } }'
  [[ "$(sha256sum "$gem_artifact_path" | awk '{ print $1 }')" == "$gem_sha256" ]] || fail "SHA-256 mismatch for gem artifact $gem_artifact."
  GEM_HOME="$gem_home" GEM_PATH="$gem_home" gem install --local --no-document --ignore-dependencies --install-dir "$gem_home" "$gem_artifact_path"
  copy_gem_licenses "$gem_name" "$gem_version"
done < <(ruby -rjson -e 'JSON.parse(File.read(ARGV.fetch(0))).fetch("gems").each { |gem| puts [gem.fetch("name"), gem.fetch("version"), gem.fetch("artifact"), gem.fetch("sha256"), gem.fetch("artifactUrl")].join("\t") }' "$manifest_path")
find "$gem_home/gems" -mindepth 2 -maxdepth 2 -type d -name ext -prune -exec rm -rf {} +
ruby -rjson -e '
  manifest = JSON.parse(File.read(ARGV.fetch(0)))
  gem_home = ARGV.fetch(1)
  expected = manifest.fetch("gems").map { |gem| "#{gem.fetch("name")}-#{gem.fetch("version")}" }.sort
  actual = Dir.children(File.join(gem_home, "gems")).sort
  abort "Unexpected staged gems: #{actual.inspect}; expected #{expected.inspect}" unless actual == expected
' "$manifest_path" "$gem_home"

apt_packages_path="$build_root/apt-packages.tsv"
mapfile -t apt_packages < <(ruby -rjson -e 'puts JSON.parse(File.read(ARGV.fetch(0))).fetch("linux").fetch("aptPackages")' "$manifest_path")
(( ${#apt_packages[@]} > 0 )) || fail "The apt package allowlist is empty."
: > "$apt_packages_path"
for apt_package in "${apt_packages[@]}"; do
  [[ "$apt_package" =~ ^[a-z0-9][a-z0-9+.-]*$ ]] || fail "Invalid apt package allowlist entry: $apt_package"
  dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\n' "$apt_package" >> "$apt_packages_path"
done
ruby -rjson -e '
  manifest = JSON.parse(File.read(ARGV.fetch(0)))
  expected = manifest.fetch("linux").fetch("aptPackageVersions")
  actual = File.readlines(ARGV.fetch(1), chomp: true).to_h do |line|
    name, version, = line.split("\t", 3)
    [name, version]
  end
  abort "Linux package versions do not match the dependency manifest: #{actual.inspect}; expected #{expected.inspect}" unless actual == expected
' "$manifest_path" "$apt_packages_path"

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
[[ "$classifier_sha256" == "$(manifest_value linguist.classifierSha256)" ]] || fail "Expected classifier SHA-256 $(manifest_value linguist.classifierSha256), found $classifier_sha256."

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

declare -A copied_library_sources=()
for library_path in "${ruby_libraries[@]}"; do
  copied_library_sources["$native_asset_root/lib/$(basename "$library_path")"]="$(readlink -f "$library_path")"
done
for library in icudata icui18n icuuc; do
  library_path="$(ldconfig -p | awk -v name="lib${library}.so" '$1 ~ ("^" name) { print $NF; exit }')"
  copied_library_sources["$native_asset_root/lib/$(basename "$library_path")"]="$(readlink -f "$library_path")"
done

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
      copied_library_sources["$destination"]="$(readlink -f "$dependency")"
      dependency_queue+=("$destination")
    fi
  done < <(awk '$2 == "=>" && $3 ~ /^\// { print $3 }' <<<"$ldd_output")
done

copy_required_license "$native_asset_root/licenses/MBW.GHLinguist/LICENSE" "$repo_root/LICENSE"
copy_required_license "$native_asset_root/licenses/MBW.GHLinguist/THIRD-PARTY-NOTICES.md" "$repo_root/THIRD-PARTY-NOTICES.md"
while IFS=$'\t' read -r license_name license_url license_sha256; do
  download_locked_file "$native_asset_root/licenses/ruby/$license_name" "$license_url" "$license_sha256"
done < <(ruby -rjson -e 'JSON.parse(File.read(ARGV.fetch(0))).fetch("ruby").fetch("licenseFiles").each { |file| puts [file.fetch("name"), file.fetch("url"), file.fetch("sha256")].join("\t") }' "$manifest_path")
copy_required_license "$native_asset_root/licenses/linguist/LICENSE" "$linguist_root/LICENSE"

declare -A documented_debian_packages=()
for copied_source in "${copied_library_sources[@]}"; do
  # CRuby is supplied by the pinned container image and is documented above.
  [[ "$copied_source" == "$ruby_prefix/"* ]] && continue
  [[ "$copied_source" == /usr/* || "$copied_source" == /lib/* ]] || continue
  package_owner="$(dpkg-query -S "$copied_source" 2>/dev/null | head -n 1 || true)"
  if [[ -z "$package_owner" && "$copied_source" == /usr/lib/* ]]; then
    package_owner="$(dpkg-query -S "${copied_source#/usr}" 2>/dev/null | head -n 1 || true)"
  fi
  [[ -n "$package_owner" ]] || fail "Unable to identify the Debian package owning copied ELF library: $copied_source"
  debian_package="${package_owner%%:*}"
  [[ -n "${documented_debian_packages[$debian_package]:-}" ]] && continue
  copy_required_license "$native_asset_root/licenses/debian/$debian_package/copyright" "/usr/share/doc/$debian_package/copyright"
  documented_debian_packages["$debian_package"]=1
done
copy_required_license "$native_asset_root/licenses/debian/common/GPL-2" "/usr/share/common-licenses/GPL-2"
copy_required_license "$native_asset_root/licenses/debian/common/GPL-3" "/usr/share/common-licenses/GPL-3"
copy_required_license "$native_asset_root/licenses/debian/common/LGPL-2" "/usr/share/common-licenses/LGPL-2"
copy_required_license "$native_asset_root/licenses/debian/common/LGPL-2.1" "/usr/share/common-licenses/LGPL-2.1"
copy_required_license "$native_asset_root/licenses/debian/common/LGPL-3" "/usr/share/common-licenses/LGPL-3"

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

NATIVE_ASSET_ROOT="$native_asset_root" MANIFEST_PATH="$manifest_path" LICENSE_INVENTORY_PATH="$license_inventory_path" BRIDGE_SOURCE_PATH="$bridge_source" LINGUIST_VERSION_PATH="$linguist_root/lib/linguist/VERSION" APT_PACKAGES_PATH="$apt_packages_path" RUBY_DESCRIPTION="$ruby_description" ruby -rjson -rdigest -e '
  root = ENV.fetch("NATIVE_ASSET_ROOT")
  files = Dir.chdir(root) do
    Dir.glob("**/*", File::FNM_DOTMATCH).select { |path| File.file?(path) && path != "provenance.json" }.sort.map do |path|
      { "path" => path, "sha256" => Digest::SHA256.file(path).hexdigest }
    end
  end
   manifest = JSON.parse(File.read(ENV.fetch("MANIFEST_PATH")))
   gem_artifacts = manifest.fetch("gems").map do |gem|
     gem.slice("name", "version", "artifact", "artifactUrl", "sha256", "windowsBuildArguments")
   end
   apt_packages = File.readlines(ENV.fetch("APT_PACKAGES_PATH"), chomp: true).map do |line|
     name, version, architecture = line.split("\t", 3)
     { "name" => name, "version" => version, "architecture" => architecture }
   end
   output = {
     "schemaVersion" => 2,
     "platform" => "linux-x64",
     "manifestSha256" => Digest::SHA256.file(ENV.fetch("MANIFEST_PATH")).hexdigest,
     "lockInputs" => {
       "nativeDependenciesSha256" => Digest::SHA256.file(ENV.fetch("MANIFEST_PATH")).hexdigest,
       "thirdPartyRedistributionSha256" => Digest::SHA256.file(ENV.fetch("LICENSE_INVENTORY_PATH")).hexdigest,
       "bridgeSha256" => Digest::SHA256.file(ENV.fetch("BRIDGE_SOURCE_PATH")).hexdigest,
       "linguistVersionSha256" => Digest::SHA256.file(ENV.fetch("LINGUIST_VERSION_PATH")).hexdigest
     },
      "externalDependencies" => {
        "ruby" => {
          "version" => manifest.fetch("ruby").fetch("version"),
          "description" => ENV.fetch("RUBY_DESCRIPTION"),
          "dockerImage" => manifest.fetch("ruby").fetch("dockerImage"),
          "bundledComponents" => manifest.fetch("ruby").fetch("bundledComponents")
        },
        "gems" => gem_artifacts,
       "aptPackages" => apt_packages
     },
    "rubyVersion" => manifest.fetch("ruby").fetch("version"),
    "linguistVersion" => manifest.fetch("linguist").fetch("version"),
    "linguistRevision" => manifest.fetch("linguist").fetch("revision"),
    "classifierSha256" => manifest.fetch("linguist").fetch("classifierSha256"),
    "buildConfiguration" => manifest.fetch("build"),
    "files" => files
  }
  File.write(File.join(root, "provenance.json"), JSON.pretty_generate(output) + "\n")
'

echo "Staged complete Linux native closure: $native_asset_root"
