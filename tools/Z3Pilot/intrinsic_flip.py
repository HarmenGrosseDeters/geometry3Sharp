"""Integer arithmetic of the local normal-coordinate flip.

The model overapproximates valid mesh states by using the five stored
edge counts in [-1, int.MaxValue]. A -1 denotes an input edge. A concrete
case with all counts -1 describes a two-triangle input diamond and
exposes how that marker behaves after a reverse flip. The checks cover
counts. The reverse edge order uses the relocation documented in
IntrinsicTriangulation.FlipEdge. Geometric flip guards, topology,
roundabouts and extraction are outside the model.
"""
from pilot_obligations import main, source_contract, verify

SOURCE = 'mesh_ops/IntrinsicIntegerCoordinates.cs'
ANCHORS = [
    ('static long pos(', 'int wrap_roundabout(', '6b9884c4e3990ac2d2d1401384668052fd0d9e0ff78f3946d7d9185009be9f5e'),
    ('long flipped_coordinate_twice(', 'void set_boundary_split_coordinates(', '8a1b8d49dd9ae58f5ff43398f052572d93573a3b10738e9a9ef5a4deb2b8cd91'),
]

MODEL = """
(define-fun pos ((x Int)) Int (ite (< x 0) 0 x))
(define-fun neg ((x Int)) Int (pos (- x)))
(define-fun abs_i ((x Int)) Int (+ (pos x) (neg x)))
(define-fun emanating ((a Int) (b Int) (c Int)) Int
  (pos (- (pos b) (+ (pos a) (pos c)))))
(define-fun numerator ((a Int) (b Int) (c Int)) Int
  (- (pos (- (+ (pos a) (pos c)) (pos b)))
     (+ (emanating c a b) (emanating b c a))))
(define-fun corner ((a Int) (b Int) (c Int)) Int
  (div (numerator a b c) 2))
(define-fun twice ((ij Int) (jk Int) (ki Int) (il Int) (lj Int)) Int
  (+ (* 2 (corner lj ij il)) (* 2 (corner ki ij jk))
     (abs_i (- (corner ij il lj) (corner jk ki ij)))
     (abs_i (- (corner il lj ij) (corner ij jk ki)))
     (- (emanating lj ij il)) (- (emanating ki ij jk))
     (* 2 (+ (emanating il lj ij) (emanating ij jk ki)
             (emanating ij il lj) (emanating jk ki ij)))
     (* 2 (neg ij))))
; Sum of magnitudes of the addends bounds every partial sum in C#.
(define-fun budget ((ij Int) (jk Int) (ki Int) (il Int) (lj Int)) Int
  (+ (* 2 (corner lj ij il)) (* 2 (corner ki ij jk))
     (abs_i (- (corner ij il lj) (corner jk ki ij)))
     (abs_i (- (corner il lj ij) (corner ij jk ki)))
     (emanating lj ij il) (emanating ki ij jk)
     (* 2 (+ (emanating il lj ij) (emanating ij jk ki)
             (emanating ij il lj) (emanating jk ki ij)))
     (* 2 (neg ij))))
(define-fun representable ((x Int)) Bool (and (<= (- 1) x) (<= x 2147483647)))
(declare-const ij Int)
(declare-const jk Int)
(declare-const ki Int)
(declare-const il Int)
(declare-const lj Int)
(assert (and (representable ij) (representable jk) (representable ki)
             (representable il) (representable lj)))
(define-fun forward () Int (twice ij jk ki il lj))
(define-fun backward () Int (twice (div forward 2) lj jk ki il))
"""


def run(timeout_ms=120000):
    source_contract(SOURCE, ANCHORS)
    integer = verify('QF_LIA', MODEL, [
        ('corner_csharp_division_domain', '(< (numerator ij jk ki) 0)', 'unsat'),
        ('long_accumulation_range', '(> (budget ij jk ki il lj) 68719476736)', 'unsat'),
        ('input_edge_marker_round_trip',
         '(and (= ij (- 1)) (= jk (- 1)) (= ki (- 1)) (= il (- 1)) (= lj (- 1)) '
         '(= forward 2) (= backward (- 2)))', 'sat'),
        ('overflow_guard_reachable_in_model', '(> forward 4294967294)', 'sat'),
    ], timeout_ms)
    # With integral corner numerators the divisions in C# are exact.
    # Check the identity in the larger rational domain to avoid SMT
    # branching on parity. Apply it only where both calls have integral
    # corner numerators and the first stored count is representable.
    rational_model = MODEL.replace('Int', 'Real').replace('(div ', '(/ ')
    reverse = verify('QF_LRA', rational_model, [
        ('supported_input_reachable',
         '(and (= ij 3) (= jk 2) (= ki 1) (= il 1) (= lj 2) (representable (/ forward 2)))', 'sat'),
        ('round_trip_mismatch',
         '(and (representable (/ forward 2)) (not (= backward (* 2 ij))))', 'unsat'),
    ], timeout_ms)
    return dict(passed=integer['passed'] and reverse['passed'], integer=integer, reverse=reverse)


if __name__ == '__main__':
    raise SystemExit(main(run))
