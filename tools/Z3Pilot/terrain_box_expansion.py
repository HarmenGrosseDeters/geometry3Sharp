"""Local enclosure check for TerrainSlopeDesign's box2.Expand call.

The reference box expands the toe bounds by the supplied dMax in exact
arithmetic. This checks padding and coordinate rounding, conditional on
dMax already bounding the geometric reach. It does not prove the dMax
formula, face classification, or reachability of a full design state.
"""
from fractions import Fraction
import math

from continuous_collision import rational
from pilot_obligations import main, source_contract, verify

SOURCE = 'mesh_ops/TerrainSlopeDesign.cs'
ANCHORS = [
    ('var box2 = toeBox;', 'var map = new SlopeMap(kernel);', '17ca83735ef43ee3cb3974e4ec4d7e5093e8019829ec8919c3eda1be38a35617'),
]
BOX = 'math/AxisAlignedBox2d.cs'
BOX_ANCHORS = [
    ('public void Expand(double fRadius)', 'public void Contract(double fRadius)', '95b5595344785c4f23feefe46147a9d287e75269b338017767ef3d6edf3dedd5'),
]


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
    ], timeout_ms)
    edge, reach, width = float(2**52), 4.2, 20_000.0
    radius = 1.05 * reach + 1e-6 * width
    stored = edge + radius
    exact = Fraction(edge) + Fraction(reach)
    replayed = math.isfinite(stored) and Fraction(stored) < exact
    result['passed'] &= replayed
    result['binary64_replay'] = dict(edge=edge, width=width, reach=reach, radius=radius,
                                    stored=stored, exact=str(exact), reproduced=replayed)
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
