#!/bin/bash
# A/B of the fast-winding tree prototype (prototype/fwn_parallel.diff, NOT library code): way 1 builds the trees of the
# open meshes concurrently in ExactMeshBoolean.classify, way 2 builds the two subtrees of the fast-winding cache
# concurrently in the top G3_FWN_PAR_DEPTH levels of DMeshAABBTree3 (0 = off).
# Variants: base (the master library), way1 (diff, depth 0), way12 (diff, depth 4); two rounds, alternating per case.
# usage: bash benchmarks/LevyClassification/prototype/proto_job.sh <work dir> <id>:<op>...   (after job.sh: uses its
#        cases and src_master worktree); prints one JSON line per run, also to <work dir>/proto/results.jsonl
set -euo pipefail
W=$1; shift
J=$(cd "$(dirname "$0")/.." && pwd)
SHA=$(git -C "$W/src_master" rev-parse HEAD)
for v in base proto; do
  if [ ! -f "$W/tool_h_$v/LevyClassification.dll" ]; then
    if [ $v = proto ]; then
      rm -rf "$W/src_proto"; git -C "$J" worktree add -f --detach "$W/src_proto" "$SHA"
      git -C "$W/src_proto" apply "$J/prototype/fwn_parallel.diff"; src="$W/src_proto"
    else src="$W/src_master"; fi
    mkdir -p "$src/benchmarks/LevyClassification"
    cp "$J/Program.cs" "$J/LevyClassification.csproj" "$src/benchmarks/LevyClassification/"
    dotnet build "$src/benchmarks/LevyClassification" -c Release -o "$W/tool_h_$v"
  fi
done
mkdir -p "$W/proto"
for r in 1 2; do
  for x in "$@"; do
    id=${x%%:*}; op=${x#*:}
    for v in base:base:0 way1:proto:0 way12:proto:4; do
      name=${v%%:*}; rest=${v#*:}; tool=${rest%%:*}; depth=${rest#*:}
      G3_FWN_PAR_DEPTH=$depth dotnet "$W/tool_h_$tool/LevyClassification.dll" --ops "$op" "$W/cases/$id" \
        | python3 -c "import json,sys; r=json.loads(sys.stdin.read()); print(json.dumps({'variant':'$name','round':$r,'case':r['case'],'op':r['op'],'ms':r['ms'],'classification_ms':r['phases_ms'].get('classification'),'hash':r['hash']}))" \
        | tee -a "$W/proto/results.jsonl"
    done
  done
done
