#!/usr/bin/env bash

set -euo pipefail

manifest_path="${1:?Expected the native dependency manifest path.}"
mapfile -t packages < <(ruby -rjson -e 'manifest = JSON.parse(File.read(ARGV.fetch(0))); puts manifest.fetch("linux").fetch("aptPackages")' "$manifest_path")
(( ${#packages[@]} > 0 )) || { echo "The apt package allowlist is empty." >&2; exit 1; }

apt-get update
apt-get install --yes --no-install-recommends "${packages[@]}"
rm -rf /var/lib/apt/lists/*
