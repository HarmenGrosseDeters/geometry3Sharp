#!/usr/bin/env bash
# Fetches the queries and ground truth of the Apache-2.0 sample of the ToI dataset (Belgrod, Ferguson et al.,
# github.com/Continuous-Collision-Detection/Sample-Scalable-CCD-Data, LICENSE: Apache-2.0) at a pinned commit into a
# directory outside the repository, and checks each file's SHA-256. The data is never committed (CLAUDE.md); PR-tier
# tests never fetch it (owner 2026-11-13). About 157 MB.
#
#   benchmarks/CcdBenchmark/fetch_sample.sh <target-dir>
set -euo pipefail
target="${1:?usage: fetch_sample.sh <target-dir> (outside the repository)}"
commit=d1de594b017d98b37be67f02f74e541e74804109
here="$(cd "$(dirname "$0")" && pwd)"
case "$(cd "$(dirname "$target")" 2>/dev/null && pwd)/" in
  "$(cd "$here/../.." && pwd)/"*) echo "the target must be outside the repository" >&2; exit 1 ;;
esac
mkdir -p "$target"
while read -r sum path; do
  mkdir -p "$target/$(dirname "$path")"
  if [ -f "$target/$path" ] && echo "$sum  $target/$path" | sha256sum -c --status; then continue; fi
  curl -sSfL --retry 4 -o "$target/$path" \
    "https://raw.githubusercontent.com/Continuous-Collision-Detection/Sample-Scalable-CCD-Data/$commit/$path"
  echo "$sum  $target/$path" | sha256sum -c --quiet
done < "$here/sample_files.sha256"
echo "fetched into $target"
