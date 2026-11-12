#!/bin/bash
cd $(dirname $0)
for L in tail8 normal10; do
  DOTNET_gcConcurrent=0 ./drive.sh list_$L.txt gc_noconc_$L.jsonl
  DOTNET_gcServer=1 ./drive.sh list_$L.txt gc_server_$L.jsonl
  DOTNET_GCgen0size=0x10000000 ./drive.sh list_$L.txt gc_gen0_256m_$L.jsonl
  ./drive.sh list_$L.txt base4b_$L.jsonl
done
./budget.sh base4_tail.jsonl 300000 budget300k_tail.jsonl
for L in tail8 normal10; do
  while read f; do grep -q "\"file\":\"$f\"" serial_$L.jsonl 2>/dev/null || BENCH_REPAIR=wrappipe BENCH_FIDELITY=0 TIMEOUT=900 taskset -c 0 /home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/run.sh /home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes/$f >> serial_$L.jsonl; done < list_$L.txt
done
echo DONE
