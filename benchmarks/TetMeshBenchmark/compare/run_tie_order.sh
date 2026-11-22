#!/bin/bash
# The tie-order measurement of docs/research/TET_TIE_ORDER_2026-11.md (CROSS_FIELD_TRANSFER_2026-11.md section 5.2).
# Usage: run_tie_order.sh <TetMeshBenchmark dll> <mesh dir> <out dir> [orders = 8]
# One process at a time: `TetMeshBenchmark tieorder <id>.stl 40 600 <floor> <orders> <trace>` for the inputs below (the meshes of
# TET_MESHING.md 17.16 above 20 s at the floor 20, and the two meshes left below 10 degrees at the default floor). Meshes are fetched at
# run time (Hugging Face mirror of Thingi10K) into <mesh dir>, deleted after use, never published (a mesh already there is used and kept).
# Floors are written as the harness prints them (10, 20, 12.5; not 10.0), or the resume check does not see the rows. Output: <out dir>/tie-order.csv (rows,
# with a header) and <out dir>/tie-order-trace.csv. Resumable: an input with all its rows is skipped. Summary: summarize_tie_order.py.
# Other inputs: G3_TIE_INPUTS="<id>:<floor> ..." (eg the 40-mesh subset at the default floor, with thingi-subset-ids.txt).
set -u
DLL=$1; MESH=$2; OUT=$3; ORDERS=${4:-8}
INPUTS=${G3_TIE_INPUTS:-"282598:10 38781:10 85539:20 40011:20 38781:20 46017:20"}      # <id>:<floor> ...
mkdir -p "$MESH" "$OUT"
FETCHED=""
CSV=$OUT/tie-order.csv; TRACE=$OUT/tie-order-trace.csv
[ -s "$CSV" ] || echo "name,alpha,floor,order,status,vertices_in,tets_in,below_floor_in,visits_total,visits_at_rescue,passes,rescue_passes,tets_out,below_floor,worst_dihedral,valid,digest,imp_s" > "$CSV"
for job in $INPUTS; do
  id=${job%%:*}; floor=${job##*:}
  have=$(grep -c "^$id.stl,40,$floor," "$CSV")
  [ "$have" -eq $((ORDERS + 1)) ] && continue
  if [ "$have" -ne 0 ]; then      # a partial input: drop its rows and trace, run it again
    grep -v "^$id.stl,40,$floor," "$CSV" > "$CSV.tmp"; mv "$CSV.tmp" "$CSV"
    [ -f "$TRACE" ] && { grep -v "^$id.stl,$floor," "$TRACE" > "$TRACE.tmp"; mv "$TRACE.tmp" "$TRACE"; }
  fi
  f=$MESH/$id.stl
  if [ ! -s "$f" ]; then      # only a complete download gets the name; only files fetched here are deleted
    curl -sS -f -L --retry 4 -m 600 -o "$f.part" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl" && mv "$f.part" "$f" && FETCHED="$FETCHED $f"
    rm -f "$f.part"
  fi
  if [ ! -s "$f" ]; then echo "$id.stl,40,$floor,-1,MISSING_MESH" >> "$CSV"; continue; fi
  dotnet "$DLL" tieorder "$f" 40 600 "$floor" "$ORDERS" "$TRACE" nohdr >> "$CSV" 2> "$OUT/tie-order-$id-$floor.err" || echo "$id.stl,40,$floor,-1,ERROR" >> "$CSV"
  [ -s "$OUT/tie-order-$id-$floor.err" ] || rm -f "$OUT/tie-order-$id-$floor.err"
done
for f in $FETCHED; do rm -f "$f"; done
