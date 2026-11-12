# query_counts.py : counts and cost per query of the wrap's input queries (AlphaWrapOracle), from the counter probe
# (oracle_counters_probe.patch; counts are deterministic) and the phase times of the unpatched baseline rows
import json, statistics as st
from phases import split
def load(p): return {r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())}
for s in ('tail', 'normal'):
    base, cnt = load(f'base4_{s}.jsonl'), load(f'counts_{s}.jsonl')
    S = [0] * 12; o = t = g = st_ = T = 0; same = 0; per = []
    for f, r in cnt.items():
        q = r['study_q']; b = base[f]; sp = split(b); same += r['after_hash'] == b['after_hash']
        for i in range(12): S[i] += q[i]
        o += sp['oracle']; t += sp['touch']; g += r['wrap_counts']['gates_popped']; st_ += r['wrap_counts']['steiner_points']; T += b['pipe_ms']
        per.append((1e6 * sp['oracle'] / q[0], 1e6 * sp['touch'] / q[4], q[1] / q[0], q[5] / q[4]))
    print(f"## {s} ({len(cnt)} meshes, output hashes equal to the baseline on {same}); popped gates {g/1e6:.0f} M, Steiner points {st_/1e6:.1f} M")
    print('| query | calls per popped gate | per Steiner point | ns per query (sum; median mesh) | share of T | BVH nodes per query | primitive tests per query | outcome |')
    print('|---|---:|---:|---:|---:|---:|---:|---|')
    print(f"| FirstEntry (segment vs offset input, rule 1) | {S[0]/g:.2f} | {S[0]/st_:.1f} | {1e6*o/S[0]:.0f}; {st.median(x[0] for x in per):.0f} | {100*o/T:.1f} % | {S[1]/S[0]:.1f} | {S[2]/S[0]:.1f} | hit {100*S[11]/S[0]:.1f} %, rejected at the root box {100*S[3]/S[0]:.0f} % |")
    print(f"| TetrahedronTouches (touch test) | {S[4]/g:.2f} | {S[4]/st_:.1f} | {1e6*t/S[4]:.0f}; {st.median(x[1] for x in per):.0f} | {100*t/T:.1f} % | {S[5]/S[4]:.1f} | {S[6]/S[4]:.1f} | touches {100*S[7]/S[4]:.1f} % |")
    print(f"| Distance / closest point (rule 2, vertex distances) | {S[8]/g:.3f} | {S[8]/st_:.2f} | - (inside bookkeeping, 0.8 % of T by perf) | - | {S[9]/max(1,S[8]):.1f} | {S[10]/max(1,S[8]):.1f} | |")
    print(f"ns per visited node incl. its share of primitive tests: FirstEntry {1e6*o/S[1]:.0f}, touch {1e6*t/S[5]:.0f}\n")
