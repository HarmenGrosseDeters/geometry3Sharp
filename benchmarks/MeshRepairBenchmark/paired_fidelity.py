"""Strict paired acceptance for the predefined alpha-wrap quality experiment."""
import argparse
import json
import math
import statistics
from criteria import repaired

HIGH = {'iou', 'fm_nc', 'fm_viou', 'fm_oviou'}
HIGH_PREFIX = ('fm_f_', 'fm_p_', 'fm_r_', 'fm_ef_', 'fm_ep_', 'fm_er_', 'fm_of_', 'fm_or_', 'fm_oer_')
SCORES = {prefix + tag for prefix in HIGH_PREFIX for tag in ('0001', '0005', '001')}
LOW = {'hd_fwd_hi_rel', 'hd_back_hi_rel', 'iou_missing', 'iou_extra', 'fm_max_fwd', 'fm_max_back',
       'fm_outer_mean', 'fm_outer_max', 'fm_chamfer', 'fm_ecd', 'fm_ecd_fwd', 'fm_ecd_back',
       'fm_viou_missing', 'fm_viou_extra', 'fm_oviou_missing', 'fm_oviou_extra',
       'fm_h_err', 'fm_k_err', 'fm_h_w1', 'fm_k_w1', 'fm_nangle_fwd', 'fm_nangle_back',
       'fm_reversed_fwd', 'fm_reversed_back'}
DIST = {'minangle': True, 'sj': True, 'maxangle': False, 'aspect': False, 'cond': False}
BINS = ('minangle_lt30', 'minangle_lt10', 'minangle_lt1', 'maxangle_gt90', 'maxangle_gt160',
        'aspect_gt13', 'sj_lt05', 'cond_gt13')
ZERO = ('after_boundary_edges', 'after_bowties', 'after_degenerate_tris', 'after_duplicate_tris',
        'after_inconsistent_orient_edges', 'after_inverted_components', 'after_selfint_exact')


def finite(value):
    if isinstance(value, bool):
        return None
    try:
        value = float(value)
        return value if math.isfinite(value) else None
    except (TypeError, ValueError):
        return None


def read_rows(path):
    with open(path, encoding='utf-8-sig') as stream:
        return [json.loads(line) for line in stream if line.strip()]


def compare(before, after, unsimplified, expected=400, subset=False, require_certificate=False):
    failures, diagnostic = [], []
    def safe(value):
        if isinstance(value, float) and not math.isfinite(value):
            return str(value)
        if isinstance(value, list):
            return [safe(item) for item in value]
        if isinstance(value, dict):
            return {key: safe(item) for key, item in value.items()}
        return value
    def fail(file, metric, a=None, b=None):
        failures.append({'file': file, 'metric': metric, 'baseline': safe(a), 'candidate': safe(b)})
    def index(rows, label):
        result = {}
        for row in rows:
            key = row.get('file')
            if not isinstance(key, str) or not key or key in result:
                fail('*', label + ': duplicate or missing ID')
                continue
            result[key] = row
        if len(rows) != expected:
            fail('*', label + ': row count', expected, len(rows))
        return result
    b = index(before, 'baseline')
    u = index(unsimplified, 'unsimplified')
    c = {}
    for row in after:
        key = row.get('file')
        if not isinstance(key, str) or not key or key in c:
            fail('*', 'candidate: duplicate or missing ID')
            continue
        c[key] = row
    wrap = {key for key, row in b.items() if row.get('robust_used_wrap') is True}
    wrap |= {key for key, row in c.items() if row.get('robust_used_wrap') is True}
    wanted = wrap if subset else set(b)
    if set(c) != wanted:
        fail('*', 'candidate ID set', sorted(wanted), sorted(c))
    if set(b) != set(u):
        fail('*', 'unsimplified ID set', sorted(b), sorted(u))
    if not subset and len(after) != expected:
        fail('*', 'candidate row count', expected, len(after))

    def relative(file, metric, av, bv, high=False):
        a, z = finite(av), finite(bv)
        if a is None or z is None:
            fail(file, metric + ': missing/nonfinite', av, bv)
        elif (z < .95 * a if high else z > 1.05 * a):
            fail(file, metric, a, z)

    for file in sorted(wanted):
        if file not in b or file not in c:
            fail(file, 'unpaired result')
            continue
        a, z = b[file], c[file]
        if a.get('status') != z.get('status') or z.get('status') not in ('closed', 'not_closed', 'emptied'):
            fail(file, 'status', a.get('status'), z.get('status'))
        for key in ('apply_returned', 'after_valid', 'after_closed'):
            if key not in a or key not in z or (a.get(key) is True and z.get(key) is not True):
                fail(file, key, a.get(key), z.get(key))
        try:
            if repaired(a) and not repaired(z):
                fail(file, 'lost repaired case', True, False)
        except (TypeError, ValueError):
            fail(file, 'repaired criterion unavailable')
        for key in ZERO:
            if key not in a or key not in z or (a.get(key) == 0 and z.get(key) != 0):
                fail(file, key, a.get(key), z.get(key))
        if a.get('fm_ok') is not True or z.get('fm_ok') is not True:
            fail(file, 'fm_ok', a.get('fm_ok'), z.get('fm_ok'))
        for key in ('fm_topo_out', 'fm_topo_changes', 'robust_topology_changed'):
            if key not in a or key not in z or a.get(key) != z.get(key):
                fail(file, key, a.get(key), z.get(key))
        judged = set()
        keys = LOW | HIGH | SCORES | {key for key in set(a) | set(z) if key.startswith(HIGH_PREFIX)}
        for key in sorted(keys):
            judged.add(key)
            if key.startswith(('fm_ef_', 'fm_ep_', 'fm_er_', 'fm_oer_')) or key.startswith('fm_ecd'):
                if finite(a.get('fm_sharp_in')) == 0:
                    diagnostic.append({'file': file, 'metric': key, 'reason': 'no reference sharp edges'})
                    continue
                if key.startswith(('fm_ef_', 'fm_ep_')) or key.startswith('fm_ecd'):
                    if finite(a.get('fm_sharp_out')) == 0:
                        diagnostic.append({'file': file, 'metric': key, 'reason': 'no baseline result sharp edges'})
                        continue
                if key.startswith('fm_oer_') and finite(a.get('fm_outer_edge_share')) == 0:
                    diagnostic.append({'file': file, 'metric': key, 'reason': 'no baseline outer edges'})
                    continue
            if key.startswith(('fm_of_', 'fm_or_')) or key in ('fm_outer_mean', 'fm_outer_max'):
                if finite(a.get('fm_outer_share')) == 0:
                    diagnostic.append({'file': file, 'metric': key, 'reason': 'no baseline outer samples'})
                    continue
            if key.startswith('fm_oviou'):
                converged, share = a.get('fm_outer_converged'), finite(a.get('fm_outer_face_share'))
                if type(converged) is not bool or share is None or not 0 <= share <= 1:
                    fail(file, key + ': applicability metadata unavailable',
                         [a.get('fm_outer_converged'), a.get('fm_outer_face_share')])
                    continue
                if converged is False or share == 0:
                    diagnostic.append({'file': file, 'metric': key, 'reason': 'outer solid unavailable in baseline'})
                    continue
            if key.startswith(('fm_viou', 'fm_oviou')):
                family = 'fm_oviou' if key.startswith('fm_oviou') else 'fm_viou'
                if all(a.get(family + suffix) == 'NaN' for suffix in ('', '_missing', '_extra')):
                    diagnostic.append({'file': file, 'metric': key, 'reason': family + ' reference without a solid'})
                    continue
            if key in ('iou', 'iou_missing', 'iou_extra') and a.get('iou') == 'NaN':
                diagnostic.append({'file': file, 'metric': key, 'reason': 'legacy input without a solid'})
                continue
            relative(file, key, a.get(key), z.get(key), key in HIGH or key.startswith(HIGH_PREFIX))
        aq, zq = a.get('fm_q_out') or {}, z.get('fm_q_out') or {}
        relative(file, 'fm_q_out.degenerate', aq.get('degenerate'), zq.get('degenerate'))
        for key, high in DIST.items():
            av, zv = aq.get(key), zq.get(key)
            if not isinstance(av, list) or not isinstance(zv, list) or len(av) != 4 or len(zv) != 4:
                fail(file, 'fm_q_out.' + key + ': incomplete distribution', av, zv)
                continue
            for n in range(4):
                relative(file, 'fm_q_out.' + key + '[' + str(n) + ']', av[n], zv[n], high)
        for key in BINS:
            at, zt = finite(aq.get('tris')), finite(zq.get('tris'))
            av, zv = finite(aq.get(key)), finite(zq.get(key))
            relative(file, 'fm_q_out.' + key + '/tris', av / at if av is not None and at and at > 0 else None,
                     zv / zt if zv is not None and zt and zt > 0 else None)
        for key in sorted(set(a) | set(z)):
            if key.startswith('fm_') and key not in judged and key not in ('fm_q_out', 'fm_topo_out', 'fm_topo_changes'):
                diagnostic.append({'file': file, 'metric': key, 'reason': 'reference metadata/nonmonotonic or inapplicable'})
        if file in wrap and require_certificate:
            relative_limit = finite(z.get('wrap_certificate_hi_over_offset'))
            if relative_limit is None or relative_limit > .5:
                fail(file, 'wrap certificate UpperBound/offset <= 0.5', .5, z.get('wrap_certificate_hi_over_offset'))

    angles, ratios, base_time, candidate_time, simplify = [], [], [], [], []
    for file in sorted(wanted):
        bt, ct = finite(b.get(file, {}).get('repair_ms')), finite(c.get(file, {}).get('repair_ms'))
        if bt is None or bt <= 0 or ct is None or ct < 0:
            fail(file, 'paired repair time unavailable', b.get(file, {}).get('repair_ms'),
                 c.get(file, {}).get('repair_ms'))
            continue
        base_time.append(bt); candidate_time.append(ct)
    for file in sorted(wrap):
        a, z, raw = b.get(file, {}), c.get(file, {}), u.get(file, {})
        angle = (z.get('fm_q_out') or {}).get('minangle', [])
        angle = finite(angle[1]) if isinstance(angle, list) and len(angle) == 4 else None
        tris, original = finite(z.get('after_tris')), finite(raw.get('after_tris'))
        if raw.get('robust_used_wrap') is not True:
            fail(file, 'unsimplified control did not take wrap path')
        if angle is None or tris is None or not original or original <= 0:
            fail(file, 'aggregate measurement unavailable')
            continue
        angles.append(angle); ratios.append(tris / original)
        sm = finite((z.get('robust_phases') or {}).get('wrap.simplify'))
        if sm is not None:
            simplify.append(sm)
    aggregate = {'wrap_cases': len(wrap), 'paired_cases': len(angles), 'time_paired_cases': len(base_time),
                 'angle_median': statistics.median(angles) if angles else None,
                 'triangle_ratio_median': statistics.median(ratios) if ratios else None,
                 'repair_time_ratio': sum(candidate_time) / sum(base_time) if base_time else None,
                 'repair_ms_baseline': sum(base_time), 'repair_ms_candidate': sum(candidate_time),
                 'simplify_ms_median': statistics.median(simplify) if simplify else None,
                 'simplify_ms_max': max(simplify) if simplify else None}
    for metric, limit, high in [('angle_median', 10, True), ('triangle_ratio_median', .4, False), ('repair_time_ratio', 1.25, False)]:
        value = aggregate[metric]
        if value is None or (value < limit if high else value > limit):
            fail('*', metric, limit, value)
    return {'pass': not failures, 'subset_only': subset, 'certificate_required': require_certificate,
            'aggregate': aggregate, 'failures': failures, 'diagnostic': diagnostic}


def main(args):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('before'); parser.add_argument('after'); parser.add_argument('unsimplified')
    parser.add_argument('--expected', type=int, default=400)
    parser.add_argument('--subset', action='store_true')
    parser.add_argument('--require-certificate', action='store_true')
    options = parser.parse_args(args)
    result = compare(read_rows(options.before), read_rows(options.after), read_rows(options.unsimplified),
                     options.expected, options.subset, options.require_certificate)
    print(json.dumps(result, indent=2, allow_nan=False))
    return 0 if result['pass'] else 1
