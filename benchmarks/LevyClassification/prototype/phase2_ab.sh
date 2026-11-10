#!/bin/bash
# A/B of Lévy phase 2: base (origin/master) vs branch (wt_levy2), 2 rounds alternating, per id:op
set -euo pipefail
S=${S:?set S to a work dir with the worktrees wt_mtree (base) and wt_levy2 (branch)}
W=$S/levyab; J=$S/wt_levy2/benchmarks/LevyClassification
mkdir -p $W/cases $W/raw
for v in base:$S/wt_mtree branch:$S/wt_levy2; do
  name=${v%%:*}; src=${v#*:}
  if [ ! -f $W/tool_$name/LevyClassification.dll ]; then
    mkdir -p $src/benchmarks/LevyClassification
    cp $J/Program.cs $J/LevyClassification.csproj $src/benchmarks/LevyClassification/ 2>/dev/null || true
    dotnet build $src/benchmarks/LevyClassification -c Release -o $W/tool_$name > $W/build_$name.log 2>&1
  fi
done
[ -f $W/tool_export/BooleanComparison.dll ] || dotnet build $S/wt_levy2/benchmarks/BooleanComparison -c Release -f net10.0 -o $W/tool_export > $W/build_export.log 2>&1
for x in "$@"; do
  id=${x%%:*}
  if [ ! -f $W/cases/$id/B.off ]; then
    python3 $J/fetch_thingi.py $W/raw $id > /dev/null
    f=$(ls $W/raw/$id.* | head -1)
    dotnet $W/tool_export/BooleanComparison.dll export $f $W/cases/$id > /dev/null 2>&1
    rm -f $f
  fi
done
for r in 1 2; do
  for x in "$@"; do
    id=${x%%:*}; op=${x#*:}
    for name in base branch; do
      dotnet $W/tool_$name/LevyClassification.dll --ops "$op" $W/cases/$id \
        | python3 -c "import json,sys; r=json.loads(sys.stdin.read()); print(json.dumps({'variant':'$name','round':$r,'case':r['case'],'op':r['op'],'ms':r['ms'],'classification_ms':r['phases_ms'].get('classification'),'hash':r['hash']}))" | tee -a $W/results.jsonl
    done
  done
done
