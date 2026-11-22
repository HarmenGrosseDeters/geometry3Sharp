#!/usr/bin/env bash
# Turns extracted ABC files into the input of `FilletDetection corpus`:
#   prepare.sh <dir with *_features_*.yml> <dir with *_trimesh_*.obj> <out dir>
# writes <out>/<model id>.json and <out>/<model id>.obj (moved) and <out>/ids.txt, the model ids in the order of the
# sample rule (sample.py). A feature file that does not convert is reported and left out.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
feat="$1"; obj="$2"; out="$3"
mkdir -p "$out"
failed=0
find "$feat" -type f -name '*.yml' | sort > "$out/feat_files.txt"
while read -r f; do
  id="$(python3 "$here/sample.py" id "$f")"
  [ -n "$id" ] || continue
  if ! python3 "$here/feat_to_json.py" "$f" "$out/$id.json" 2> "$out/convert_error.txt"; then
    failed=$((failed + 1)); rm -f "$out/$id.json"
    if [ "$failed" -le 3 ]; then echo "conversion failed: $(basename "$f"): $(tail -n 1 "$out/convert_error.txt")"; fi
  fi
done < "$out/feat_files.txt"
find "$obj" -type f -name '*.obj' | sort | while read -r f; do
  id="$(python3 "$here/sample.py" id "$f")"
  if [ -n "$id" ]; then mv "$f" "$out/$id.obj"; fi
done
# (a call without feature files, eg to add the meshes later, keeps the ids of the earlier call)
if [ -s "$out/feat_files.txt" ]; then
  python3 "$here/sample.py" order < "$out/feat_files.txt" | while read -r f; do python3 "$here/sample.py" id "$f"; done > "$out/ids.txt"
fi
touch "$out/ids.txt"
rm -f "$out/feat_files.txt" "$out/convert_error.txt"
echo "prepared: $(ls "$out" | grep -c '\.json$') feature files converted ($failed failed), $(ls "$out" | grep -c '\.obj$') meshes, $(wc -l < "$out/ids.txt") ids"
