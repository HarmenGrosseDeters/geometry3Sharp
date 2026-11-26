"""Local transition of the proposed coplanar propagation, under label completeness.

The design relies on constraints surviving into the triangulation. This
pilot checks the consumer of the labels and separates that premise from
the implementation of the constraint producers.
"""
from check_pilot_obligations import check, main, report, source

SOURCE = 'docs/design/COPLANAR_PROPAGATION.md'
CONTRACTS = [
    ('2. **Across an edge e from p to q:**', '3. **Decisions stay per piece.**', '3c3bd2479b4c64fb21457088872a2e5fd6f1caa9409e6206b2d41aba2ef5d003'),
    ('**Labels (preferred, no arithmetic).**', 'make the first nonzero entry', 'a18b255f5d8fc432220a1c6d6846fe2cb888b9765f8b8afbbce9be64f3a7ffbe'),
]
PRODUCER = 'mesh_ops/ExactMeshArrangement.cs'
PRODUCER_CONTRACTS = [
    ('any |= clip_into(T, U.Corners[k]', 'if (any) {', '27c0b15d5c0bb5c59f88040ac54b2fb82143dd858ca4c8bc757633261adca6db'),
    ('lock (T.Lock) { T.AddSegment(new Segment(lo, hi, 4 * ui))', '// Devillers and Guigue (2002), the proper case:', '5d4ad800cb81e7ac2ba5e8f5b52f4cbe5aee4934a04d8a2ceac3334218e18cee'),
]

LABELS = """
(declare-const id Int)
(declare-const j Int)
(declare-const edge Int)
(declare-const other Int)
(assert (and (<= 0 id) (<= id 536870911) (<= 0 j) (<= j 536870911)))
(assert (and (<= 0 edge) (<= edge 2) (<= 0 other) (<= other 2)))
(define-fun label ((i Int) (k Int)) Int (+ (* 4 i) 1 k))
(define-fun planeLabel ((i Int)) Int (* 4 i))
"""

# One arbitrary candidate is enough to prove the pointwise membership
# transition; it composes over every candidate and therefore over walks.
TRANSITION = """
(declare-const previous Bool)
(declare-const exactNext Bool)
(declare-const listed Bool)
(declare-const previousWinding Int)
(declare-const exactNextWinding Int)
(define-fun exactWindingChanges () Bool (distinct previousWinding exactNextWinding))
(declare-const trianglePlaneLabel Bool)
(define-fun next () Bool (ite listed exactNext previous))
(define-fun nextWinding () Int
    (ite trianglePlaneLabel exactNextWinding previousWinding))
(define-fun complete () Bool
    (and (=> (distinct previous exactNext) listed)
         (=> exactWindingChanges trianglePlaneLabel)))
"""


def run(timeout_ms=120000):
    for item in CONTRACTS:
        source(SOURCE, *item, prose=True)
    for item in PRODUCER_CONTRACTS:
        source(PRODUCER, *item)
    checks = [
        ('label_owner_or_kind_corrupt', 'QF_LIA', LABELS,
         '(or (distinct (div (label id edge) 4) id) (= (mod (label id edge) 4) 0) '
         '(> (label id edge) 2147483647) (= (label id edge) (planeLabel j)) '
         '(distinct (div (planeLabel id) 4) id) '
         '(and (= (label id edge) (label j other)) (or (distinct id j) (distinct edge other))))', 'unsat'),
        ('complete_label_retest_reachable', 'QF_LIA', TRANSITION,
         '(and complete listed (distinct previous exactNext))', 'sat'),
        ('membership_differs_with_complete_labels', 'QF_LIA', TRANSITION,
         '(and complete (distinct next exactNext))', 'unsat'),
        ('winding_reused_across_crossing', 'QF_LIA', TRANSITION,
         '(and complete (distinct nextWinding exactNextWinding))', 'unsat'),
        ('dropped_coplanar_label_can_change_representative', 'QF_LIA', TRANSITION,
         # Take this candidate as the only lower-id member: representative
         # is then equivalent to its absence.
         '(and (not previous) exactNext (not listed) (distinct (not next) (not exactNext)))', 'sat'),
        ('dropped_triangle_plane_label_can_reuse_wrong_winding', 'QF_LIA', TRANSITION,
         '(and (not trianglePlaneLabel) (distinct nextWinding exactNextWinding))', 'sat'),
    ]
    return report([check(*row, timeout_ms) for row in checks])


if __name__ == '__main__':
    raise SystemExit(main(run))
