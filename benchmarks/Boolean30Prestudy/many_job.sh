#!/bin/bash
# Exact Boolean 3.0 pre-study, topic 7: one body against many (T3a: A - (B1 u ... u Bn); T3b: A n Bi each).
# usage: bash many_job.sh <work dir> <tool dir>; rows to <work dir>/many_a.jsonl and many_b.jsonl, resumable (one
# process per case and variant, each row tagged with its key).
set -uo pipefail
W=$(cd "$1" && pwd); T=$(cd "$2" && pwd)
run() { out=$1; shift; cores=$1; shift; key="$cores $*"
  grep -q "\"key\":\"$key\"" "$out" 2> /dev/null && return
  DOTNET_PROCESSOR_COUNT=$cores timeout 2400 taskset -c 0-$((cores - 1)) dotnet "$T/BooleanComparison.dll" "$@" 2> /dev/null \
    | sed "s|^{|{\"key\":\"$key\",|" >> "$out" || echo "{\"key\":\"$key\",\"error\":\"exit\"}" >> "$out"; }
A=$W/many_a.jsonl; B=$W/many_b.jsonl
for v in onepass chained twostep tree probe; do
  for n in 5 20 50 100 200; do for s in 1 2; do run $A 4 prestudy-many-a terrain $n $s --size 50 --variants $v; done; done
  for n in 100 200; do run $A 4 prestudy-many-a terrain $n 1 --size 70 --variants $v; done
  for n in 10 50 100 200 500; do run $A 4 prestudy-many-a cad $n 1 --size 30 --variants $v; done
  for n in 10 50 100; do run $A 4 prestudy-many-a cad $n 1 --size 92 --variants $v; done
done
for c in 4 2 1; do
  for n in 5 20 50 100 200; do run $B $c prestudy-many-b terrain $n 1 --size 50; done
  for n in 10 50 100 500; do run $B $c prestudy-many-b cad $n 1 --size 30; done
  for n in 10 50; do run $B $c prestudy-many-b cad $n 1 --size 92; done
done
echo done
