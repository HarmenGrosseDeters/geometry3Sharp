import json, collections
S='/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/results/c3/'
KEYS=('arr.exact_point.big','arr.pt.input','arr.pt.edge','arr.pt.segments','arr.pt.planes','arr.pt.reduce','arr.pt.publish','bool.centroid.big','bool.side.big','bool.orient3d.big','arr.incircle.big','arr.orient.big','arr.compare.big','arr.along.big','arr.same_plane.big','arr.local_get.big','arr.edge_planes.big','arr.planes','arr.to_exact.outside','arr.to_exact.inside','pt.fixed','pt.box')
def agg(rows, title):
    tot=sum(r['ms'] for r in rows); alloc=sum(r['alloc_mb'] for r in rows); gc=sum(r['gc_pause_ms'] for r in rows)
    paths=collections.defaultdict(lambda:[0,0.0,0.0])
    for r in rows:
        for k,v in r.get('paths',{}).items():
            paths[k][0]+=v[0]; paths[k][1]+=v[1]; paths[k][2]+=v[2]
    print('\n#### %s: %d operations, %.2f s, GC pause %.2f s, allocated %.2f GB (one thread, timers on)' % (title, len(rows), tot/1000, gc/1000, alloc/1024))
    print('| region | calls | ms | share of the time | MB | share of the allocation | us per call |\n|---|---|---|---|---|---|---|')
    for k in KEYS:
        v=paths.get(k,[0,0,0])
        print('| %s | %d | %.0f | %.2f %% | %.0f | %.1f %% | %.1f |' % (k, v[0], v[1], 100*v[1]/tot, v[2], 100*v[2]/alloc, 1000*v[1]/max(1,v[0])))
for f,t in (('normal_pairs','normal, 20 pairs'),('normal_copy','normal, 13 models with a rotated copy')):
    agg([json.loads(l) for l in open(S+'serial_%s.jsonl'%f)], t)
rows=[json.loads(l) for l in open(S+'serial_b.jsonl')]
for c in ('copy_107554','copy_42327'):
    agg([r for r in rows if r['case']==c], 'slow tail, '+c)
