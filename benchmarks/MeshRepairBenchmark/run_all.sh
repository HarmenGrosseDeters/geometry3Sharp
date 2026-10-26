#!/bin/bash
# Builds the benchmark and runs it on all downloaded meshes.
# Usage: run_all.sh <default|cleanup|instr> [std|soup|manual] [none|meters|geo] [tolerance factor]
# Writes results/<mode>.jsonl and results/<mode>_large.jsonl
# Env: BENCH_DATA=holdout reads data/holdout/ (fetch_data.py --holdout) and writes results/holdout/
set -e
cd "$(dirname "$0")"
dotnet build -c Release -v q
data=data${BENCH_DATA:+/$BENCH_DATA}
res=results${BENCH_DATA:+/$BENCH_DATA}
mkdir -p $res
mode=${1:-default}
tag=$mode${2:+_$2}${3:+_$3}${4:+_$4}
ls $data/meshes/* | BENCH_REPAIR=$mode xargs -P ${BENCH_P:-4} -I{} ./run.sh {} "${2:-std}" "${3:-none}" "${4:-1}" > $res/$tag.jsonl
if [ -d $data/meshes_large ]; then
  ls $data/meshes_large/* | BENCH_REPAIR=$mode TIMEOUT=900 xargs -P ${BENCH_P:-4} -I{} ./run.sh {} "${2:-std}" "${3:-none}" "${4:-1}" > $res/${tag}_large.jsonl
fi
echo "$res/$tag.jsonl"
