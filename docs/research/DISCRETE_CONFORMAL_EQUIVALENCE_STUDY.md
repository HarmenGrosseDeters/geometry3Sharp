# Discrete conformal equivalence on the P1 annulus reproducers

Study for priority 1 of [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424). The P1 failure and its existing alternatives are tracked in [PR #1285](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1285) and [GEOGRAM_MEASUREMENTS_2026-11.md](GEOGRAM_MEASUREMENTS_2026-11.md).

**Answer to the falsifiable question: partly.**
A full C# prototype of discrete conformal equivalence with Ptolemy intrinsic Delaunay flips ([`benchmarks/DiscreteConformalAnnulusProbe/DiscreteConformalSolver.cs`](../../benchmarks/DiscreteConformalAnnulusProbe/DiscreteConformalSolver.cs)) converges on **17 of 17** procedural P1, jittered, pinched, twisted, bent, conical and two-hole cases in 3–15 damped Newton iterations (3–128 ms) using `SparseCholesky`, with **0 flipped or degenerate intrinsic triangles**. On every rotationally symmetric annulus (including every P1 cylinder where LSCM fails, the pinched waist and the twisted cylinder) and every disk/cone, the flat intrinsic triangulation unfolds seamlessly into a globally overlap-free planar chart (`transHol < 5e-13`, `0` boundary contacts). On **asymmetric, independently jittered, bent, or multi-hole annuli**, curvature-only prescription leaves a nonzero 2D translational holonomy (`1.6e-4` to `4.0e-2` of diameter), so a seamless uncut planar chart still requires an explicit hole-closure (holonomy) constraint or a seam cut.

## Method and building blocks

The prototype implements the convex discrete conformal energy of Springborn, Schröder and Pinkall (2008), the hyperbolic Delaunay criterion and cross-ratio-invariant Ptolemy flip update of Bobenko, Pinkall and Springborn (2015), and the intrinsic flip-and-Newton loop of Gillespie, Springborn and Crane (2021):

- **State:** intrinsic halfedge mesh with unscaled log-lengths $\lambda_{ij}$ and vertex scale factors $u_i$. Scaled lengths are $\tilde\ell_{ij} = \exp(\lambda_{ij} + \frac{1}{2}(u_i + u_j))$. Because the scale factors cancel identically in the Ptolemy update, flipping an edge $(i,j)$ to $(k,l)$ updates the unscaled log-length via $\lambda_{kl} = \operatorname{logaddexp}(\lambda_{ik}+\lambda_{jl}, \lambda_{kj}+\lambda_{li}) - \lambda_{ij}$.
- **Flip predicate:** Penner / Bobenko–Pinkall–Springborn hyperbolic Delaunay value
  $$D_{\mathrm{hyp}}(h) = \frac{a^2 + b^2 - e^2}{ab} + \frac{c^2 + d^2 - e^2}{cd} \ge 0,$$
  which needs no square roots and stays well-defined even when an intermediate Newton step temporarily violates Euclidean triangle inequalities.
- **Newton step:** exact angle gradient $\nabla_i E = \Theta_i^{\mathrm{target}} - \Theta_i(u)$ and positive-definite cotangent Hessian $H = \frac{1}{2} L_{\mathrm{cot}}$ solved with `geometry3Sharp`'s own `SparseCholesky`, with Lobachevsky–Milnor energy backtracking and hyperbolic Delaunay flips on each trial step.
- **Boundary modes tested:** `turn` (uniform loop turning $+2\pi/N_{\mathrm{out}}$ on the outer loop, $-2\pi/N_{\mathrm{in}}$ on each inner hole loop, one vertex pinned) and `u_b=0` (fixed boundary scale factors $u_b = 0$, free boundary curvature).
- **Layout & holonomy check:** BFS spanning-tree unfolding of the final intrinsic Delaunay triangulation, checking rotational and translational mismatch across every non-tree interior edge and exact segment intersection (`ExactPredicates.Orient2d`) on all nonadjacent boundary edges when seamless.

## C# solver measurements

Reproduce from the repository root:

```sh
LC_ALL=C dotnet run --project benchmarks/DiscreteConformalAnnulusProbe -c Release
```

Measured in Release on Linux x64 with .NET SDK 10.0.115:

| Case | Mode | LSCM (`flips+deg`) | Spectral | CETM `conv/iters` | Flips `init+solve` | Max angle res | Rot hol (rad) | Trans hol (rel) | Intr `flips+deg` | Bnd contacts | Mean conf | Time (ms) |
|---|---|---|---|---|---:|---:|---:|---:|---|---:|---:|---:|
| P1 cyl 8×3 h=1.8 | turn | `FlippedTriangles(2+0)` | `InvalidTopology` | `yes/4` | `0+0` | `1.78e-15` | `4.68e-15` | `1.90e-15` | `0+0` | `0` | `1.6564` | `18.4` |
| P1 cyl 8×3 h=3.6 | turn | `FlippedTriangles(2+0)` | `InvalidTopology` | `yes/4` | `0+0` | `1.16e-13` | `9.09e-13` | `3.46e-13` | `0+0` | `0` | `2.0326` | `3.4` |
| P1 cyl 24×6 h=1.8 | turn | `FlippedTriangles(0+2)` | `InvalidTopology` | `yes/3` | `0+0` | `6.66e-15` | `7.65e-14` | `2.48e-14` | `0+0` | `0` | `1.2205` | `34.9` |
| P1 cyl 48×12 h=3.6 | turn | `FlippedTriangles(0+2)` | `InvalidTopology` | `yes/3` | `0+3` | `8.88e-15` | `1.72e-13` | `4.08e-14` | `0+0` | `0` | `1.1773` | `127.5` |
| z-jit 10% 8×3 h=1.8 | turn | `Success(0+0)` | `InvalidTopology` | `yes/4` | `13+12` | `1.26e-13` | `6.61e-14` | `5.29e-03` | `0+0` | open (hol) | `1.6533` | `3.7` |
| z-jit 10% 8×3 h=3.6 | turn | `FlippedTriangles(1+0)` | `InvalidTopology` | `yes/5` | `13+12` | `3.55e-15` | `3.54e-15` | `1.83e-03` | `0+0` | open (hol) | `2.0272` | `4.4` |
| z-jit 10% 24×6 h=1.8 | turn | `FlippedTriangles(1+0)` | `InvalidTopology` | `yes/4` | `77+9` | `3.51e-14` | `5.25e-14` | `1.69e-04` | `0+0` | open (hol) | `1.2196` | `28.5` |
| z-jit 10% 24×6 h=3.6 | turn | `Success(0+0)` | `InvalidTopology` | `yes/5` | `77+11` | `7.99e-15` | `4.49e-14` | `1.59e-04` | `0+0` | open (hol) | `1.3829` | `24.1` |
| 3D-jit 24×6 h=1.8 | turn | `FlippedTriangles(1+0)` | `InvalidTopology` | `yes/4` | `59+29` | `2.66e-15` | `3.99e-14` | `9.29e-04` | `0+0` | open (hol) | `1.2305` | `30.1` |
| frustum 24×6 r=0.5 | turn | `Success(0+0)` | `InvalidTopology` | `yes/3` | `0+0` | `1.02e-12` | `1.05e-11` | `4.97e-13` | `0+0` | `0` | `1.1975` | `23.7` |
| pinched waist 24×8 | turn | `FlippedTriangles(1+1)` | `InvalidTopology` | `yes/4` | `0+1` | `8.88e-15` | `9.85e-14` | `1.51e-14` | `0+0` | `0` | `1.3416` | `40.5` |
| twisted cyl 16×6 | turn | `FlippedTriangles(4+0)` | `InvalidTopology` | `yes/4` | `192+0` | `5.33e-15` | `2.23e-14` | `5.37e-15` | `0+0` | `0` | `1.5193` | `33.5` |
| bent banana 16×8 70° | turn | `Success(0+0)` | `InvalidTopology` | `yes/15` | `77+0` | `2.96e-12` | `1.96e-14` | `3.05e-02` | `0+0` | open (hol) | `1.2930` | `36.8` |
| bent banana (`u_b=0`) | `u_b=0` | `Success(0+0)` | `InvalidTopology` | `yes/3` | `77+0` | `3.53e-13` | `2.21e-01` | `8.67e-01` | `0+0` | open (hol) | `1.0243` | `6.4` |
| sharp cone h=4 (disk) | turn | `Success(0+0)` | `Success(0+0)` | `yes/7` | `154+63` | `3.02e-14` | `5.27e-14` | `1.74e-14` | `0+0` | `0` | `1.4599` | `26.2` |
| sharp cone (`u_b=0`) | `u_b=0` | `Success(0+0)` | `Success(0+0)` | `yes/6` | `154+65` | `1.31e-13` | `1.48e-13` | `2.67e-14` | `0+0` | `0` | `1.4586` | `13.1` |
| 2-hole bumped plate | turn | `Success(0+0)` | `InvalidTopology` | `yes/5` | `104+92` | `1.55e-12` | `5.53e-13` | `4.04e-02` | `0+0` | open (hol) | `1.1787` | `14.0` |

Aggregate solver diagnostics across all 17 runs: `totalSolveFlips = 297`, `nonEuclideanBeforeFlip = 78`, `cotanVsHypDisagreements = 0`.

### Key findings from the solver run

1. **Every P1 collapse case where LSCM fails is flattened with zero intrinsic flips.** On the four symmetric P1 cylinders, the pinched waist and the twisted cylinder (6 cases where LSCM fails and `SpectralConformalParameterization` refuses multi-boundary topology), CETM + Ptolemy converges in 3–4 Newton iterations and unfolds seamlessly with `0` boundary contacts.
2. **Non-Euclidean intermediate states really happen (`78` events in 17 meshes).** During Newton steps, trial scale factors violated Euclidean triangle inequalities 78 times before hyperbolic Delaunay Ptolemy flips restored valid Euclidean triangles. This rules out using the library's existing Euclidean `IntrinsicTriangulation.FlipEdge` directly inside the Newton loop: the Ptolemy update and hyperbolic flip test are genuinely required.
3. **Curvature-only prescription does NOT close asymmetric holes.** On every independently jittered cylinder (`1.6e-4` to `5.3e-3` relative gap), the bent tube (`3.05%`), and the two-hole plate (`4.04%`), rotational holonomy vanishes to `1e-14..5e-13 rad` (so seam edges are parallel), yet the 2D translational period around each hole is nonzero. With fixed boundary scale `u_b = 0` on the bent tube, rotational holonomy is also nonzero (`0.221 rad`). Thus a general uncut annulus solver needs either a seam cut (which the UV atlas already does) or an explicit holonomy-closing boundary/cone adjustment.

## Fit with `IntrinsicTriangulation` and exact arithmetic

- **Connectivity & correspondence:** `IntrinsicTriangulation` already has halfedge flips, signposts, and integer normal coordinates (`IntrinsicIntegerCoordinates`, `IntrinsicCommonSubdivision`, [PR #1311](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1311)), which are directly reusable for tracking correspondence across Ptolemy flips. What is missing is a separate Ptolemy-length / hyperbolic-flip path (since `FlipEdge` unfolds Euclidean triangles and enforces convexity preconditions that break during intermediate Newton steps) plus post-solve projective/barycentric interpolation onto the common subdivision.
- **Exact vs. double flip predicates:** Part 2 of the probe compares `ExactPredicates.InCircle`, an exact `BigInteger` evaluation of the degree-4 Penner hyperbolic polynomial $(a^2+b^2-e^2)cd + (c^2+d^2-e^2)ab$ on binary64 lengths, its `double` evaluation, `double` cotangent sum, and `IntrinsicTriangulation.IsDelaunay`:

| Diamond | Exact `InCircle` | Exact `HypPoly` | Double `HypPoly` | Double `cotSum` | Library `IsDelaunay` | Euclidean len | Ptolemy len |
|---|---:|---:|---:|---:|---|---:|---:|
| cyclic | `0` | `-1` | `-8.88e-16` | `-4.44e-16` | `True` | `1.4142135623730949` | `1.4142135623730949` |
| near inside (`2^-54`) | `+1` | `-1` | `-8.88e-16` | `-4.44e-16` | `True` | `1.4142135623730949` | `1.4142135623730949` |
| near outside (`-2^-54`) | `-1` | `-1` | `-8.88e-16` | `-4.44e-16` | `True` | `1.4142135623730949` | `1.4142135623730949` |
| near inside (`2^-40`) | `+1` | `-1` | `-1.82e-12` | `-9.10e-13` | `True` | `1.4142135623724519` | `1.4142135623724519` |
| near outside (`-2^-40`) | `-1` | `+1` | `+1.82e-12` | `+9.09e-13` | `True` | `1.414213562373738` | `1.414213562373738` |
| clearly inside | `+1` | `-1` | `-0.48507` | `-0.25000` | `False` | `1.2499999999999998` | `1.2591990727455731` |
| noncyclic | `-1` | `+1` | `+0.48507` | `+0.25000` | `True` | `1.6007810593582121` | `1.612752463338847` |

- **What can and cannot be exact:**
  - The hyperbolic Delaunay sign $(a^2+b^2-e^2)cd + (c^2+d^2-e^2)ab$ is an exact degree-4 polynomial for exact rational/dyadic edge lengths, and the Ptolemy update $(ac+bd)/e$ is rational (leaving the dyadic ring after one division).
  - However, converting 3D coordinates to edge lengths (`sqrt`) and scaling by $\exp((u_i+u_j)/2)$ are transcendental. As the `±2^-54` rows show, once `sqrt(2)` rounds to `double`, **even exact `BigInteger` arithmetic on the stored lengths cannot recover the coordinate-level `InCircle` sign** — both signs collapse to `-1`. At `±2^-40`, `HypPoly` separates the signs cleanly while `IntrinsicTriangulation.IsDelaunay` still accepts both due to its `1e-12` tolerance. During all 17 solver runs, `double` `HypPoly` and `cotSum` never disagreed (`0` disagreements).

## Recommendation

**Park as a P1 annulus fix; keep as a candidate only if a caller specifically needs disk/cut-chart conformal flattening with guaranteed local injectivity.**

- **Why park for P1 (#1285):** P1 asks for uncut annular / multi-hole UV charts. Pure curvature-prescribed discrete conformal equivalence leaves nonzero translational holonomy on every asymmetric or jittered annulus (`1.6e-4` to `4.0e-2` of diameter). Closing holes requires an extra holonomy constraint (plus projective common-subdivision interpolation if flips occurred), making it substantially heavier than virtual-fan hole closure or seam cutting (`#1285`) for a P1 fix.
- **Expected build cost if ever resumed:** ~1–2 Tier-L PRs (Ptolemy-length mode + integer normal-coordinate tracking on `IntrinsicTriangulation`, damped Newton driver on `SparseCholesky`, projective interpolation onto `IntrinsicCommonSubdivision`, plus holonomy closing for multi-boundary charts).
- **Guarantee:** locally injective, strictly positive-area intrinsic triangulation on every chart (and globally seamless on simply connected disks/cones and rotationally symmetric annuli); global injectivity on general curved charts still requires an explicit `UVOverlap` check.
