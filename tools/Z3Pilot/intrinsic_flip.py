"""Integer arithmetic of the local normal-coordinate flip.

The model overapproximates valid mesh states by using the five stored
edge counts in [-1, int.MaxValue]. A -1 denotes an input edge. A concrete
case with all counts -1 describes a two-triangle input diamond and
exposes how that marker behaves after a reverse flip. The checks cover
counts. The reverse edge order uses the relocation documented in
IntrinsicTriangulation.FlipEdge. Geometric flip guards, topology,
roundabouts and extraction are outside the model.
"""
from check_pilot_obligations import main, source_contract, verify

SOURCE = 'mesh_ops/IntrinsicIntegerCoordinates.cs'
ANCHORS = [
    ('static long pos(', 'int wrap_roundabout(', '6b9884c4e3990ac2d2d1401384668052fd0d9e0ff78f3946d7d9185009be9f5e'),
    ('long flipped_coordinate_twice(', 'void set_boundary_split_coordinates(', '8a1b8d49dd9ae58f5ff43398f052572d93573a3b10738e9a9ef5a4deb2b8cd91'),
]

MODEL_TEMPLATE = """
(define-fun pos ((x {sort})) {sort} (ite (< x 0) 0 x))
(define-fun neg ((x {sort})) {sort} (pos (- x)))
(define-fun abs_i ((x {sort})) {sort} (+ (pos x) (neg x)))
(define-fun emanating ((a {sort}) (b {sort}) (c {sort})) {sort}
  (pos (- (pos b) (+ (pos a) (pos c)))))
(define-fun numerator ((a {sort}) (b {sort}) (c {sort})) {sort}
  (- (pos (- (+ (pos a) (pos c)) (pos b)))
     (+ (emanating c a b) (emanating b c a))))
(define-fun corner ((a {sort}) (b {sort}) (c {sort})) {sort}
  ({half} (numerator a b c) 2))
(define-fun twice ((ij {sort}) (jk {sort}) (ki {sort}) (il {sort}) (lj {sort})) {sort}
  (+ (* 2 (corner lj ij il)) (* 2 (corner ki ij jk))
     (abs_i (- (corner ij il lj) (corner jk ki ij)))
     (abs_i (- (corner il lj ij) (corner ij jk ki)))
     (- (emanating lj ij il)) (- (emanating ki ij jk))
     (* 2 (+ (emanating il lj ij) (emanating ij jk ki)
             (emanating ij il lj) (emanating jk ki ij)))
     (* 2 (neg ij))))
; Sum of magnitudes of the addends bounds every partial sum in C#.
(define-fun budget ((ij {sort}) (jk {sort}) (ki {sort}) (il {sort}) (lj {sort})) {sort}
  (+ (* 2 (corner lj ij il)) (* 2 (corner ki ij jk))
     (abs_i (- (corner ij il lj) (corner jk ki ij)))
     (abs_i (- (corner il lj ij) (corner ij jk ki)))
     (emanating lj ij il) (emanating ki ij jk)
     (* 2 (+ (emanating il lj ij) (emanating ij jk ki)
             (emanating ij il lj) (emanating jk ki ij)))
     (* 2 (neg ij))))
(define-fun representable ((x {sort})) Bool (and (<= (- 1) x) (<= x 2147483647)))
(declare-const ij {sort})
(declare-const jk {sort})
(declare-const ki {sort})
(declare-const il {sort})
(declare-const lj {sort})
(assert (and (representable ij) (representable jk) (representable ki)
             (representable il) (representable lj)))
(define-fun forward () {sort} (twice ij jk ki il lj))
(define-fun backward () {sort} (twice ({half} forward 2) lj jk ki il))
"""


def run(timeout_ms=120000):
    source_contract(SOURCE, ANCHORS)
    integer = verify('QF_LIA', MODEL_TEMPLATE.format(sort='Int', half='div'), [

        ('long_accumulation_range', '(> (budget ij jk ki il lj) 68719476736)', 'unsat'),
        ('use_int32_accumulator_mutant', '(> (budget ij jk ki il lj) 2147483647)', 'sat'),
        ('input_edge_marker_round_trip',
         '(and (= ij (- 1)) (= jk (- 1)) (= ki (- 1)) (= il (- 1)) (= lj (- 1)) '
         '(= forward 2) (= backward (- 2)))', 'sat'),
        ('overflow_guard_reachable_in_model', '(> forward 4294967294)', 'sat'),
    ], timeout_ms)
    # With integral corner numerators the divisions in C# are exact.
    # Check the identity in the larger rational domain to avoid SMT
    # branching on parity. Apply it only where both calls have integral
    # corner numerators and the first stored count is representable.
    rational_model = MODEL_TEMPLATE.format(sort='Real', half='/')
    reverse = verify('QF_LRA', rational_model, [
        ('supported_input_reachable',
         '(and (= ij 3) (= jk 2) (= ki 1) (= il 1) (= lj 2) (representable (/ forward 2)))', 'sat'),
        ('reverse_role_permutation_mutant',
         '(and (representable (/ forward 2)) (not (= (twice (/ forward 2) jk lj ki il) (* 2 ij))))', 'sat'),
        ('round_trip_mismatch',
         '(and (representable (/ forward 2)) (not (= backward (* 2 ij))))', 'unsat'),
    ], timeout_ms)
    return dict(passed=integer['passed'] and reverse['passed'], integer=integer, reverse=reverse)


if __name__ == '__main__':
    raise SystemExit(main(run))
