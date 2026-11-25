# Exact gradients of the volume and area of a Boolean result (2026-11-24)

This research note asks whether we can give the derivatives of the volume V and the area A of an exact Boolean result
(`ExactMeshBoolean`, before the rounding) with respect to the input vertex positions, either exactly or with a proven bound,
and with a defined treatment of the degenerate cases. The use is shape and topology optimization. **No library code was
changed.** The prototype is test-only: `tests/geometry3Sharp.Tests/Harness/BooleanGradient.cs`, checked by
`tests/geometry3Sharp.Tests/ExactBooleanGradientTests.cs` (20 tests, about 4 s in total).

## 1. Result in short

- **Volume: yes, exactly, and more cheaply than expected.** By the Hadamard / Reynolds formula the intersection curves drop
  out, so the implicit points (LPI, TPI) never have to be differentiated. Every kept piece moves with its source triangle,
  and
  `dV/dx_i = sum over kept pieces P of a source T that contains vertex i of  s_P * lambda_i^T(centroid P) * N_P`,
  where N_P is the piece's area vector, oriented like T, and s_P = -1 for a flipped piece. This is one rational per piece,
  computed from data the boolean already has: the pieces, their sources, keep and flip, and the exact corners.
- **Agreement with finite differences.** Exact central differences at steps 2^-8 ... 2^-40 converge to it as h^2: the
  error falls by a factor of 256.00 per 4 halvings, from 1e-4 down to 1e-24 relative. This holds on 6 generic cases (box
  with box, box with rotated box; union, intersection, difference). Under Similarity.All() the gradient follows exactly:
  for the 2^k scales, g' = s^2 g as rationals.
- **Area: yes, with a bound, not as a rational.** The area and its gradient are algebraic (square roots), so the gradient
  is not a rational. A flat piece changes area only through the in-plane normal velocity of its boundary. On a seam with a
  triangle U, that velocity is `(w_U - w_T cos theta) / sin theta`, and this is where the other mesh's vertices enter. The
  prototype formula, in double, agrees with central differences to 5e-11 ... 9e-10 relative. Its value has no bound as the
  seam angle goes to 0: measured, theta * dA -> 1.000 for theta = 2^-4, 2^-8, 2^-12 at a sliver wedge.
- **Degenerate cases: classified and tested.**
  - V is differentiable everywhere except at a contact of positive area: coplanar faces with overlapping interiors, with
    either orientation. An edge on a face, a vertex on a face and edge-edge contacts are differentiable, because the
    change of the overlap is o(t): a wedge of volume t^2 / 2 was measured.
  - A has kinks also at an edge on a face (measured: left derivative 1 + sqrt 2, right derivative 0).
  - At every kink the formula returns an exact one-sided derivative: that of the combinatorial branch that the exact
    result realizes. It is an extreme point of the Clarke generalized gradient.
  - For flush faces (same orientation) the branch is chosen by the boolean's tie-break, the first triangle of the
    coplanar stack (input order). That acts like simulation of simplicity, and swapping A and B gives the other one-sided
    derivative (tested).
- **Prior art: none found.** No published method gives exact gradients of a mesh Boolean result with a defined treatment
  of degeneracies (section 2). The closest exact analogue is the volume and area derivative of a union of balls
  (Edelsbrunner and Koehl 2003; Bryant et al. 2004).
- **Verdict.**
  - The volume gradient is a small, sound feature: tier M, about 3-4 days. It returns a certified double per vertex, not a
    rational, and a list of non-differentiable contact regions with both one-sided gradients.
  - The area gradient needs the seam provenance (the other triangle of each intersection edge) from
    `ExactMeshArrangement`, and so it is exact-code work. It is a second step, after a go.

## 2. Prior art (papers only; no code read)

These claims come from a literature check by web search: abstracts, metadata and snippets, not full papers. [S] marks a
claim that was seen in search results only.

| Work | What it differentiates | Exact? Treatment of kinks |
|---|---|---|
| Yuan, Bousseau, Pan, Zhang, Mitra, Li, *DiffCSG: Differentiable CSG via Rasterization*, SIGGRAPH Asia 2024, arXiv 2409.01421 | a rendered image, with respect to the parameters of the CSG primitives, by Goldfeather visibility; the intersection edges are passed to the anti-aliasing of a differentiable rasterizer | image based, approximate; no mesh, volume or area; the authors call mesh Booleans hard to differentiate because the topology changes [S] |
| CSG-Stump (Ren et al., ICCV 2021), UCSG-Net (Kania et al., NeurIPS 2020), CAPRI-Net (Yu et al., CVPR 2022) | learned CSG trees over implicit or occupancy primitives | soft min / max, approximate; kinks smoothed away [S] |
| Li, Aittala, Durand, Lehtinen, *Differentiable Monte Carlo Ray Tracing through Edge Sampling*, TOG 37(6) 2018 | rendering; the Dirac terms on silhouette and intersection edges | Monte Carlo; the rendering analogue of the boundary term of Reynolds transport |
| Delfour, Zolésio, *Shapes and Geometries*, 2nd ed., SIAM 2011; Sokołowski, Zolésio, *Introduction to Shape Optimization*, Springer 1992 | the shape derivatives dV = ∫ v·n, dA = ∫ H v·n (plus boundary terms) | smooth or Lipschitz domains; contact problems through directional derivatives (variational inequalities) |
| Meyer, Desbrun, Schröder, Barr, *Discrete Differential-Geometry Operators for Triangulated 2-Manifolds*, VisMath 2003 | the cotangent mean-curvature normal, which is the gradient of polyhedral area with fixed connectivity | exact for a fixed mesh; no Boolean |
| Lasserre, JOTA 39, 1983; Sellaroli, arXiv 1712.00825 | the volume of {Ax ≤ b} and its derivative with respect to b (the facet area), face-area Jacobians | exact, convex polytopes only; degenerate at non-simple vertices |
| Allard, Faure, Courtecuisse, Falipou, Duriez, Kry, *Volume Contact Constraints at Arbitrary Resolution*, SIGGRAPH 2010 | the interpenetration volume and its gradient with respect to the vertices | approximate (layered depth images); contact is a complementarity constraint |
| Edelsbrunner, Koehl, *The weighted-volume derivative of a space-filling diagram*, PNAS 2003; Bryant, Edelsbrunner, Koehl, Levitt, *The area derivative of a space-filling diagram*, DCG 2004 | the volume and area of a union of balls, with respect to the centres | exact closed form; analyses where the derivative jumps (changes of the alpha complex). The closest analogue, but for balls |
| Clarke, *Optimization and Nonsmooth Analysis*, Wiley 1983 | the generalized gradient of a locally Lipschitz function | the convex hull of the limiting gradients |
| Bolte, Pauwels, *Conservative set valued fields, automatic differentiation, ...*, Math. Programming 2020 | what automatic differentiation returns at kinks | an element of a conservative field; equal to the gradient almost everywhere |
| Edelsbrunner, Mücke, *Simulation of Simplicity*, ACM TOG 9(1) 1990 | a symbolic perturbation that breaks every degeneracy consistently | read as a tie-break: one adjacent smooth piece, so one one-sided derivative |

**Nothing found** for exact (rational or exact-arithmetic) gradients of a triangle-mesh Boolean result, or for an analytic
treatment of coplanar or contact degeneracies in mesh Booleans. The exact Booleans (Lévy, arXiv 2405.12949; Cherchi et
al. 2020) note that the intersection points are rational in the inputs, but they do not differentiate. The search terms
included "differentiable mesh boolean gradient vertex positions intersection volume polyhedra exact", "exact arithmetic
gradient mesh boolean volume derivative rational shape derivative polyhedral intersection" and "differentiable
intersection volume two triangle meshes gradient contact optimization".

### What the library already has (not presented as new)

- The arrangement's pieces with their source triangle, the classification's keep and flip, and the exact corners as
  `ExactPoint3`. These are recorded for the certificate (`RecordCertificate`, `BooleanCertificate.From`). The prototype
  reads exactly these, so it needs no new geometry.
- The coplanar stacks (the input triangles grouped by exact plane, C3 of the certificate). They are the data the contact
  classification of section 3.3 needs.
- `ExactInterval` and `ExactDoubleWord` (certified double arithmetic) for a bounded double result, and the volume sign
  filter of `ExactPredicates.VolumeSign`. No volume gradient, area gradient or shape derivative exists in the library: a
  search for "gradient", "derivative" and "Hadamard" in `mesh_ops/`, `math/` and `docs/` found only unrelated gradients
  (fields, winding-number bounds).

## 3. The math

Notation:
- T is an input triangle with corners t_0, t_1, t_2 (input vertices) and N_T = (t_1 - t_0) × (t_2 - t_0).
- A kept piece P ⊂ T has exact corners p_0, p_1, p_2 (input points, LPI or TPI points), an area vector
  N_P = (p_1 - p_0) × (p_2 - p_0) / 2 oriented like T, and an orientation s_P in the result (-1 if flipped).
- A perturbation δ moves each input vertex i by δx_i. A point y of T moves with T:
  v_T(y) = Σ_k λ_k^T(y) δx_{t_k}, where λ^T are the barycentric coordinates in T (affine, extended to T's plane).

### 3.1 Volume (exact)

V = Σ_P s_P (1/6) p_0 · (p_1 × p_2) is a rational function of the input coordinates, as long as the combinatorics is
fixed (the generic case). Its directional derivative is the integral of the normal velocity over the result's boundary
(Reynolds transport; Delfour and Zolésio):

  dV[δ] = Σ_P s_P ∫_P v_T · n̂_T dA = Σ_P s_P Σ_k λ_k^T(c_P) N_P · δx_{t_k},  c_P the centroid of P,

because λ is affine, so its mean over P is its value at the centroid. The intersection curves have zero area and do not
contribute.

The same result follows from the chain rule through the implicit points. A corner on a seam is shared by pieces on the
two sides of the seam (pieces of T and of U). The derivative of the volume with respect to that corner's position, summed
over those pieces, is tangent to the seam to first order: the closed surface's volume does not change when one of its
vertices slides inside the surface. So the terms dV/dp · dp/dx cancel, and only the motion of the planes remains. The
prototype checks the formula directly against exact central differences (section 4.1), and the h^2 convergence shows
that no seam term is missing: a missing term would leave a constant error.

Exactness: every term is a rational in the input doubles. λ_k(c) = ((t_{k+1} - c) × (t_{k+2} - c)) · N_T / (N_T · N_T),
with degree-4 denominators, times the corners' homogeneous denominators W, of degree 3 for LPI and TPI. A per-vertex
value is a sum of such rationals, one per adjacent kept piece. Units and scaling: the gradient has units of length^2;
under x -> 2^k x it scales by exactly 2^{2k}, and under a rotation R it maps to R g.

### 3.2 Area (algebraic, with a bound)

A = Σ_P |N_P| contains square roots, so neither A nor its gradient is rational. A piece is flat (H = 0), so the first
variation of its area is the flux of the boundary's in-plane normal velocity:
dA_P[δ] = ∮_{∂P} v · ν ds, where ν is the outward co-normal in T's plane.
- **An edge on T's boundary, or inside T:** v = v_T. The fluxes of the interior edges cancel between the two pieces on
  either side.
- **A seam with a triangle U:** the seam point stays on both planes, n_T · d = w_T and n_U · d = w_U, with
  w_X = n_X · v_X(y). Its in-plane normal velocity is

  β = (w_U − w_T cos θ) / (n_U · ν),  where |n_U · ν| = sin θ and θ is the angle between the planes.

β is affine along the edge, so the flux is |e| β(midpoint). The coefficients enter the vertices of both T and U. Summed
over the two pieces that meet at a seam:
- **Dihedral near π** (an almost flat crease): the 1/sin θ parts cancel to O(θ).
- **Dihedral near 0 or 2π** (a sliver wedge): they add. Then |∇A| ~ 1/θ, which has no bound as the seam angle goes to 0.

The gradient is a sum of terms q / sqrt(r) with rationals q and r. Its sign is decidable, but its value is not a
rational, so an API returns a double with a proven bound (interval or double-word evaluation of each term).

### 3.3 Degenerate configurations

V is a piecewise rational, Lipschitz, semialgebraic function of the inputs. So it has one-sided directional derivatives
V'(x; δ) everywhere, and a Clarke generalized gradient. The question is where it fails to be differentiable.

1. **Contact of measure zero in the surface:** a vertex on a face, an edge on a face (including an edge lying in the
   face's plane), edge-edge contacts, or a vertex on a vertex. The overlap region that a perturbation of size t creates
   has volume o(t): t^2 for a wedge, t^3 for a corner. So V is differentiable there, and the formula is the gradient: no
   kept piece of positive area changes its classification to first order. Measured (§4.3): wedge overlap t^2 / 2, both
   one-sided quotients converge to the gradient 0, and the left quotient equals h / 2 exactly.
2. **Contact of positive area (coplanar faces with overlapping interiors)**, C the overlap region.
   - **Opposite orientation** (two solids touching from outside, or a pocket flush with a face from inside). Let a(y) and
     b(y) be the normal velocities of the two faces along A's outward normal. Then

     V'(x; δ) = g·δ + ∫_C φ(a − b) dA, with φ(u) = min(0, u) for union, max(0, u) for intersection, and the matching
     form for difference.

     The exact result drops C for union and keeps neither copy for intersection, so the formula returns g: the derivative
     of the branch the result realizes. For union that is the overlapping side, for intersection and difference the
     separated side. Measured exactly (§4.3), for a contact of area 1/4: the formula gives 1/4 for union, 0 for
     intersection, 0 for difference. The left and right difference quotients are (1/4, 0), (−1/4, 0) and (1/4, 0).
   - **Same orientation** (flush faces). For union, the top of the result is max(a, b): the contribution is ∫_C max(a, b),
     and for intersection ∫_C min(a, b). The exact result keeps one copy of C, from the first triangle of the coplanar
     stack, by input order. So the formula returns the derivative of the branch "C moves with that mesh". This acts like
     simulation of simplicity: a fixed, consistent tie-break picks one adjacent smooth piece. Measured: A ∪ B gives the
     left derivative 1/4, B ∪ A the right derivative 1/2.
   - **Clarke generalized gradient.** In both cases it is ∂V = {g + ∫_C θ(y) ∇_δ u(y) dA : θ: C → [0, 1]}. Here u is the
     relative normal velocity, affine on each coplanar piece. The formula's g is an extreme point (θ ≡ 0 or 1). The other
     extreme, g plus the contact pieces with their source velocities, is computable from the same arrangement: the
     contact pieces are the unkept (or other-copy) pieces of the coplanar stacks. So an API can return both one-sided
     gradients exactly, together with C.
3. **Area.** Besides case 2, A also has kinks at an edge-on-face contact, where a strip of width ~t is cut out (§4.3: left
   derivative 1 + sqrt 2, right 0, formula 0). The area gradient also has no bound near tangential seams (§3.2). For area
   optimization, a near-contact therefore needs a barrier or smoothing on the caller's side. The volume needs neither.

**What simulation of simplicity would give.** SoS resolves every degenerate predicate as if the input were perturbed by
an infinitesimal, index-dependent ε. The result is then the generic result of one adjacent open cell of configuration
space, and the formula of §3.1 on it is that cell's gradient: one one-sided derivative. Our boolean decides coplanar
cases exactly (winding numbers, coplanar stacks) instead. It merges opposite contacts, which is the regularized
set-operation answer, and orders flush faces by input order. The gradient it implies is still one extreme of the Clarke
set. The difference to SoS: the choice depends on the operation (union merges, intersection drops), not on an index
order, except for the flush-face tie.

## 4. The prototype and its numbers

`BooleanGradient.Compute(a, b, op)` runs `ExactMeshBoolean` with `RecordCertificate`. It then accumulates:
- the volume gradient as `BigRational` per input vertex (§3.1, in the arrangement's grid coordinates, then scaled by
  2^{2 ScaleExp});
- the area gradient in double (§3.2), finding the triangle U of each seam edge by an exact search (plane containment and
  midpoint inside, exact);
- the exact volume of the result (Σ s_P p_0·(p_1×p_2)/6, rational), and the area.

Inputs are procedural:
- the unit box;
- an axis-aligned box shifted by dyadic offsets;
- a box rotated by 27° about (1, 2, 3), with its corners rounded to the 2^-24 grid (so that x ± h δ is exact in double for
  h = 2^-k, k ≤ 44);
- the contact fixtures of §4.3.

The directions δ are seeded integer vectors in {−3..3}^3 on every vertex of both meshes.

Command (all numbers below):

```
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~ExactBooleanGradientTests" --logger "console;verbosity=detailed"
```

### 4.1 Volume against exact central differences

FD = (V(x + hδ) − V(x − hδ)) / 2h, evaluated exactly in rationals. The table gives the relative error |FD − dV[δ]| / |dV[δ]|
(converted to double for printing).

| case | dV[δ] | k = 8 | 12 | 16 | 20 | 24 | 28 | 32 | 36 | 40 | ratio per 4 halvings |
|---|---|---|---|---|---|---|---|---|---|---|---|
| axis ∪ | −2.0201822916666665 | 3.0e-4 | 1.2e-6 | 4.5e-9 | 1.8e-11 | 6.9e-14 | 2.7e-16 | 1.1e-18 | 4.1e-21 | 1.6e-23 | 256.00 |
| axis ∩ | 0.18684895833333334 | 1.1e-4 | 4.4e-7 | 1.7e-9 | 6.8e-12 | 2.6e-14 | 1.0e-16 | 4.0e-19 | 1.6e-21 | 6.2e-24 | 256.00 |
| rotated ∪ | −2.520416786603002 | 5.3e-5 | 1.1e-6 | 4.1e-9 | 1.6e-11 | 6.3e-14 | 2.5e-16 | 9.6e-19 | 3.7e-21 | 1.5e-23 | 256.00 |
| rotated ∩ | 0.7079193993994769 | 6.9e-4 | 3.3e-7 | 1.3e-9 | 5.0e-12 | 2.0e-14 | 7.7e-17 | 3.0e-19 | 1.2e-21 | 4.6e-24 | 256.00 |
| rotated − | −3.54125273273281 | 1.2e-4 | 1.2e-7 | 4.5e-10 | 1.8e-12 | 6.9e-15 | 2.7e-17 | 1.1e-19 | 4.1e-22 | 1.6e-24 | 256.00 |

The axis difference passes the same assertion; it is omitted from the table. The ratio is exactly the h^2 law from
k = 16 on. At k = 8 and 12 the step crosses a combinatorial change (a seam passes a vertex), which is why the first ratio
is 50-2092, not 256. The test asserts relative error ≤ 64 h^2 at every step.

### 4.2 Invariance (Similarity.All())

The gradient of the transformed inputs, mapped back (R^T g' / s^2), against the original:
- **2^-10 and 2^10 scales:** every component is equal as a rational, g' = s^2 g, and V' = s^3 V exactly.
- **identity and utm:** 0 difference. The UTM translation keeps the 2^-24 grid exact, and a translation does not change
  the gradient.
- **1e-3, 1e3 and rotated:** ≤ 1.4e-16. The transformed input is rounded, and the gradient is Lipschitz in it.
- **rotated, 1e-3, utm:** 1.5e-7, from the input rounding at UTM magnitude in millimetre units.

### 4.3 Degenerate positions

| fixture | direction | left quotient | right quotient | formula | |
|---|---|---|---|---|---|
| face contact, area 1/4, union | B along +x (away) | 1/4 | 0 | 1/4 | exact rationals; the merged branch |
| same, intersection | | −1/4 | 0 | 0 | the empty branch |
| same, difference A − B | | 1/4 | 0 | 0 | A unchanged |
| flush tops, A ∪ B | B's top up | 1/4 | 1/2 | 1/4 | the stack's first triangle is A's |
| flush tops, B ∪ A | | 1/4 | 1/2 | 1/2 | swapped input order |
| edge on face, union, **volume**, h = 2^-10 / 2^-20 | B along +z | 4.88e-4 / 4.77e-7 (= h/2 exactly) | 0 | 0 | differentiable |
| edge on face, union, **area**, h = 2^-20 | | 2.4142155 (1 + sqrt 2 + O(h)) | −1.9e-9 | −2.2e-16 | a kink; the separated branch |

**Sliver seam, A ∩ B**, with B's bottom at slope θ against A's top. dA[δ] for B moving down:

| θ | dA[δ] | θ · dA |
|---|---|---|
| 2^-4 | 17.5156 | 1.0947 |
| 2^-8 | 257.50 | 1.0059 |
| 2^-12 | 4097.5 | 1.0004 |

The formula matches central differences to 4 digits or better in each row.

### 4.4 Area against central differences (double)

The relative error at k = 12 / 16 / 20 / 24, h = 2^-k:

| case | dA[δ] | k = 12 | 16 | 20 | 24 |
|---|---|---|---|---|---|
| rotated ∪ | 19.45882039245051 | 1.5e-7 | 5.8e-10 | 5.2e-11 | 9.1e-10 |
| rotated ∩ | −4.69071723638236 | 2.4e-8 | 9.4e-11 | 1.5e-10 | 2.5e-10 |
| rotated − | 38.120259809866106 | 1.4e-6 | 5.6e-9 | 5.9e-11 | 8.8e-11 |
| axis cases | 4.328125, 7.671875, 23.8125 | | 1.7e-8 ... 3.0e-8 at k = 14 | 7.8e-11 ... 1.2e-10 at k = 18 | |

From k = 20 on, the floor comes from the rounding of the double areas divided by h, not from the formula. The test
asserts ≤ 1e-6 from k = 16 on.

### 4.5 Mutations (hand mutants, all killed)

Each mutant was run against the whole test class, by the loop that is now in the PR description.

| mutant | failing tests (of 20) |
|---|---|
| λ replaced by 1/3 (no barycentric weighting) | 7 |
| flip ignored | 4 |
| the U term of a seam dropped | 8 |
| the sign of cos θ | 5 |
| seams treated as material edges | 8 |

## 5. Cost of a real API

**The prototype is not representative.** It normalizes every `BigRational` by a GCD, and its seam search is quadratic.
A scratch probe (not committed) measured two cube-spheres (`Sphere3Generator_NormalizedCube`) in a union:

| triangles per mesh | kept pieces | boolean | gradient |
|---|---|---|---|
| 588 | 1020 | 429 ms (with JIT) | 0.26 s |
| 2700 | 4034 | 87 ms | 3.4 s |
| 11532 | 16074 | 317 ms | 40 s |

The prototype's cost per piece grows with the bit size of the accumulated sums. Exact rationals per vertex are therefore
the wrong output format: the sum of many rationals of independent denominators grows without bound.

**Proposed API (volume, tier M, about 3-4 days with tests):**
- `ExactMeshBoolean.VolumeGradient(out double[][] bound)`, or a small result class: per input mesh a `Vector3d[]` by
  vertex id, available after `Compute()`. It is evaluated per kept piece from the exact corners in interval or
  double-word arithmetic (`ExactInterval`, `ExactDoubleWord`), with a per-component a-posteriori bound (Higham's
  γ_n Σ|terms| for the summation).
- The cost is O(kept pieces) with fixed-width arithmetic. The estimate is below 10 % of the boolean's assembly, from the
  work per piece: one barycentric triple and one cross product. It is not measured; the prototype's numbers do not apply.
- The keep and flip arrays and the arrangement must be retained after `Compute()`. Today they are retained only with
  `RecordCertificate`, which an opt-in flag can extend: memory O(pieces), already paid for the certificate.
- **Degeneracy report:** the contact regions of positive area (the coplanar stacks with more than one triangle, which the
  classification already builds), each with the second one-sided gradient (§3.3), and a flag `Differentiable`. Callers
  then get the Clarke extremes, not a silent choice.
- **Tests:** the ones of this note (FD, Similarity.All() bit for bit for the 2^k scales, contacts), plus a reuse test
  (compute twice) and a determinism test if a parallel path is added.

**Area (a second step after a go, about 3-5 more days):**
- Each intersection edge needs its pair (T, U). The arrangement knows the pair when it constructs the constraint segment,
  but does not keep it (`ConstraintEdges` stores only point pairs). Keeping it costs about 8 bytes per constraint edge in
  `ExactMeshArrangement`. That is exact code: review, mutation gate.
- The value is irrational, so only a bounded double is possible.
- The 1/sin θ growth and the edge-contact kinks need a documented contract: report "nearly tangential seam" above a
  threshold rather than return a huge gradient silently.

**Exact-code rules** (no FMA, no `float`, no parallel loops in `math/Exact*`, `mesh_ops/ExactMesh*`):
- The gradient is a value, not a sign. It belongs in a new file outside the exact section, which reads the exact corners
  through the internal API, so the analyzers G3E0001-3 do not apply to it.
- It should still avoid FMA (`Math.FusedMultiplyAdd`), to stay bit-identical across runtimes and JIT settings. It should
  sum the per-vertex terms in a fixed order (piece order), so that a parallel evaluation of the terms into an array,
  followed by a serial sum, stays deterministic.
- If it ever moved into an exact file, a parallel loop would need an entry in `core/ExactCodeAllowList.cs` and a review:
  no floating-point reduction across threads.
- `Math.Sqrt`, for the area, is correctly rounded, so it is deterministic.
- Determinism promises need an entry in `DeterministicApis.txt`.

## 6. Options

1. **Volume gradient API**, as in §5. It is the first sound method we know of for exact Boolean volume gradients with a
   defined treatment of degeneracies. Risk: low. It reuses the certificate's recording and adds no new predicates.
   **Leaning: go**, if shape optimization is a target use.
2. **Area gradient.** It needs the seam provenance in the arrangement (exact code) and a contract for near-tangential
   seams. Leaning: only after option 1, and only with a user need.
3. **Park.** Keep the prototype as a test-only oracle. It also checks the boolean itself: the h^2 convergence of the
   exact volume under perturbations is a sensitive metamorphic test of the classification.
