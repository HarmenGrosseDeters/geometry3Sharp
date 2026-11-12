# cats.py <perf dir> : shared profile categories, sub-buckets, and the parallel split from the perf samples (499 Hz, cpu-clock)
import re, collections, sys, json, statistics as st
from prof import load
HZ = 499.0
EXACT = re.compile(r'^(ExactPredicates|AdaptivePredicates|Exact\w*|TriangleTriangleFilter|Expansion\w*|IntervalD?\w*)::')
BVH = re.compile(r'^(AlphaWrapOracle|DMeshAABBTree3|PointAABBTree3|\w*AABBTree\w*|\w*Bvh\w*)(\+[^:]*)?::')
GRID = re.compile(r'^(TriangleHashGrid|PointHashGrid\w*|\w*HashGrid\w*|SmallListSet)(\+[^:]*)?::')
ROUND = re.compile(r'Rounding|Snap', re.I)
def native(f): return f.startswith(('libclrgc!', 'libcoreclr!', 'libclrjit!', 'K!', 'libc!', 'libm!', 'ld-linux', 'libSystem', 'libunwind', '__vdso', 'libstdc', 'libhost')) or f.startswith('[unknown]')
def category(fr):
    # native frames at the leaf side (below the first managed frame) decide the runtime buckets
    k = 0
    while k < len(fr) and native(fr[k]): k += 1
    lead = fr[:k]
    if lead and not any(f.startswith('libclrjit!') for f in lead):
        if any(f.startswith('libclrgc!') for f in lead): return 'iii', 'gc (libclrgc: allocation slow path and collections)'
        if any(f.startswith('K!') and re.search(r'page_fault|clear_page|memcg|handle_mm|do_anonymous|mmap|munmap|madvise', f) for f in lead): return 'iii', 'kernel memory (page faults)'
        if any(f.startswith('libc!') and re.search(r'memset|memmove|memcpy', f) for f in lead) and k < len(fr) and not re.match(r'^(Reducer|DMesh3|Delaunay3|MeshAlphaWrap|AlphaWrapOracle|TriangleHashGrid|DVector)', fr[k]): return 'iii', 'memset/memmove (array clears and copies in BCL)'
        if any(f.startswith('libcoreclr!') for f in lead) and k < len(fr): return 'iii', 'runtime helpers (libcoreclr: allocation helpers, casts, stubs)'
    for f in fr[k:]:
        if EXACT.match(f): return 'i', f.split('::')[0] + '::' + f.split('::')[1]
        if BVH.match(f): return 'ii', 'bvh:' + f.split('::')[0]
        if GRID.match(f): return 'ii', 'grid:' + f.split('::')[0]
        if ROUND.search(f): return 'iv', f
    if k == len(fr):
        if any(f.startswith('libclrgc!') for f in fr): return 'iii', 'gc threads (background / server GC)'
        if any(f.startswith('libclrjit!') for f in fr): return 'v', 'jit'
        return 'v', 'runtime threads / idle spin (native only)'
    if any(f.startswith('libclrjit!') for f in lead): return 'v', 'jit'
    fr = fr[k:]
    f = fr[0]
    for pat, name in ((r'^Delaunay3', 'Delaunay3 (insert, locate; filtered InSphere inlined)'), (r'^MeshAlphaWrap', 'MeshAlphaWrap (carving, queue, manifold step, extract)'),
                      (r'^(Reducer|PartitionedReduction|MeshRefinerBase|QuadricError|MeshConstraints)', 'Reducer'), (r'^TriangleDeviationBounds|^MeshHausdorff', 'deviation bounds (certify)'),
                      (r'^(DMesh3|DVector|RefCountVector|MeshQueries|MeshEditor)', 'DMesh3 / mesh queries'), (r'^K!', 'kernel other'), (r'clock_gettime', 'timers')):
        if re.search(pat, f): return 'v', name
    for f in fr:
        if re.match(r'^(Reducer|PartitionedReduction)', f): return 'v', 'Reducer'
        if re.match(r'^MeshAlphaWrap', f): return 'v', 'MeshAlphaWrap (carving, queue, manifold step, extract)'
    return 'v', 'other (BCL collections, unknown)'
PAR = re.compile(r'Parallel|ThreadPool|TaskReplicator|Task::|WorkStealing|ThreadStart')
def analyse(d):
    res = []
    for name, stk in load(d):
        row = json.load(open(f'{d}/{name}.row.json'))
        bytid = collections.Counter()
        for n, tid, fr in stk:
            if any('WrapPipeline' in f for f in fr): bytid[tid] += n
        main = bytid.most_common(1)[0][0]
        cat = collections.Counter(); sub = collections.Counter(); S = P = O = 0; tot = 0
        catS = collections.Counter(); catP = collections.Counter()
        for n, tid, fr in stk:
            c, s = category(fr); cat[c] += n; sub[(c, s)] += n; tot += n
            if tid == main and not any(PAR.search(f) for f in fr): S += n; catS[c] += n
            elif tid != main and not any(PAR.search(f) for f in fr) and (s in ('gc', 'jit') or fr[0].startswith(('libclrgc', 'libclrjit', 'libcoreclr', 'K!'))): O += n   # background GC / JIT threads
            else: P += n; catP[c] += n
        res.append(dict(name=name, row=row, cat=cat, sub=sub, S=S / HZ, P=P / HZ, O=O / HZ, tot=tot / HZ, catS=catS, catP=catP))
    return res
if __name__ == '__main__':
    res = analyse(sys.argv[1]); res.sort(key=lambda r: -r['row']['pipe_ms'])
    print('mesh | wall s | cpu s (perf) | serial S s | parallel work P s | bg GC/JIT threads s | P share of S+P | Amdahl x4 / x16 vs 1 core | floor today-structure 4 / 16 cores s | i exact % | ii bvh+grid % | iii alloc/GC % | iv % | v rest %')
    for r in res:
        S, P = r['S'], r['P']; t = sum(r['cat'].values())
        print(f"{r['name']} | {r['row']['pipe_ms']/1000:.1f} | {r['tot']:.1f} | {S:.1f} | {P:.1f} | {r['O']:.1f} | {100*P/(S+P):.0f}% | {(S+P)/(S+P/4):.2f} / {(S+P)/(S+P/16):.2f} | {S+P/4:.1f} / {S+P/16:.1f} | " + ' | '.join(f"{100*r['cat'][c]/t:.1f}" for c in ('i', 'ii', 'iii', 'iv', 'v')))
    T = sum(sum(r['cat'].values()) for r in res)
    print('\nall meshes, share of CPU samples:')
    for c in ('i', 'ii', 'iii', 'iv', 'v'):
        n = sum(r['cat'][c] for r in res); print(f"  ({c}) {100*n/T:.1f}%   median per mesh {st.median(100*r['cat'][c]/sum(r['cat'].values()) for r in res):.1f}%  | of serial main-thread time {100*sum(r['catS'][c] for r in res)/sum(sum(r['catS'].values()) for r in res):.1f}% | of parallel work {100*sum(r['catP'][c] for r in res)/max(1,sum(sum(r['catP'].values()) for r in res)):.1f}%")
    subs = collections.Counter()
    for r in res: subs.update(r['sub'])
    for (c, s), n in subs.most_common(40): print(f"    {100*n/T:5.2f}%  ({c}) {s}")
    print('sum wall %.0f s, S %.0f, P %.0f, bg %.0f' % (sum(r['row']['pipe_ms'] for r in res)/1000, sum(r['S'] for r in res), sum(r['P'] for r in res), sum(r['O'] for r in res)))
