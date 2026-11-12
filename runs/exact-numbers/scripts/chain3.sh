#!/bin/bash
# the shares of the exact paths: one thread (gParallel serial on the calling thread, one core, non-concurrent GC), so a
# timed region's wall time is its CPU time plus the GC its own allocations trigger; nothing else may run on core 0
cd /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
export EXACT_SERIAL=1 DOTNET_gcConcurrent=0 DOTNET_TieredPGO=0
B="taskset -c 0 dotnet bin/inst/BooleanComparison.dll"
$B list data/cases_normal_pairs.txt results/normal_pairs.jsonl 2> results/normal_pairs.log
$B list data/cases_normal_copy.txt results/normal_copy.jsonl 2> results/normal_copy.log
$B list data/cases_tail_small.txt results/tail_small.jsonl 2> results/tail_small.log
$B list data/cases_tail_big.txt results/tail_big.jsonl 2> results/tail_big.log
$B list data/cases_tail_55928.txt results/tail_55928.jsonl 2> results/tail_55928.log
echo chain3 finished
