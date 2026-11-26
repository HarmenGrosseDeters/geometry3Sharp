"""Equivalence of the guarded and saturating FloorToInt/Long branches.

Model floor by f <= x < f + 1 with an integer f. In the unguarded
interior, the legacy cast is used only on representable integers.
The .NET 9 semantics (including NaN and infinities) are a premise.
"""
from pilot_obligations import main, source_contract, verify

SOURCE = 'math/MathUtil.cs'
ANCHORS = [
    ('internal static int FloorToIntSaturating', 'internal static int ToIntSaturating', '2a792b81d4d689875033089f555adcd5b07f2d7a434d0f1b20479286df1ec75b'),
    ('internal static long FloorToLongSaturating', 'public static bool IsFinite(double', '2d6b99af710f421b0bbc21821fea00c0fce76aee26c90b652d4d77ded846829a'),
]
GRID = 'implicit/CachingGridImplicit3d.cs'
GRID_ANCHORS = [
    ('if (x0 < 0 || x0 >= Grid.ni - 1 ||', 'if (gridPt.x < 0 || gridPt.x >= Grid.ni - 1 ||', '3cef0f0f4a40778e3354be4efb83e7404c80c39b2a4a3c468d72667c4bf081fa'),
]


def run(timeout_ms=120000):
    source_contract(SOURCE, ANCHORS)
    source_contract(GRID, GRID_ANCHORS)
    reports = {}
    for bits in (32, 64):
        limit = 2**(bits - 1)
        upper_guard = limit - 1 if bits == 32 else limit
        model = f"""
        (declare-const x Real)
        (declare-const f Int)
        (declare-const kind Int)
        (assert (and (<= 0 kind) (<= kind 3)))
        (assert (and (<= f x) (< x (+ f 1))))
        (define-fun lo () Int (- {limit}))
        (define-fun hi () Int {limit - 1})
        (define-fun modern () Int
          (ite (= kind 1) 0 (ite (= kind 2) hi (ite (= kind 3) lo
            (ite (> f hi) hi (ite (< f lo) lo f))))))
        (define-fun legacy () Int
          (ite (= kind 1) 0 (ite (= kind 2) hi (ite (= kind 3) lo
            (ite (>= x {upper_guard}) hi (ite (<= x lo) lo f))))))
        """
        reports[str(bits)] = verify('QF_LIRA', model, [
            ('legacy_cast_out_of_range', f'(and (= kind 0) (< lo x) (< x {upper_guard}) (or (< f lo) (> f hi)))', 'unsat'),
            ('branches_differ', '(not (= modern legacy))', 'unsat'),
            ('negative_fraction_reachable', '(and (= kind 0) (< x 0) (> x (- 1)) (= f (- 1)))', 'sat'),
            ('nan_zero_reachable', '(and (= kind 1) (= modern 0) (= legacy 0))', 'sat'),
        ], timeout_ms)
    grid = """
    (declare-const x Int)
    (declare-const n Int)
    (assert (and (<= 0 x) (<= x 2147483647) (<= 1 n) (<= n 2147483647)))
    (define-fun oldNext () Int (ite (= x 2147483647) (- 2147483648) (+ x 1)))
    """
    reports['grid'] = verify('QF_LIA', grid, [
        ('accepted_neighbor_out_of_bounds', '(and (< x (- n 1)) (>= (+ x 1) n))', 'unsat'),
        ('wrapped_neighbor_mutant', '(and (= x 2147483647) (< oldNext n) (>= (+ x 1) n))', 'sat'),
    ], timeout_ms)
    return dict(passed=all(r['passed'] for r in reports.values()), checks=reports)


if __name__ == '__main__':
    raise SystemExit(main(run))
