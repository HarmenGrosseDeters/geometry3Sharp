import json, sys
base = {}
for l in open('/home/user/aw3runs/aw3/l4/l4_269.jsonl'):
    d = json.loads(l); base[d['file']] = d
same = diff = bad = 0; sb = sc = ib = ic = tb = tc = 0
for l in open(sys.argv[1]):
    d = json.loads(l); b = base.get(d['file'])
    if b is None or 'after_hash' not in d: bad += 1; print('missing', d['file'], d.get('status')); continue
    if d['after_hash'] == b['after_hash'] and d['raw_tris'] == b['raw_tris']: same += 1
    else: diff += 1; print('DIFF', d['file'])
    ph = lambda r, s: sum(v for k, v in r['pipe_phases'].items() if k.endswith('/' + s))
    sb += b['pipe_ms']; sc += d['pipe_ms']; ib += ph(b, 'intersection'); ic += ph(d, 'intersection'); tb += ph(b, 'touch'); tc += ph(d, 'touch')
    if d.get('status') != 'closed' or d.get('after_self_intersecting_pairs') != 0: print('not clean', d['file'], d.get('status'))
print(f'identical {same}/{same+diff} (missing {bad}); pipe {sb/1000:.0f} s -> {sc/1000:.0f} s ({sc/sb:.3f}), intersection {ic/max(ib,1):.3f}, touch {tc/max(tb,1):.3f}')
