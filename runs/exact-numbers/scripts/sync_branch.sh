#!/bin/bash
# copies the study's scripts, prototypes, specs and results into the runs/exact-numbers worktree (no meshes, no fuzz
# inputs, no library changes: the instrumentation is a patch file)
set -e
S=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
D=$S/runs-wt/runs/exact-numbers
mkdir -p $D/Probe $D/scripts $D/specs $D/results $D/data
cp $S/Probe/*.cs $S/Probe/Probe.csproj $D/Probe/
cp $S/scripts/*.py $S/scripts/*.sh $D/scripts/
cp $S/specs/* $D/specs/
cp $S/data/*.md $S/data/cases_*.txt $D/data/ 2>/dev/null || true
mkdir -p $D/results/mt $D/results/1core_pool $D/results/pr $D/results/b $D/results/b3 $D/results/c $D/results/c2 $D/results/c3 $D/results/d; cp $S/results/d/*.jsonl $D/results/d/; for x in d2 d3 d4 d5; do mkdir -p $D/results/$x; cp $S/results/$x/*.jsonl $D/results/$x/; done; cp $S/results/c2/*.jsonl $D/results/c2/; cp $S/results/c3/*.jsonl $D/results/c3/; cp $S/results/b3/*.jsonl $D/results/b3/; cp $S/results/c/*.jsonl $D/results/c/; cp $S/results/b/*.jsonl $D/results/b/; cp $S/results/*.jsonl $D/results/; cp $S/results/mt/*.jsonl $D/results/mt/; cp $S/results/1core_pool/*.jsonl $D/results/1core_pool/; cp $S/results/pr/*.jsonl $S/results/pr/*.md $D/results/pr/
(cd $S/lib && git add -N math/PathStats.cs math/DyadicPrototype.cs math/ScaledFilterPrototype.cs math/LimbProto.cs 2>/dev/null; git diff c0f8aa9 -- . ':!obj' ':!bin') > $D/instrumentation.patch
[ -f $S/REPORT.md ] && cp $S/REPORT.md $D/REPORT.md
[ -f $S/README.md ] && cp $S/README.md $D/README.md
cd $S/runs-wt && git add runs/exact-numbers && git status --short | wc -l
