#!/bin/bash
# usage: run_gate.sh <out> [extra args]; the 54 runs of round 4 (option A), with the per-stage profile lines
out=$1; shift
EXE=$(dirname "$0")/../../benchmarks/QuadFlow3BSpike/bin/Release/net10.0/QuadFlow3BSpike
: > "$out"
for v in "scale=1" "scale=0.8" "scale=1.25" "scale=1.5" "ratio=0.35" "ratio=0.7"; do
  echo "=== A $v" >> "$out"
  "$EXE" gate=1 cancel=1 proj=1 cost=2 signpen=100 signtol=0 $v "$@" | grep -E "^\||^PROF" >> "$out"
done
