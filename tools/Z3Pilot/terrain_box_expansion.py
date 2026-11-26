"""Local enclosure check for TerrainSlopeDesign's box2.Expand call.

The reference box expands the toe bounds by the supplied dMax in exact
arithmetic. This checks padding and coordinate rounding, conditional on
dMax already bounding the geometric reach. It does not prove the dMax
formula bounds the geometry or that an inward boundary clips a face.
"""
from fractions import Fraction
import math

from continuous_collision import rational
from pilot_obligations import main, source_contract, verify

SOURCE = 'mesh_ops/TerrainSlopeDesign.cs'
ANCHORS = [
    ('double D0, maxXY, exy, eeval, rampShare, reachLowered;', 'double lambda = (lambdaReq',
     '49f59f5f5096ce123738726b71133a1fc066b2779234bd8b2b2cd3f5c09530f1'),
    ('double dMax = (H - zMin', 'double rFan = 1;',
     '7f5bd8647590754f53c66a259f2ededf71d8d5ff449ffd6d30cdaa10b606fd99'),
    ('D0 = (H - zMin + reachLowered)', 'rampShare = ramp_share',
     '0c0d7e8bca2fcdbdde70814aeb69cf03102d11c87901a5376b94ea12641bb2dc'),
    ('var box2 = toeBox;', 'var map = new SlopeMap(kernel);', '17ca83735ef43ee3cb3974e4ec4d7e5093e8019829ec8919c3eda1be38a35617'),
]
BOX = 'math/AxisAlignedBox2d.cs'
BOX_ANCHORS = [
    ('public void Expand(double fRadius)', 'public void Contract(double fRadius)', '95b5595344785c4f23feefe46147a9d287e75269b338017767ef3d6edf3dedd5'),
]


def replay_at_edge(edge, reach, width):
    radius = 1.05 * reach + 1e-6 * width
    stored = edge + radius
    exact = Fraction(edge) + Fraction(reach)
    outward = math.nextafter(stored, math.inf)
    return dict(edge=edge, width=width, reach=reach, radius=radius, stored=stored,
                exact=str(exact), reproduced=math.isfinite(stored) and Fraction(stored) < exact,
                outward=outward, outward_encloses=Fraction(outward) >= exact)


def replay_straight_toe():
    """Reconstruct the domain bound for a successful .NET straight-toe probe.

    Endpoints (2^52-20000,0,0) and (2^52,0,0), slope 1, height 5/8,
    tolerance 500. The public Compute() run selected delta=1e-6 and
    16 facets. The Python replay starts with those observed selections.
    """
    edge, width, height = float(2**52), 20_000.0, 0.625
    delta, facets = 1e-6, 16
    d0 = height / (1 - delta)
    exy = math.ulp(edge + 2 * d0)
    eeval = math.ulp(2 * height)
    min_feature = 2 * (8 * exy + 4 * eeval) / delta
    lambda_bound = 2 * (2 * exy + eeval) * (1 + 4 * 1.1 * d0 / min_feature)
    reach = (height + lambda_bound) / ((1 - delta) * math.cos(2 * math.pi / facets)) * 1.01
    return dict(height=height, tolerance=500, delta=delta, facets=facets,
                exy=exy, lambda_bound=lambda_bound, **replay_at_edge(edge, reach, width))


def run(timeout_ms=120000):
    source_contract(SOURCE, ANCHORS)
    source_contract(BOX, BOX_ANCHORS)
    u = Fraction(1, 2**53)
    eta = Fraction(1, 2**1075)
    # The exact value of the C# double literal, not 105/100.
    factor = Fraction(1.05)
    model = f"""
    (declare-const reach Real)
    (declare-const edge Real)
    (declare-const scaled Real)
    (declare-const pad Real)
    (declare-const radius Real)
    (declare-const stored Real)
    (declare-const spacing Real)
    (declare-const outward Real)
    (define-fun u () Real {rational(u)})
    (define-fun abs ((v Real)) Real (ite (< v 0) (- v) v))
    (define-fun close ((v Real) (w Real) (err Real)) Bool
      (<= (abs (- v w)) err))
    (assert (>= reach {rational(Fraction(1, 2**1022))}))
    (assert (>= pad 0))
    (assert (close scaled (* {rational(factor)} reach)
      (+ (* u {rational(factor)} reach) {rational(eta)})))
    (assert (close radius (+ scaled pad) (* u (abs (+ scaled pad)))))
    (assert (close stored (+ edge radius) (* u (abs (+ edge radius)))))
    (define-fun localScale () Bool (<= (abs edge) (* {2**40} reach)))
    """
    # Reflection gives the lower face from the same lemma.
    result = verify('QF_LRA', model, [
        ('guard_reachable', 'localScale', 'sat'),
        ('local_scale_encloses', '(and localScale (< stored (+ edge reach)))', 'unsat'),
        ('unrestricted_offset_can_shrink', '(< stored (+ edge reach))', 'sat'),
        ('local_spacing_guard_encloses',
         '(and (> spacing 0) (>= reach (* 11 spacing)) '
         '(close stored (+ edge radius) (/ spacing 2)) (< stored (+ edge reach)))', 'unsat'),
        ('outward_face_encloses',
         '(and (>= outward (+ edge radius)) (< outward (+ edge reach)))', 'unsat'),
    ], timeout_ms)
    result['binary64_replay'] = replay_at_edge(float(2**52), 4.2, 20_000.0)
    result['straight_toe_replay'] = replay_straight_toe()
    for key in ('binary64_replay', 'straight_toe_replay'):
        case = result[key]
        result['passed'] &= case['reproduced'] and case['outward_encloses']
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
