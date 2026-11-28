#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 6 ]]; then
    echo "Usage: bash run-weighted-trial.sh <stage1|stage2> <dotnet> <audit.dll> <tail-root> <fixture-dir> <derived-dir>" >&2
    exit 2
fi

stage=$1
sdk=$2
audit=$3
tail_root=$4
fixtures=$5
derived=$6
export DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredPGO=0 DOTNET_GCHeapHardLimit=0x150000000

run() {
    timeout 420s "$sdk" "$audit" endpoint-prototype "$@"
}

tail_pair() {
    local id=$1 operation=$2 budget=$3
    run "Thingi10K-$id" "$operation" "$budget" 4 \
        "$tail_root/operands/$id/A.off" "$tail_root/operands/$id/B.off" --allow-open
}

case "$stage" in
    stage1)
        run coplanar Union 48 12 "$fixtures/B04-identical-other-diagonal-a.obj" "$fixtures/B04-identical-other-diagonal-b.obj"
        run cubes70 Union 387280 4 70
        tail_pair 55928 Difference 1600000
        tail_pair 519511 Union 1000000
        ;;
    stage2)
        run cubes20 Union 20000 4 20
        run cubes20 Union 112800 4 20
        run cubes70 Union 348640 4 70
        tail_pair 1368057 Union 420000
        tail_pair 1368057 Union 500000
        tail_pair 117959 Union 2573320
        run rational Difference 18 12 "$fixtures/B14-rational-cut-a.obj" "$fixtures/B14-rational-cut-b.obj"
        run ABC-00000382 Difference 21182 6 "$derived/ABC-00000382-fine.obj" "$derived/ABC-00000382-cutter.obj"
        run EMBER-0016 Intersection 28754 6 "$derived/EMBER-0016-a.obj" "$derived/EMBER-0016-b.obj"
        run EMBER-0084 Union 105802 4 "$derived/EMBER-0084-a.obj" "$derived/EMBER-0084-b.obj"
        run OBJ-02543290 Difference 3158 6 "$derived/OBJ-02543290-welded-repair.obj"
        ;;
    *)
        echo "Unknown stage: $stage" >&2
        exit 2
        ;;
esac
