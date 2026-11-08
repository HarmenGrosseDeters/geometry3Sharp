# Conservative Marching Cubes: prerequisite audit

Status: legacy prerequisite mismatch; opt-in `LinearEdges` adapter implemented.
Tier M, library option plus verification tools and tests. Audit baseline revision: `871ac2abf00723094883c6e57017721ce5e0b406`.
Source: Alec Jacobson, *A Sharp Conservative Offset for Marching Cubes*,
[arXiv:2609.13430v1](https://arxiv.org/html/2609.13430v1), sections 1, 3 and 3.1.
No other geometry paper or implementation is used.

## Scope and acceptance

The initial comparison below was performed without Z3. The owner subsequently
authorized an opt-in linear-interpolation path. The tables remain unchanged.
The bounded verification is in `tools/Z3Pilot/conservative_mc.py`,
using the existing pilot's source contracts, SAT assumption checks, required SAT
negative controls, and failure on UNKNOWN. General continuous-function reasoning
remains the paper's theorem, not an SMT claim.

## Table findings

Both active `triTable` arrays have 256 rows and are identical. SHA-256 of Python
`repr(rows)` after parsing each row into a list of integers:

`0f9447f4b6579084662e91ef14e1ca2727bf665d9bd8818ababd8e3948c3f3ed`

`MarchingCubes.triTable2` is not used by `polygonize_cell`.
Both polygonizers classify corners with `f[i] < IsoValue`, emit only grid-edge
vertices, and use triangles contained in the parent cell in exact arithmetic.
No interior vertices are introduced by the active table.

A preliminary combinatorial check over all rows found no face-boundary mismatch:
count triangle edges whose endpoints lie on a cube face, cancel internal edges
modulo two, then compare the remaining edge pairs for equal face corner signs,
both within each face and between corresponding opposite faces of neighboring
cells. This includes the alternating-sign faces. This is a preliminary check,
not a proof of sign separation, embeddedness or watertightness of the emitted
mesh. Vertex welding and rejected triangles need separate attention.

The paper does not prescribe a unique ambiguity lookup table: section 1 requires
a watertight, sign-respecting separator. Thus a different diagonal alone would
not invalidate its proof. Table identity with another implementation is also
insufficient to establish its prerequisites.

## Blocking mismatch: edge placement

`MarchingCubes.find_iso` and `MarchingCubesPro.find_iso` both apply endpoint
nudging before selecting `RootMode`. Consequently even `SingleLerp` is not
always linear interpolation of the sampled values.

An explicit unit-edge example, with isovalue 1:

- `valp1 = 1 - 5e-7`, `valp2 = 1 + 0.1`.
- `tol = 0.00001 * (abs(1-valp1) + abs(1-valp2))`, approximately `1.000005e-6`.
- The near-first-endpoint branch returns parameter `t = 1 - 0.999999`,
  approximately `1e-6`.
- The exact linear crossing parameter is approximately `4.999975e-6`.
- The linear lift at the returned point is approximately `0.9999996000005 < 1`.

This violates the edge-crossing lift identity used in section 3, independently
of floating-point roundoff. It is NOT a counterexample to enclosure at the
sharp threshold: a failed proof prerequisite is not a failed conclusion. A
separate error argument might still certify a sufficiently padded mode.

`LerpSteps` interpolates on a bracket with newly sampled values rather than the
original corner samples. `Bisection` returns a finite-bracket midpoint. Neither
is the exact edge root discussed in section 3.1. These modes require separate
certificates with residual or position bounds.

## Selected implementation

Owner-authorized choice: add an opt-in variant/path with unmodified linear
edge interpolation and explicit supported input/domain conditions; retain the
existing behavior by default. The active tables need not be replaced merely
because of the observed endpoint-placement mismatch. An alternative is to
certify the existing nudging and finite rootfinding with additional, derived
padding. The latter expands the proof scope substantially.

How the allowed source solves it: section 1 uses linear edge crossings;
section 3.1 additionally permits exact roots and interior vertices in the
convex lift projection (in particular convex combinations of edge crossings).
Neither passage certifies the present endpoint nudge or finite rootfinding.

After the decision, the pilot must separately establish table separation
(including ambiguous faces), source coupling to edge placement, and the FP
position/lift error. A fixed number of machine epsilons is not a universal
world-coordinate bound: account for field subtraction/division, coordinate
formation, scale, subnormals and overflow, or restrict inputs explicitly.
The known subthreshold negative control is the paper's small sphere at a cell
center, with `0 < r < sqrt(3)*h/2 - sigma`; every corner is high and the local
mesh is empty. A 15-case reduction requires proof that the actual table respects
the chosen symmetries; otherwise use all 256 cases.

## Verification and recipe

The selected path is `RootMode = RootfindingModes.LinearEdges` in each class.
It bypasses endpoint nudging, uses canonical low-to-high ordering, clamps the
rounded point to its original grid edge, and fails on endpoint collapse or mesh
triangle rejection. Pro also bypasses its float sample cache in this mode.
Legacy mode values and defaults remain compatible.

The [pilot README](../../tools/Z3Pilot/README.md#conservative-marching-cubes-source-coupled-adapter)
contains the composed winding-enclosure argument, exact FP prerequisites,
normalized recipe, and treatment of approximate samples. The default gate is a
modular adapter certificate. It verifies closed high boundary chains and neighbor
face consistency of the parsed tables (the substance), plus the elementary lift
composition (one trivial linear lemma, instantiated per case) and FP error
envelopes; it is not a new proof of the continuous-function theorem or
embeddedness. Its link to `find_iso` is a whole-file hash tripwire that forces a
re-review on any change, not a semantic check: that the code matches the FP
model, that shared crossings are bit-identical in neighbouring cells, and that no
FMA contraction happens remain review obligations (with native tests). The
failure paths (`InvalidOperationException` for a crossing on a corner or a
collapsed triangle, also from the parallel paths; `ArgumentException` for a Pro
grid of more than 65535 cells per axis in `Generate()`, 65534 in
`GenerateContinuation()`, which also visits the cell of index n, and for a
continuation seed outside cells 0 to n: beyond these its 16-bit hash would alias
edges) have native tests in `MarchingCubesConservativeTests`.
The throw on a rejected triangle is a guard: within these limits every visited
grid edge has its own vertex and by the case table no edge between two crossings
is used by more than two triangles, so it is not expected to fire. The optional
`--direct` query contains the proposed zero-point/Lipschitz nonlinear model;
UNKNOWN results there are not proofs. SAT assumptions are required for the
adapter queries; impossible zero-point cases in the direct model are separately
reported exclusions.

Use full, uncanceled `Generate()`, a box containing the complete zero set and offset surface, with every sample
on the actual extraction boundary high,
consistent field evaluations, the stated grid/sample/gap bounds, and a closed,
valid resulting mesh. Do not apply the normalized FP recipe to arbitrary world
coordinates or claim a bound for unquantified approximate mesh-SDFs.
