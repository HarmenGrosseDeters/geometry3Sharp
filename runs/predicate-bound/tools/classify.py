# bucket the CPU samples of a speedscope trace by predicate stage (see README: method)
import json, re, sys, collections
ENTRY = [
 ('arr.orient', r'ExactMeshArrangement\.Orient\('), ('arr.incircle', r'ExactMeshArrangement\.InCircle\(|InCircleFiltered'),
 ('arr.along', r'compare_along\(|along_approx'), ('arr.compare', r'ExactMeshArrangement\.ComparePoints\(|order_key'),
 ('orient2d', r'ExactPredicates\.Orient2d\('), ('orient3d', r'ExactPredicates\.Orient3d\(|ExactRayTriangle'),
 ('incircle', r'ExactPredicates\.InCircle\('), ('insphere', r'ExactPredicates\.InSphere\('), ('volume', r'ExactPredicates\.VolumeSign'),
 ('plane_side', r'plane_side|ExactLimbSigns\.PlaneSide|PreparedLimbs\.PlaneSide|ExactMeshBoolean\.orient3d_filtered|ExactMeshBoolean\.orient3d_filter|ExactMeshBoolean\.orient3d_exact'),
 ('limb.other', r'ExactLimbSigns\.(Dot|Det4|Orient2|InCircle)'),
]
ENTRY = [(k, re.compile(p)) for k, p in ENTRY]
CONSTRUCT = re.compile(r'compute_exact|ExactPoint3\.Create|input_exact|publish_exact|TryCreateLimbs|edge_plane_exact|segments_exact|FixedPoint3\.(LoadN|FromTwos)|ExactMeshArrangement\.Exact\(')
EXACT = re.compile(r'ExactPredicates\.(orient|incircle|insphere)\w*_(exact|bigint|int)|ExactPredicates\.\w+Exact|AdaptivePredicates|FixedArith\.|FixedPoint3|FixedInt|ExactLimbSigns\.|PreparedLimbs|System\.Numerics|exact_ref|ExactMeshArrangement\.exact_|ExactPoint3|Expansion\.')
LIB = 'geometry3Sharp!'
def analyse(path):
    d = json.load(open(path)); names = [f['name'] for f in d['shared']['frames']]
    isentry = [next((k for k, r in ENTRY if r.search(n)), None) for n in names]
    isc = [bool(CONSTRUCT.search(n)) for n in names]; isx = [bool(EXACT.search(n)) for n in names]
    islib = [n.startswith(LIB) for n in names]
    b = collections.Counter(); dts = []
    for p in d['profiles']:
        st = []; last = None
        for e in p['events']:
            if last is not None and st and e['at'] > last:
                dt = e['at'] - last
                if names[st[-1]] == 'CPU_TIME' and any(islib[f] for f in st):
                    dts.append(dt)
                    ent = next((isentry[f] for f in st if isentry[f]), None)
                    if any(isc[f] for f in st): b['construction' + ('' if ent is None else '|' + ent)] += dt
                    elif any(isx[f] for f in st): b['exact|' + (ent or 'other')] += dt
                    elif ent: b['filter|' + ent] += dt
                    else: b['other'] += dt
                    b['T'] += dt
                elif names[st[-1]] == 'UNMANAGED_CODE_TIME' and any(islib[f] for f in st): b['unmanaged_in_lib'] += dt
            last = e['at']
            if e['type'] == 'O': st.append(e['frame'])
            else:
                while st and st[-1] != e['frame']: st.pop()
                if st: st.pop()
    b['n_samples'] = len(dts)
    return b
def summary(b):
    T = b['T'] or 1e-9
    f = sum(v for k, v in b.items() if k.startswith('filter|'))
    ex = sum(v for k, v in b.items() if k.startswith('exact|'))
    c = sum(v for k, v in b.items() if k.startswith('construction'))
    return dict(T_ms=round(T, 1), samples=b['n_samples'], filter_pct=100 * f / T, exact_pct=100 * ex / T, construct_pct=100 * c / T)
if __name__ == '__main__':
    b = analyse(sys.argv[1])
    for k, v in sorted(b.items(), key=lambda kv: -kv[1]): print(f'{v:10.1f} {k}')
    print(summary(b))
