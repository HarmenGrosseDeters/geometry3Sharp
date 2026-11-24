#!/bin/sh
# All runs of the varifold feature / curvature study (docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md), 4 cores.
set -e
B=benchmarks/SharpFeatureProbe/bin/Release/net10.0/SharpFeatureProbe
O=benchmarks/SharpFeatureProbe/results/soup
dotnet build benchmarks/SharpFeatureProbe -c Release -v q
mkdir -p $O && rm -f $O/*.jsonl
$B soup crease $O/crease.jsonl tris=50000 "breaks=clean;soup;nonconf;jitter:0.02;jitter:0.1;holes:0.1;dup:0.2;gap:0.3;gap:1;flip:0.2;mix:0.25;mix:0.5;mix:1;mix:2" c=1.5,2,3 > /dev/null
$B soup curv $O/curv.jsonl tris=50000 > /dev/null
$B soup cloud $O/cloud.jsonl tris=50000 > /dev/null
$B soup time $O/time.jsonl > /dev/null
$B soup check $O/check.jsonl > /dev/null
python3 benchmarks/SharpFeatureProbe/soup_tables.py $O > $O/tables.md
