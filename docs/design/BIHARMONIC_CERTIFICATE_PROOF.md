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
section 3 bound I). For n free unknowns, eps = 2^-53 and
gamma = (n+1)*eps/(1-(n+1)*eps), a no-underflow trace correction is
c >= gamma/(1-gamma)*trace(B), subject to the theorem's gamma conditions.
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
original-data residual and response check. The current 16-vertex production cap
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
It accepts one active connected component, at most 400 vertices and 2000 triangles,
and fixes interval fractional precision at 256 bits. Each signed endpoint is
limited to 512 bytes; checked arithmetic rejects a larger result. Fixed-point
division by an interval containing zero is inconclusive. Sparse Q/factor storage
is limited to 50000 live dictionary entries and Q/Schur updates to 1000000. These
are explicit structural/arithmetic budgets, not a claim to enforce a particular
managed heap size or wall-clock deadline. Other storage is bounded by the vertex
and triangle caps. A successful positive-pivot proof for the fixed trial mu=0.5
is required; failure does not establish that A is indefinite.

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
