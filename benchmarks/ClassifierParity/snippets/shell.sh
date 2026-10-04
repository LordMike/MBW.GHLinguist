#!/bin/sh
set -eu
for f in "$@"; do
  [ -f "$f" ] && wc -l < "$f"
done
