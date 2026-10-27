#!/bin/bash
# Builds the benchmark and runs all cases (one process each, with a timeout) in parallel.
# Usage: run_all.sh [tag] [filter]   Env: BENCH_P (cases in parallel, default 2), TIMEOUT (s, default 1800)
#   tag:    results/<tag>.jsonl (default: all)
#   filter: only cases whose line matches this regex (eg "^volume")
# The large cases (24M + 24M triangles) need about 14 GB of memory: run them with BENCH_P=1.
set -e
cd "$(dirname "$0")"
dotnet build -c Release -v q
mkdir -p results
tag=${1:-all}

# windows (UTM 32N): 250 m and 500 m crops of the cut faces, 1, 4 and 12 km^2
C250=326500,5640250,326750,5640500
C500=326500,5640250,327000,5640750
T1=326000,5641000,327000,5642000
T4=326000,5640000,328000,5642000
T12=326000,5640000,329000,5644000
P1=327000,5641000,328000,5642000      # laser scan tile 327/5641
P2=322000,5642000,323000,5643000      # laser scan tile 322/5642

cases() {
  for c in utm local; do
    echo "volume before=dgm2022 after=bdom2025_1m win=$T1 coords=$c"
    echo "volume before=dgm2022 after=bdom2025 win=$C500 coords=$c"
  done
  echo "volume before=dgm2022 after=bdom2025_1m win=$T4"
  echo "volume before=dgm2022 after=bdom2025_1m win=$T4 heightcheck=0"
  echo "volume before=dgm2022 after=bdom2025_1m win=$T12"
  echo "volume before=dgm2022 after=bdom2025 win=$T1"
  echo "volume before=dgm2022 after=bdom2025 win=$T4"
  for s in 0.05 0.2; do echo "volume before=dgm2022 after=bdom2025_1m win=$T1 simplify=$s"; done
  for v in clean tiles overlap holes; do
    for m in auto cleanup robust tdc; do echo "repair grid=dgm2022 win=$C500 variant=$v mode=$m"; done
  done
  echo "repair grid=dgm2022 win=$C500 variant=tiles mode=auto coords=local"
  echo "repair grid=dgm2022 win=$T1 variant=tiles mode=auto"
  for c in utm local; do
    echo "tdc grid=dgm2022 win=$T1 coords=$c"
    echo "tdc grid=dgm2022_327_5641 points=3dm_327_5641 win=$P1 coords=$c"
  done
  echo "tdc grid=dgm2022 win=$T1 tol=0.2"
  echo "tdc grid=dgm2022 win=$T4"
  echo "tdc grid=dgm2022 win=$T12"
  echo "tdc grid=dgm2022_322_5642 points=3dm_322_5642 win=$P2"
  for e in 0.05 0.1 0.2 0.5; do echo "gtin grid=dgm2022 win=$T1 maxerr=$e"; echo "gtin grid=dgm2022 win=$T1 maxerr=$e remove=1"; done
  for e in 0.05 0.2; do echo "gtin grid=dgm2022 win=$T12 maxerr=$e"; done
  echo "gtin grid=dgm2022 win=$T1 maxerr=0.1 coords=local"
  # fidelity of the simplified TINs (curvature, sharp edges at the benches) against the full grid TIN
  for e in 0.05 0.2 0.5 1; do for r in 0 1; do echo "gtin grid=dgm2022 win=$T1 maxerr=$e remove=$r fidelity=1"; done; done
  echo "gtin grid=dgm2022 win=$T1 maxerr=0.2 fidelity=1 coords=local"
  for c in utm local; do
    for d in 5 -5 20; do echo "offset grid=dgm2022 win=$C250 d=$d coords=$c"; done
  done
  echo "offset grid=dgm2022 win=$C500 d=10"
  for c in utm local; do
    for t in grid tdc; do
      for m in horizontal surface guaranteed; do echo "safety grid=dgm2022 win=$C250 d=5 mode=$m terrain=$t coords=$c"; done
    done
  done
  for m in horizontal surface guaranteed; do echo "safety grid=dgm2022 win=$C500 d=10 mode=$m terrain=tdc"; done
  for m in surface guaranteed; do echo "safety grid=dgm2022 win=$C500 d=10 mode=$m terrain=grid"; done
  echo "safety grid=dgm2022 win=$C500 d=20 mode=guaranteed terrain=grid"
  for c in utm local; do echo "geodesic grid=dgm2022 win=$C250 coords=$c"; done
  echo "geodesic grid=dgm2022 win=$C250 src=crest"
  echo "geodesic grid=dgm2022 win=$C500"
  echo "geodesic grid=dgm2022 win=$T1"
  for c in utm local; do
    echo "csf points=3dm_327_5641 grid=dgm2022_327_5641 coords=$c"
    echo "csf points=3dm_322_5642 grid=dgm2022_322_5642 coords=$c"
  done
  echo "csf points=3dm_322_5642 grid=dgm2022_322_5642 res=0.5"
}

cases | grep -E "${2:-.}" | xargs -P ${BENCH_P:-2} -I{} sh -c './run.sh {}' > results/$tag.jsonl
echo "results/$tag.jsonl"
