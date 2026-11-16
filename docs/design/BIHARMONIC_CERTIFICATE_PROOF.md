# Euclidean certificate for moving meshes

This is a sufficient a posteriori certificate, not an assertion that every input
can be certified within a fixed time or precision budget. It has no mathematical
16-vertex limit. The production dense solver still has that implementation limit.
The experimental scalar verifier currently caps its free system at 2500 unknowns.

## Objects and premises

Consider one connected component of nondegenerate triangles with positive lumped
masses and at least one hard positional handle. All inputs and returned positions
are interpreted as their exact binary64 values. The component and free/handle
partition must be complete. Let s > 0 be the same exact binary64 normalization
used throughout construction and verification. Normalized rest coordinates are
formed from original coordinates using exact mathematical subtraction/division,
enclosed outward by the verifier, not from previously rounded normalized points.

Let L be the symmetric cotangent stiffness matrix, M the positive barycentric
lumped mass matrix, Q = L^T M^-1 L, A = Q_ff and b_j = -Q_fc u_c,j. Here handle
displacements u_c,j are exact target-minus-original differences divided by s.
Signed cotangent weights are allowed. The triangle FEM energy is nonnegative and
its kernel on a connected nondegenerate component consists of constants. A zero
quadratic form for a vector with zero handles therefore forces that vector to be
zero. Thus A is positive definite. Independently verifying its positive spectral
gap below provides a quantitative, machine-checkable premise as well.

Define the candidate normalized free displacement as u_hat_i =
(returned_position_i - original_position_i)/s. Subtraction is interpreted exactly;
all final world-coordinate rounding is therefore included in the candidate.
Handles must equal the requested targets; an unconstrained or mismatched input
must not reach certificate acceptance.

## Verified spectral gap

Choose a finite binary64 mu > 0. An estimate may choose it but cannot prove it.
Assemble a symmetric interval enclosure of A - mu I from the original data,
including all structural contributions. Permutation preserves eigenvalues.
Let B be its binary64 midpoint matrix and rho an outward upper bound on the
maximum symmetric radius row sum. Then every enclosed matrix differs from B in
spectral norm by at most rho, since the radius is symmetric and nonnegative.

Apply Rump's sufficient shifted-Cholesky criterion (2006, Corollary 2.7 and
section 3 bounds I and II). For n free unknowns, eps = 2^-53 and
gamma = (n+1)*eps/(1-(n+1)*eps), a no-underflow trace correction is
c >= gamma/(1-gamma)*trace(B), subject to the theorem's gamma conditions.
Production also evaluates the envelope-aware bound II. In lower-triangular
storage, let t_i = max(i-j) over all retained entries (i,j) in row i, including
structural zeros. This bounds the envelope of upper Cholesky column i. Set
k_i=t_i+2, beta_i=gamma_(k_i). In the same exact 2^-1074 units, an outward bound is

    c_II >= sum_i B_ii * k_i / (2^53-2*k_i).

Each positive term is rounded upward separately with integer division. The smaller
of this bound and the separately valid global bound I is used. A structural
superset only enlarges t_i, so retained zero weights are conservative. No computed
factor value or imported symbolic count supplies this correction. The internal
control mode keeps bound I for comparisons. The existing arithmetic/range and
complete-pattern premises apply equally to both bounds; no supported size or
resource cap changes in this step.

Construct C with the same off-diagonals as B and C_ii <= B_ii-c-rho, rounding
downward. Successful scalar Cholesky under the theorem's arithmetic assumptions
proves positive definiteness for every matrix in the enclosure, hence
lambda_min(A) > mu. Completion alone, without c and rho, is insufficient.

The pilot evaluates c, rho and diagonal comparisons with exact rationals and
outward conversion. Its scalar recurrence uses direct division. Audit mode checks
every executed product, subtraction, division and square root against exact
nearest-even rounding cells decoded from binary64 bits. Nonfinite results,
subnormal results, nonzero products/quotients rounded to zero, and exactly
subnormal products/quotients rounded to the minimum normal value are rejected.
This excludes the underflow terms for an accepted audited run. The audit does
not prove unexecuted operations, another runtime or another input; integer
arithmetic, bit decoding and the audit itself remain trusted implementation.
Without the audit, the documented IEEE execution assumptions remain prerequisites.
Finite positive diagonals and gamma < 1 are enforced by input/size checks.

The bounded production fixed-point verifier can instead use a cloned permutation
from the numerical SparseCholesky. It validates length, range and uniqueness and
uses its inverse only when assembling the interval matrix in the new ordering.
Original L/M, handles, candidate and residual remain in their original indexing.
No double factor value or numerical `IsValid` flag is used as a proof. Permutation
preserves the spectrum; positive interval pivots remain the proof premise. A
retained proof for unchanged geometry/free-map need not match a later proposal's
ordering. The numerical ordering snapshot is read only when new preparation is
needed, not copied for every target update.

Reference: S. M. Rump, *Verification of Positive Definiteness*, BIT Numerical
Mathematics 46 (2006), 433–452, https://www.tuhh.de/ti3/paper/rump/Ru06c.pdf.
The prototype uses the theorem, not a source-code port. Interval assembly must
independently satisfy outward arithmetic and completeness; successful verification
of an imported matrix alone cannot establish those properties for a mesh.

## Final-position error

For each coordinate j in {x,y,z}, enclose r_j = b_j - A u_hat_j using the
unshifted original-data L/M operator. Let R_j >= ||r_j||_2 be an outward upper
bound. Since ||A^-1||_2 < 1/mu,

    ||u_hat_j - u*_j||_2 <= R_j/mu <= e_j.

Consequently, every free vertex has Euclidean world-position error at most

    E = s * sqrt(e_x^2 + e_y^2 + e_z^2).

This uses the actual three coordinate bounds, not a claim that a coordinate bound
also bounds the vector length. Residual norms and division must be enclosed
outward; small numerical residuals alone are not certificates.

## Agreed absolute and relative contract

Let S be the maximum Euclidean rest distance from the component's lowest vertex
ID, D the maximum Euclidean candidate free displacement, and D* the corresponding
maximum exact-minimizer displacement. The reverse triangle inequality gives
D* >= max(D-E,0). For positive tolerances p and t, accept only if

    E^2 <= p^2 S^2,
    E^2 (1+t)^2 <= t^2 D^2.

All terms are nonnegative, so the second inequality is equivalent to
E <= t(D-E). It implies E <= t D*. Thus each free vertex meets the absolute
position limit and its error is bounded relative to the maximum response in its
component. It is not a relative guarantee for each individual vertex.

S^2 and D^2 can be calculated exactly from binary64 coordinate differences;
e_j may be finite rational interval endpoints. Both comparisons then use exact
rationals without square roots or rounded thresholds. If D=0 and E>0, relative
acceptance fails. If E=0, both tests accept, including an exactly proven zero
response. The separate algebraic zero-handle-motion path already proves that case.

Repeat independently for every component. A large response in one component
cannot mask a missing response in another. Euclidean metrics are invariant under
exact rigid rotation, but rounded input transformations and enclosure widths may
change whether a particular implementation establishes the sufficient test.

## Scope and remaining engineering

The combined scalar-audit pilot constructs the original-data intervals in memory
and checks the final all-axis candidate. Adding the two comparisons above extends
its certificate to the agreed contract for its single-component fixtures. It is
independent of the production supernodal candidate solver; the same argument can
check its output if all premises are validated against that actual mesh/output.

There is no proof here of a particular runtime budget or acceptance for all
ill-conditioned inputs. Sparse fill, scalar factorization cost, interval widths,
representability and audit cost can prevent practical certification. Production
integration needs its own bounds review, resource limits, reference/mutation
tests and cold/warm measurements. Geometry-only bounds may be reused only with
an unchanged, complete rest snapshot and handle set; each output needs a fresh
original-data residual and response check. The production size/resource caps
must not be removed merely because this dimension-independent theorem holds.

The separate C# fixed-point experiment uses the same two Euclidean comparisons
with outward integer intervals. Its JSON mesh mode verifies that all listed
vertices form one connected component; index zero is its local anchor. JSON face
and vertex completeness relative to any external DMesh3 remain caller premises.
Both C# and Python pilots use the exact binary64 value represented by `1e-8` as
their default position and response tolerance. The C# pilot's existing per-axis
absolute check is an additional conservative rejection gate.

## Implementation evidence

The bounded production reference path in `BiharmonicSparseCertificate` reconstructs
these objects directly from the deformer arrays and DMesh3 triangle enumeration.
It accepts one active connected component and fixes interval fractional precision
at 256 bits. Each signed endpoint is limited to 512 bytes; checked arithmetic
rejects a larger result. Division by an interval containing zero is inconclusive.
The supported scalar runtime admits the following tiers (n is the vertex count):

| Vertices | Triangles | Q/Schur updates | Live Q/symbolic entries | Trial mu |
|---|---:|---:|---:|---:|
| 17–400 | 2000 | 1000000 | 50000 | 0.5 |
| 401–2500 | 8*n | 10000000 | 50000 | 0.125 |
| 2501–10000 | 8*n | 200000000 | 250000 | 0.015625 |

Other runtimes retain the first tier and interval LDL. The scalar verifier also
limits retained factor entries plus live off-diagonal symbolic entries to 2000000
and scalar products to the remaining Q/Schur budget after assembly. These limits
bound certificate work, not the preceding numerical candidate factorization, total
managed heap bytes or wall-clock time. A successful spectral proof for the chosen
trial mu is required; the size-dependent proposal is untrusted and failure does
not establish that A is indefinite.

Only the explicitly requested strict mode may form a trial candidate despite
completely rounded-away summands. That candidate remains local until this proof,
including final world rounding and both Euclidean comparisons, succeeds. Default
numerical mode retains the old guards. Multiple active components and unsupported
sizes currently remain inconclusive rather than sharing one response norm.

The sparse rest proof can now be retained in one opaque `Prepared` snapshot per
deformer. It owns cloned original-coordinate/ID/free-map data and private interval
L/M/rest-coordinate arrays. Before reuse the wrapper compares the original bits
directly with the live mesh, and the snapshot compares mesh identity, all vertex
bits/IDs/free-map indices and every triangle ID/index/count. Shape timestamps are
not trusted as a complete invalidation contract. New targets, final-coordinate
displacements, handles, residuals and both tolerances are checked on every call.
No old candidate/target bound is reused. A numerical Cholesky factor may be retained only after successful certification, paired with that exact Prepared object. Before reuse both proof identity and the complete current key must match. Targets/RHS and final output proof are recomputed; no numerical factor is treated as a certificate. A changed proof discards the old pair even if the new candidate fails certification.
Instances and their input may not be mutated concurrently with Compute. Tests
cover target-only reuse, stale input snapshots, geometry/topology/identity/handle
changes and rejection of corrupted candidates using the same rest proof.

`euclidean_certificate.assess` consumes exactly one component, ordered by original
vertex ID, so its first point is the stated rest anchor. It does not infer graph
membership or establish its input error bounds. `interval_ldl_pilot.py --euclidean`
supplies these premises for its procedural fixtures and combines the checks in
the same process. The scalar-audit backend checks each executed recurrence.

The rotated 20x20 fixture has 400 vertices and 360 free unknowns, and simultaneous
right-boundary xyz motion. Both Euclidean checks pass at tolerances 1e-8. The
historical wrong candidates for tiny150, tiny200 and lost-handle reject at 700
decimal digits; in tiny200 the absolute check passes but the relative check fails.
Commands and raw evidence live in the probe README. This is a concrete certificate
for these represented inputs, not an extrapolation to the production candidate.
Independent review found no concrete false-success derivation in this composition
and required the single-component, ordered-anchor premise to be explicit.

## C# scalar feasibility implementation (2026-11-15)

The separate `BiharmonicCertificateArithmetic/ScalarCholesky` implements the scalar criterion above, not the production supernodal solver. For fixed-point precision at most 1074 bits, interval endpoints and binary64 midpoints are represented in exact 2^-1074 units. Symmetric radius row sums are exact integers. With positive midpoint trace T in those units, the correction is the upward integer quotient `T*(n+1)/(2^53-2*(n+1))`, exactly the stated `gamma/(1-gamma)` coefficient; its denominator is positive under the 2500-unknown cap. Each adjusted diagonal is converted downward from `B_ii-c-rho` by bit construction. Off-diagonals are the selected finite binary64 centers.

The scalar recurrence includes structural fill even for zero values, performs direct division and separates products with a non-inlined method to prevent fused multiply/subtract contraction. It refuses nonfinite/subnormal results and any nonzero product/quotient rounded to zero; all products/quotients at the minimum-normal result boundary are refused conservatively, including valid exact results. Thus an accepted run excludes hidden subnormal exact products/quotients at that rounding boundary. The unaudited recurrence still assumes correctly rounded IEEE binary64 execution; runtime probes or small audited inputs do not prove this premise for every runtime/input. No production or positions certificate is returned by this experiment.

For small fixtures the emitted matrix bounds are recomputed rationally and a separately formulated dense symbolic/scalar recurrence demands identical operation ordering, operand/result bits and full trace length. Each operation is checked against exact nearest-even cells. Incorrect rounding, omitted correction/radius, a truncated trace and an arithmetically valid but falsely linked operation reject. This verifies the recorded interval matrix and scalar execution, not independently the matrix's completeness with respect to the mesh. The original-data assembly retains its own review/reference premises. Bounds review found no remaining concrete error; targeted source mutation removing the correction is caught. Raw commands/evidence and costs are in the deformation benchmark README.

## Production scalar integration (2026-11-16)

`BiharmonicScalarCertificate` supplies the spectral premise to the original-data residual/Euclidean checks in `BiharmonicSparseCertificate.Prepare`. The resource tiers above govern production. Symbolic scalar elimination charges the former Schur update count, including diagonal updates, against the remainder after Q assembly. Live entries include active diagonals and are charged before clearing the current column; retained scalar factor rows have the separate combined guard above. The correction denominator remains positive for at most 10000 free unknowns. These counts are not heap-byte bounds.

The scalar path is enabled only in the net10.0 build on x64 with nearest-even binary64 execution and gradual underflow. Non-inlined operations prevent contraction. Per-call probes detect changed rounding, FTZ and DAZ entry conditions; they do not prove the platform contract for every operation. Native code must not change floating-point controls during Compute. Other targets retain the integer interval factorization. Failed scalar verification fails closed. A retained proof remains a statement about the exact rest system; every new output still goes through integer interval residual and response checks. No factor values are trusted as proof or reused as residuals.

Independent bounds review found no incorrect certificate under these stated premises. A resource-accounting finding was fixed by passing remaining assembly/Schur budgets into the scalar verifier. Existing corruption, tiny-response and cache-invalidation tests exercise the composed certificate; new direct tests cover indefinite intervals, trace-correction refusal, exhausted work/fill and directed exact-unit conversion.

## Residual correction bound for larger rounded outputs

Let g=A u_hat-b be the original-data gradient and choose any finite binary64 vector z (zero on handles), for example a numerical solve of A z approximately equal to g. Independently enclose d=g-Az with the original interval L/M operator. Since lambda_min(A)>mu, the exact error e=A^-1 g satisfies e=z+A^-1 d. Thus on each coordinate axis every vertex error is at most max_i |z_i| + ||d||_2/mu. Take the smaller of this outward upper bound and the existing ||g||_2/mu. Combine the three coordinate bounds in exactly the existing squared Euclidean acceptance inequalities. The numerical z is untrusted and no precision assumption about its solve is needed; z, the defect and the final candidate must all be enclosed from their actual binary64 values. This proof tightens verification without changing returned positions or requested tolerances.

## Scalar row indexing and compact storage (2026-11-16)

The scalar recurrence may index the completed row j into a per-column stamp/value
workspace. Rows are appended in increasing elimination-column order. Traversing
target row i in that order, and selecting exactly keys stamped for j, produces
the same ordered intersection and the same product/subtraction operands as the
previous two-pointer scan. No arithmetic occurs on unmatched keys. The last-key
cutoff handles empty rows and avoids scanning a suffix with no possible matches.
Every stamp is j+1; values from earlier rows cannot contribute. The diagonal,
neighbor and quotient operation order remains unchanged.

Factor keys and values occupy separate int/double arrays, growing together before
an entry is appended. Array copies preserve all double bits, including signed
zero. Each row starts empty, first grows to four entries and thereafter doubles.
For n rows and m stored coefficients, total key capacity and total value capacity
are each bounded by 4*n+2*m. The existing combined symbolic/factor-entry guard
bounds m by 2000000; supported n remains at most 10000. The index adds one int and
one double per unknown. These are representation bounds, not total heap-byte or
wall-clock guarantees; all previous symbolic/product/live-entry caps remain.

Full pilot operation traces must match the scan control bit for bit and pass
independent exact replay. A separate 5x5 indefinite matrix checks stale stamps:
for ideal rational edge values its last LDL pivot is -19/280, while incorrectly
using an earlier row's coefficient can make it positive. The represented binary64
fixture is tested only for the robust sign, at three power-of-two scales. This
layout/index change adds no new numerical error bound or supported runtime.

## Geometric forest bound (2026-11-16)

For a single connected triangle component with a nonempty handle set, choose an
anchored forest: every free vertex v has a mesh-edge parent at hop distance d_v-1
from the handles. Let C be the maximum over forest edges e of the sum of d_v for
free vertices whose path to a handle uses e. Set c = min_T(area_T / sum of its
three squared edge lengths), and Mmax = max_i M_ii. The bound is

    lambda_min(Q_ff) >= c^2 / (Mmax*C^2).

This is the existing graph-path bound with edge congestion replacing the global
sum of all path lengths. For a scalar vector zero on handles, Cauchy-Schwarz along
each path gives sum_v x_v^2 <= C sum_forest_edges (x_a-x_b)^2. On each triangle,
linear interpolation gives x^T L_T x = area_T*|grad x|^2, while the sum of squared
edge differences is at most |grad x|^2 times the sum of squared edge lengths.
Thus x^T L_ff x >= (c/C)*|x|^2. Keeping only the free rows in L*x gives
|L*x|^2 >= |L_ff*x|^2 >= (c/C)^2*|x|^2; M^-1 >= I/Mmax finishes the bound.
Signed cotangents and zero edge weights are permitted: the argument uses whole
triangle energies and geometric edges, not positive individual cotangent weights.

Compute hop distances by multi-source BFS and parents in reverse BFS order.
Any predecessor at distance d_v-1 is valid. A short-edge heuristic may propose a
parent, using approximate normalized coordinates; its floating-point result is
not a premise of the bound. Ties use current accumulated load then vertex ID.
A reverse pass initializes each load to d_v and adds it to the selected parent;
the maximum load of a nonroot is C. This includes path lengths, not just subtree
vertex counts. Reject disconnected forests or C=0 for an active free system.
For at most 100000 vertices, a signed 64-bit load is sufficient; additions remain
checked. BFS/parent storage is O(n), scans O(n+T).

With fixed-point unit U=2^256, compute cLower per triangle by the downward integer
quotient (twiceArea.Lo*U)/(2*sumSquaredEdges.Hi), then take the minimum. Let
massUpper be the maximum mass.Hi. The encoded lower gap is the downward integer
quotient cLower^2/(massUpper*C^2). Require strictly positive encoded gap; this
establishes a non-strict lambda>=gap premise, sufficient for the existing error
bound. All geometry and masses are reconstructed from original binary64 inputs.
No numerical factor supplies a spectral premise. Final rounded-position residuals,
untrusted correction defects and both Euclidean acceptance inequalities remain.

Independent bounds review found no missing factor or rounding-direction issue.
The implementation passes exact small-matrix/solution controls and retains the
old shifted-factor proof as a fallback in its existing domain. Failed target
checks, including range/division failure, retain an already validated rest proof;
invalid live rest keys are still discarded. Geometric admission is bounded to
100000 vertices on the supported runtime (400 elsewhere), 8*n triangles above
400, and the existing endpoint/precision/numerical-assembly guards. The interval
L dictionaries contain at most n+6*T structural keys; forest/heuristic scratch is
O(n), with checked loads. These are not total heap-byte or wall-clock guarantees.
The shifted proof retains its previous table unchanged. Current evidence certifies
the rotated 100k fixture but not the aligned one, and cold cost exceeds 20s;
#980 tracks those remaining gates. No general 100k completion follows from this
sufficient inequality alone.

Prior art: canonical-path/congestion Poincare estimates (Diaconis and Stroock,
*Geometric Bounds for Eigenvalues of Markov Chains*, 1991; Sinclair, *Improved Bounds
for Mixing Rates of Markov Chains and Multicommodity Flow*, 1992). The anchored FEM
composition above is derived here; no Markov-chain theorem constants or code are
transferred to the mesh verifier. The library search found the earlier global-path
proposal in BIHARMONIC_DEFORMATION.md and existing adjacency/BFS building blocks.
