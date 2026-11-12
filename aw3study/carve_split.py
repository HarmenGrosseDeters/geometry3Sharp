# carve_split.py <perf dir> : the main carving loop (refine_and_carve_loop outside the pinch and quality passes), main thread,
# split into (a) queries against the input (AlphaWrapOracle), (b) Delaunay work incl. its predicates, (c) queue and bookkeeping,
# (d) GC / allocation; shares of the main carve and of the pipeline's wall time
import sys, json, collections, re
from prof import load
from cats import category, PAR, HZ
d = sys.argv[1]; A = collections.Counter(); sub = collections.Counter(); wall = 0; carve = 0; allmain = 0
NODE = re.compile(r'::(entry_rec|box_entry|clip|tet_rec|face_separates|box_box_dist_sqr|closest_rec|box_dist_sqr)$')
for name, stk in load(d):
    bytid = collections.Counter()
    for n, tid, fr in stk:
        if any('WrapPipeline' in f for f in fr): bytid[tid] += n
    main = bytid.most_common(1)[0][0]
    wall += json.load(open(f'{d}/{name}.row.json'))['pipe_ms'] / 1000
    for n, tid, fr in stk:
        if tid != main or any(PAR.search(f) for f in fr): continue
        allmain += n
        if not any(f.endswith('::refine_and_carve_loop') for f in fr) or any(f.endswith(('::refine_non_manifold', '::refine_quality')) for f in fr): continue
        carve += n
        c, s = category(fr)
        if c == 'iii': A['d'] += n; sub[('d', s.split(' (')[0])] += n; continue
        orc = [f for f in fr if f.startswith('AlphaWrapOracle')]
        if orc:
            A['a'] += n
            q = ('FirstEntry (segment against the offset input, rule 1)' if any(re.search(r'::(entry_rec|box_entry|primitive_entry_once|FirstEntry|clip)$', f) for f in fr)
                 else 'TetrahedronTouches (touch test)' if any(re.search(r'::(tet_rec|face_separates|tet_primitive_touch|TetrahedronTouches|box_box_dist_sqr)$', f) or 'Tet::' in f for f in fr)
                 else 'Distance / closest point (rule 2 projection, vertex distances)')
            leaf = next(f for f in fr if not f.startswith(('lib', 'K!', '[unk', '__vdso')))
            kind = 'node / box tests' if NODE.search(leaf) else 'exact predicates' if leaf.startswith(('ExactPredicates', 'AdaptivePredicates')) else 'primitive tests'
            sub[('a', q)] += n; sub[('a2', kind)] += n; continue
        if any(f.startswith('Delaunay3') for f in fr):
            A['b'] += n
            leaf = next(f for f in fr if not f.startswith(('lib', 'K!', '[unk', '__vdso')))
            sub[('b', leaf if leaf.startswith(('Delaunay3', 'ExactPredicates')) else 'other (collections)')] += n; continue
        A['c'] += n
        leaf = next((f for f in fr if not f.startswith(('lib', 'K!', '[unk'))), fr[0])
        sub[('c', leaf if leaf.startswith(('MeshAlphaWrap', '__vdso')) else 'other (lists, dictionary)')] += n
print(f'wall {wall:.0f} s, serial main thread {allmain/HZ:.0f} s, main carve {carve/HZ:.0f} s = {100*carve/HZ/wall:.1f} % of wall')
names = {'a': '(a) queries against the input (AlphaWrapOracle BVH)', 'b': '(b) Delaunay3 (insertion, location, InSphere / Orient3d)', 'c': '(c) queue and bookkeeping', 'd': '(d) GC / allocation'}
for k in 'abcd':
    print(f"{names[k]}: {100*A[k]/carve:.1f} % of the main carve, {100*A[k]/HZ/wall:.1f} % of wall, {A[k]/HZ:.0f} s")
    for (kk, s), n in sub.most_common():
        if kk[0] == k and 100 * n / carve >= 0.3: print(f"      {'  split by test kind: ' if kk == 'a2' else ''}{s}: {100*n/carve:.1f} % of carve, {100*n/HZ/wall:.1f} % of wall")
