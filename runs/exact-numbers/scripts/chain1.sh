#!/bin/bash
# serial measurement chain (nothing else may run on the machine)
cd /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
B="dotnet bin/inst/BooleanComparison.dll"
$B list data/cases_tail_small.txt results/tail_small.jsonl 2> results/tail_small.log
$B list data/cases_fuzz_slow.txt results/fuzz_base.jsonl 2> results/fuzz_base.log
EXACT_PROTO=dyadic $B list data/cases_fuzz_slow.txt results/fuzz_dyadic.jsonl 2> results/fuzz_dyadic.log
EXACT_PROTO=dyadic,filter $B list data/cases_fuzz_slow.txt results/fuzz_dyadic_filter.jsonl 2> results/fuzz_dyadic_filter.log
$B list data/cases_tail_big.txt results/tail_big.jsonl 2> results/tail_big.log
$B list data/cases_tail_55928.txt results/tail_55928.jsonl 2> results/tail_55928.log
echo chain1 finished
