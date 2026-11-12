# reads `perf script -F tid,time,ip,sym,dso` on stdin; keeps samples inside the row's window; prints "count<TAB>tid<TAB>leaf;...;root"
import sys, json, re, collections
row = json.load(open(sys.argv[1]))
t0, t1 = row['study_t0_ns'] / 1e9, row['study_t1_ns'] / 1e9
acc = collections.Counter(); cur = None; tid = None; keep = False
def flush():
    if cur is not None and keep: acc[(tid, ';'.join(cur))] += 1
for line in sys.stdin:
    if not line.strip():
        flush(); cur = None; continue
    if line[0] != '\t':
        flush()
        m = re.match(r'\s*(\d+)\s+([\d.]+):', line)
        tid = m.group(1); keep = t0 <= float(m.group(2)) <= t1; cur = []
    elif cur is not None:
        p = line.split(None, 1)[1].rstrip() if len(line.split(None, 1)) > 1 else '?'
        m = re.match(r'(.*) \(([^()]*)\)$', p)
        sym, dso = (m.group(1), m.group(2).split('/')[-1]) if m else (p, '')
        sym = re.sub(r'\[(Optimized|Tier\d?|PreJIT|OSR|Instrumented[^\]]*|QuickJitted)[^\]]*\]', '', sym)
        sym = re.sub(r'\+0x[0-9a-f]+$', '', sym).strip()
        cur.append((dso.split('.so')[0] + '!' if dso and not dso.startswith('perf-') and not dso.startswith('[') else ('K!' if dso.startswith('[k') else '')) + sym)
flush()
for (tid, st), n in acc.most_common():
    print(f"{n}\t{tid}\t{st}")
