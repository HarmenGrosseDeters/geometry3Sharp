#!/bin/bash
# serial measurement chain (nothing else may run on the machine)
cd /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
B="dotnet bin/inst/BooleanComparison.dll"
# the fuzz units: the rounding is serial, all cores as in the issue
$B list data/cases_fuzz_slow.txt results/fuzz_base.jsonl 2> results/fuzz_base.log
EXACT_PROTO=dyadic $B list data/cases_fuzz_slow.txt results/fuzz_dyadic.jsonl 2> results/fuzz_dyadic.log
EXACT_PROTO=dyadic,filter $B list data/cases_fuzz_slow.txt results/fuzz_dyadic_filter.jsonl 2> results/fuzz_dyadic_filter.log
# the shares: one core (wall = CPU; a region's time includes the GC its allocations trigger)
export DOTNET_PROCESSOR_COUNT=1
T="taskset -c 0"
$T $B list data/cases_normal_pairs.txt results/normal_pairs.jsonl 2> results/normal_pairs.log
$T $B list data/cases_normal_copy.txt results/normal_copy.jsonl 2> results/normal_copy.log
$T $B list data/cases_tail_small.txt results/tail_small.jsonl 2> results/tail_small.log
$T $B list data/cases_tail_big.txt results/tail_big.jsonl 2> results/tail_big.log
$T $B list data/cases_tail_55928.txt results/tail_55928.jsonl 2> results/tail_55928.log
echo chain2 finished
