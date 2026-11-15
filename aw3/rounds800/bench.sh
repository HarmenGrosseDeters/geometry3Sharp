#!/bin/bash
# usage: bench.sh <benchdir> <label> <out.jsonl> <listfile>
dir=$1; label=$2; out=$3; list=$4
cd $dir
for m in $(cat $list); do
  BENCH_REPAIR=wrappipe BENCH_FIDELITY=none TIMEOUT=600 ./run.sh data/sample800_2611/meshes/$m.stl soup | python3 -c "
import json,sys
d=json.loads(sys.stdin.read()); c=d.get('wrap_counts',{}); p=d.get('pipe_phases',{})
r={'label':'$label','mesh':'$m','status':d.get('status'),'wrap_ms':d.get('wrap_ms'),'pipe_ms':d.get('pipe_ms'),'carve':p.get('wrap/carve'),'raw':d.get('raw_tris'),'raw_min':d.get('raw_angle_min'),'raw_p01':d.get('raw_angle_p01'),'fin_min':d.get('q_min_angle'),'fin_p01':d.get('after_angle_p01'),'fin_tris':d.get('after_tris'),'fin_lt05':d.get('q_lt05'),'fin_gt170':d.get('q_gt170_share'),'rounds':c.get('rounds'),'deferred':c.get('round_deferred'),'kept':c.get('round_kept')}
print(json.dumps(r))" >> $out
done
