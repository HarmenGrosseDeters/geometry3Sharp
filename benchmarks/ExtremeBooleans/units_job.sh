#!/bin/bash
# The fuzz units of #624 (runs/mesh-fuzz, fetched at run time, never committed), docs/perf/EXTREME_RUNTIME_BOOLEANS.md
# section 3.4. Copy this script and run.sh to the work directory first.
#   units_job.sh <BooleanComparison tool dir> <MeshFuzzPilot tool dir> <work dir>
# <work dir>/cases/fuzza/<sha1>: grp-a units as case directories (dump2off.py of `MeshFuzzPilot dump`), with their
# operation in <work dir>/fuzza_ops.txt ("<sha1> <op>"); <work dir>/units/grp-b/*: the grp-b input files.
# grp-a: run.sh rows into units.jsonl. grp-b: `MeshFuzzPilot replay` per file under /usr/bin/time (the whole unit, the
# target's check included), one line per file in grp_b.tsv. Resumable.
T=$1; F=$2; W=$3; R=$(dirname "$0")/run.sh
cd "$W"
while read -r k op; do "$R" "$T" "$W/units.jsonl" "$op" "cases/fuzza/$k"; done < fuzza_ops.txt
touch grp_b.tsv
for f in units/grp-b/*; do k=$(basename "$f"); grep -q "^$k	" grp_b.tsv && continue
  MESH_FUZZ_OPS=robust-repair,robust-repair-soup,rounding-repair /usr/bin/time -f "%e	%M" -o t.txt timeout 600 dotnet "$F/MeshFuzzPilot.dll" replay "$f" > o.txt 2>&1; rc=$?
  echo "$k	$rc	$(cat t.txt | tail -1)	$(grep -m1 -o 'op [0-9] ([a-z-]*)' o.txt)" >> grp_b.tsv
done
echo done
