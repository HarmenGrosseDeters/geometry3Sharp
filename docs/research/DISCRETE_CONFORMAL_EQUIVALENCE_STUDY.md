# Discrete conformal equivalence on the P1 annulus reproducers

Study for the discrete conformal equivalence plan in [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424). The P1 failure and its existing alternatives are tracked in [PR #1285](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1285).

## What the paper gives us

Gillespie, Springborn and Crane alternate vertex scale updates with intrinsic Delaunay flips using Ptolemy's relation. They track the correspondence through changes of triangulation with normal coordinates and reconstruct the map by laying out the resulting metric and interpolating across the correspondence. Section 4.4 treats boundary angles or scale factors. The planar guarantee is local injectivity. A seamless planar annulus also requires the layout to return to the same frame and position after one circuit of the hole. For example, the original polygonal cylinder metric already has zero interior angle defect and straight developed boundaries. It develops to a strip with a nonzero translation equal to its perimeter around the hole, so those angle targets admit a solution that cannot close in the plane. As a separate condition, even a closed layout needs a check for global overlap. [Gillespie et al., 2021](https://markjgillespie.com/Research/CEPS/Paper.pdf)

Campen et al. address prescribed interior and boundary curvature via a symmetric double cover, including numerical stability near Delaunay-critical configurations. This approach supplies a metric. The target curvature and the requirement that the annulus develop to a single planar chart still need to be specified by the application. [Campen et al., 2021](https://arxiv.org/abs/2104.04614)

## Procedural reference and C# comparison

Take a unit-radius polygonal cylinder with axial strip height `h` and the same angular samples on every ring. In the plane, put the rings at the same angular samples with radii decreasing inward. Set the next radius `R'` by `log(R'/R) = -2 asinh(h/2)`. Then `R - R' = h sqrt(R R')`. If `c` is a chord length on the unit circle, horizontal edges are scaled by their ring radius. The axial edges and the strip diagonal are scaled by `sqrt(R R')` because the diagonal in the layout has squared length `(R - R')² + R R' c²`. All three edge types therefore satisfy the paper's vertex scaling relation with `u = log(R)`. The polygonal rings are nested homothetic convex polygons on these cases. Their trapezoidal strips tile the annulus, and the triangulations in the strip have positive oriented area. The argument also works when the angular spacing varies around the cylinder, provided the angles remain ordered with every gap less than π.

The [C# probe](../../benchmarks/DiscreteConformalAnnulusProbe/Program.cs) compares this construction with `LSCMParameterization` on the 8-sided, 3-strip P1 test cylinder, a taller cylinder, a finer cylinder and a cylinder with the same deterministic angular jitter on every ring. It runs the library's intrinsic Delaunay pass on the original metric, reuses `ParameterizationDistortion`, counts contacts of nonadjacent UV boundary edges with exact orientation signs, and checks the maximum relative residual in the vertex scaling equation over all mesh edges. The shared jitter preserves the strip geometry. It differs from the independently jittered cylinders of the earlier spectral study.

```sh
LC_ALL=C dotnet run --project benchmarks/DiscreteConformalAnnulusProbe -c Release
```

Measured in Release on Linux x64 with .NET SDK 10.0.404.

```text
chart | method/status | flipped + degenerate | nonadjacent boundary contacts | mean conformal distortion
P1 | LSCM/FlippedTriangles | 2 + 0 | 0 | 1.96845E+13
P1 | vertex scaling | 0 + 0 | 0 | 1.65637 | edge residual 6.66134E-16 | initial Delaunay flips 0
tall | LSCM/FlippedTriangles | 2 + 0 | 0 | 1.41635E+13
tall | vertex scaling | 0 + 0 | 0 | 2.03256 | edge residual 4.44089E-16 | initial Delaunay flips 0
refined | LSCM/FlippedTriangles | 0 + 2 | 0 | 7.50557E+10
refined | vertex scaling | 0 + 0 | 0 | 1.22047 | edge residual 6.66134E-16 | initial Delaunay flips 0
shared angular jitter | LSCM/FlippedTriangles | 2 + 0 | 1 | 3.34283
shared angular jitter | vertex scaling | 0 + 0 | 0 | 1.65651 | edge residual 6.66134E-16 | initial Delaunay flips 0
diamond | exact incircle | cotangent sum | library Delaunay | Euclidean new length | Ptolemy new length
cyclic | 0 | -4.4408920985006262E-16 | True | 1.4142135623730949 | 1.4142135623730949
near inside | 1 | -4.4408920985006262E-16 | True | 1.4142135623730949 | 1.4142135623730949
near outside | -1 | -4.4408920985006262E-16 | True | 1.4142135623730949 | 1.4142135623730949
clearly inside | 1 | -0.25000000000000056 | False | 1.2499999999999998 | 1.2591990727455731
noncyclic | -1 | 0.24999999999999961 | True | 1.6007810593582121 | 1.612752463338847
```

LSCM reports two flipped or degenerate triangles on every chart. The closed-form vertex-scaled layout has zero on all four charts and no nonadjacent boundary contacts. Its maximum edge-scaling residual is below 7e-16 in this run. The intrinsic pass made zero flips on these inputs, so the result isolates vertex scaling and does not measure the effect of Ptolemy flips on annulus quality. The distortion column measures affine distortion between triangles, a different criterion from discrete conformal equivalence.

Replacing the discrete radial step with the smooth-cylinder step `-h` in the probe makes the P1 edge-scaling residual about 0.015, which fails the assertion despite the map having no flipped triangles.

The two near-cyclic perturbations have different exact incircle signs. The computed cotangent sum is identical for both, and the library accepts both under its default tolerance. The tolerance explains why a small negative sum is accepted, but even a zero tolerance cannot recover the distinction once both sums have rounded to the same value. The noncyclic Euclidean and Ptolemy diagonals differ by about 0.012 in this example.

## Fit and arithmetic

The API can lay out and flip the existing **Euclidean** metric. `IntrinsicTriangulation.FlipEdge` computes the new diagonal by unfolding the two metric triangles and updates the signposts. The measured diamond shows that this agrees with Ptolemy at a cyclic event and disagrees away from it. The paper uses Ptolemy updates on lengths that can temporarily violate Euclidean triangle inequalities, so the current Euclidean geometry and convexity preconditions do not describe those intermediate states. The integer normal-coordinate work in [PR #1311](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1311) is relevant for tracking correspondence through flips. A prototype would still need coherent connectivity, length and correspondence updates plus the final layout. The general conformal solver and interpolation were outside this first probe.

- **Delaunay sign.** For explicit planar points the library's exact `InCircle` gives the reference sign. In the intrinsic metric, suppose the two opposite angles have cotangents `p/sqrt(P)` and `q/sqrt(Q)`. For squared adjacent sides `a,b` and squared shared length `s`, take `p = a+b-s` and `P = 4ab-p²`, with the analogous formula for the other triangle. For nondegenerate triangles, equal signs settle the sum directly. With opposite signs, compare `p² Q` and `q² P` and assign the sign of the larger term. This gives an exact polynomial decision for exact squared-length inputs. Exact signs on rounded intrinsic lengths give decisions for the represented metric and cannot recover lost geometric information.
- **Ptolemy update.** The sum of two products divided by the old diagonal is rational in the stored lengths and can be evaluated exactly on rational inputs. The result may leave the dyadic domain after one division. Euclidean input lengths and vertex scaling generally introduce square roots and exponentials, so exact arithmetic on rounded values does not establish exactness for the unrounded mathematical metric. The probe measures the formula in double precision on explicit convex diamonds.

## Decision

Keep the closed-form layout as a reference for this cylindrical family. It demonstrates that the P1 collapse on the measured fixtures is avoidable by discrete conformal scaling. It does not establish that the general paper algorithm fixes independently perturbed or multiply connected charts. Park a general P1 implementation until there is a formulation that enforces closure around each hole and a test that checks global injectivity. Its cost would include a curvature solve and repeated Delaunay passes, plus correspondence reconstruction and planar layout. The cheap cylindrical construction supplies a regression oracle for future candidates, while these measurements do not estimate solver runtime.
