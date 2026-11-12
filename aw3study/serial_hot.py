# serial_hot.py <perf dir> : self and inclusive shares of the serial main-thread samples (the Amdahl-limiting part)
import sys, collections
from cats import analyse, PAR, HZ
from prof import load
import json
d = sys.argv[1]; self_ = collections.Counter(); inc = collections.Counter(); tot = 0; wall = 0
for name, stk in load(d):
    bytid = collections.Counter()
    for n, tid, fr in stk:
        if any('WrapPipeline' in f for f in fr): bytid[tid] += n
    main = bytid.most_common(1)[0][0]
    wall += json.load(open(f'{d}/{name}.row.json'))['pipe_ms'] / 1000
    for n, tid, fr in stk:
        if tid != main or any(PAR.search(f) for f in fr): continue
        tot += n; self_[fr[0]] += n
        for f in set(fr): inc[f] += n
print(f'serial main-thread samples {tot/HZ:.0f} s of wall {wall:.0f} s')
print('--- self'); [print('%5.1f%% %s' % (100 * v / tot, k)) for k, v in self_.most_common(int(sys.argv[2]))]
print('--- inclusive'); [print('%5.1f%% %s' % (100 * v / tot, k)) for k, v in inc.most_common(int(sys.argv[3])) if not k.startswith(('lib', 'Program', '[unk', 'K!'))]
