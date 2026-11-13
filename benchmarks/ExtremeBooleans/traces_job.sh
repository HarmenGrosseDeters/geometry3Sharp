#!/bin/bash
# Sampling profiles for the shared category table (docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 4.8): one
# dotnet-trace Speedscope file per case into <work dir>/traces, skipped if present. Then the normal stratum's rows
# (run.sh, union) into normal.jsonl. Copy this script and run.sh to the work directory first.
#   traces_job.sh <BooleanComparison tool dir> <MeshFuzzPilot tool dir> <work dir> <normal id>...
T=$1; F=$2; W=$3; shift 3; R=$(dirname "$0")/run.sh; DT=${DOTNET_TRACE:-$HOME/.dotnet/tools/dotnet-trace}
cd "$W"; mkdir -p traces
tr() { name=$1; shift; [ -f traces/$name.speedscope.json ] && return
  "$DT" collect --profile dotnet-sampled-thread-time --format Speedscope -o traces/$name.nettrace -- "$@" > traces/$name.out 2>&1
  rm -f traces/$name.nettrace; }
P="dotnet $T/BooleanComparison.dll profile --repeat 1 --safe-rounding"
tr r55928d $P --ops difference cases/rot/55928
tr r112798 $P --ops union cases/rot/112798
tr r1005586 $P --ops union cases/rot/1005586
tr r107554i $P --ops intersection cases/rot/107554
tr r1368057 $P --ops union cases/rot/1368057
tr r122796 $P --ops union cases/rot/122796
tr r122796A $P --antipodal-unlimited --ops union cases/rot/122796
tr boxes64 dotnet $T/BooleanComparison.dll prestudy-csg boxes 64 1
for k in $(ls units/grp-b | grep '^timeout'); do
  MESH_FUZZ_OPS=robust-repair,robust-repair-soup,rounding-repair tr b_${k:8:8} dotnet $F/MeshFuzzPilot.dll replay units/grp-b/$k; done
tr a_0edbe479 $P --ops intersection cases/fuzza/0edbe479e7ef91805bf6ba609677b08fb41ec312
for id in "$@"; do tr n$id $P --ops union cases/rot/$id; done
for id in "$@"; do "$R" "$T" "$W/normal.jsonl" union cases/rot/$id; done
echo done
