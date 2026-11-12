#!/bin/bash
D=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
for rep in 1 2; do for f in 48367.stl 75654.stl 82034.stl 248188.stl 42179.stl 1053378.stl; do for m in 0 2 3; do
  o=$(FACEMODE=$m BENCH_REPAIR=wrappipe TIMEOUT=400 $B/run.sh $D/$f); echo "{\"m\":$m,\"row\":$o}"; done; done; done
