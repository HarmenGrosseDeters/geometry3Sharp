#!/bin/bash
# before / after of the PR "exact fallback outside the expansion range": master a7693fd against the PR branch, the same
# probe (no instrumentation), nothing else on the machine
cd /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
dotnet build Probe -c Release -p:G3Root=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/base -o /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/bin/base 2>&1 | grep -E " error |Error\(s\)" | sort -u
dotnet build Probe -c Release -p:G3Root=/home/user/geometry3Sharp -o /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/bin/pr 2>&1 | grep -E " error |Error\(s\)" | sort -u
dotnet build /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/base/benchmarks/PredicateComparison -c Release -o /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/bin/span_base 2>&1 | grep -E " error |Error\(s\)" | sort -u
dotnet build /home/user/geometry3Sharp/benchmarks/PredicateComparison -c Release -o /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/bin/span_pr 2>&1 | grep -E " error |Error\(s\)" | sort -u
dotnet bin/span_base/PredicateComparison.dll span 20000 > results/pr/span_base.md 2>&1
dotnet bin/span_pr/PredicateComparison.dll span 20000 > results/pr/span_pr.md 2>&1
dotnet bin/base/BooleanComparison.dll list data/cases_fuzz_slow.txt results/pr/fuzz_base.jsonl 2> results/pr/fuzz_base.log
dotnet bin/pr/BooleanComparison.dll list data/cases_fuzz_slow.txt results/pr/fuzz_pr.jsonl 2> results/pr/fuzz_pr.log
for i in 1 2 3 4 5; do
  for v in base pr; do
    dotnet bin/$v/BooleanComparison.dll list data/cases_normal_pairs.txt results/pr/normal_pairs_${v}_$i.jsonl 2> /dev/null
    dotnet bin/$v/BooleanComparison.dll list data/cases_normal_copy.txt results/pr/normal_copy_${v}_$i.jsonl 2> /dev/null
  done
done
echo bench_pr finished
