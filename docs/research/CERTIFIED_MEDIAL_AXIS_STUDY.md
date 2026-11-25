# Certified local feature size on thin solids

**Decision.** A positive component-wide lower bound on ordinary local feature size cannot replace either SafeRounding's thin-solid criteria or a positive wrap offset on the procedural boxes. The lower bound is exactly zero even on the unit cube. A certificate for a restricted region, weak feature size, or exterior clearance would address a different question and would need its own specification.

## Exact obstruction at a crease

Let S be the boundary of the solid box [0,1]² × [0,t], with 0 < t ≤ 1. Local feature size at a surface point is its Euclidean distance to the closure of the medial axis, the set of centers with multiple nearest surface points. [Amenta and Bern, definitions 1–2](https://escholarship.org/uc/item/4rc7z0d1)

At the edge point p = (0, 1/2, 0), take centers c = (e, 1/2, e) for 0 < e < min(t/2, 1/2). Their distances to the x = 0 and z = 0 faces are both e, attained at (0, 1/2, e) and (e, 1/2, 0). The other four faces are strictly farther. Thus c lies on the medial axis and its distance to p is √2 e. Sending e to zero gives local feature size zero at the edge. The same witnesses show that the infimum is zero even if the query set excludes the edges but includes all face interiors, since their two contact points also tend to p.

The central box point has distance t/2 to the boundary and contacts on the two opposite z faces. For comparison, the box has thickness proxy 2V/A = t / (1 + 2t), strictly positive for every t > 0. SafeRounding's actual decision also uses coordinate ulps and repair outcomes. A rigid translation preserves local feature size but can change the ulps used for the decision. [SafeRounding design](../design/SAFE_ROUNDING.md) · [Thin-solid change #1421](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1421)

## C# check on the procedural boxes

Run `dotnet run -c Release --project benchmarks/CertifiedMedialAxisProbe`. The unit footprint matches the exact threshold fixtures in `MeshRoundingRepairTestsThinSolids`. The slab heights include the 2⁻²⁷ bound fraction, 256 ulps and 4 ulps at coordinate 1, and a unit cube as the control. The probe uses `TrivialBox3Generator` and the library's exact `Orient2d` predicate to verify that the dyadic contact points belong to triangles on their supporting faces. It checks equal squared radii for both contacts and samples e = t × 2⁻², t × 2⁻¹⁰ and t × 2⁻²⁰. The face-distance argument above certifies nearestness and the limiting result. The probe's printed square roots are rounded for display.

| Box height t | Edge witness distance at e = t × 2⁻²⁰ | Midplane radius t/2 | Tree midpoint distance |
|---:|---:|---:|---:|
| 1 | 1.34869915e-6 | 0.5 | 0.5 |
| 7.4505806e-9 | 1.00485917e-14 | 3.7252903e-9 | 0 |
| 5.68434189e-14 | 7.66646708e-20 | 2.84217094e-14 | 0 |
| 8.8817842e-16 | 1.19788548e-21 | 4.4408921e-16 | 0 |

The diagnostic column from the `DMeshAABBTree3.FindNearestTriangle` squared-distance path reports zero at all three thin-slab midpoints. For example, the exact nearest distance in the 2⁻²⁷ slab is 2⁻²⁸. That path has documented cancellation near the surface. The certificate uses exact face membership and the analytic face distances. Its contact checks scan at most 12 triangles per witness. There is no amount of additional computation that can produce a sound positive lower bound for the whole component.

## Filtered axis, critical distances and wrap clearance

- **λ-medial axis.** The radius of the smallest enclosing ball of the two nearest contacts at c is e/√2. Any fixed λ > 0 filters out the witnesses sufficiently close to the edge. The filtered axis is a subset of the ordinary axis, so distance to it is an upper bound on ordinary local feature size. In fact, the radius of the smallest ball containing the nearest contacts is at most the center's distance to S. Every retained center is therefore at least λ from S. The filtered distance can be positive at the edge even though the ordinary local feature size there is zero. [Chazal and Lieutier](https://geometrica.saclay.inria.fr/team/Fred.Chazal/papers/cl-lma-05/mlambda.pdf)
- **Weak feature size.** A critical center lies in the convex hull of its nearest contacts. For the box, those contacts differ from the center by signed coordinate vectors of the same length. Their convex hull contains the center exactly when they include an opposite pair. The first positive critical distance is t/2, attained at the midpoint. Outside a convex box every point has a unique projection, so there are no exterior critical centers. This identifies an exact positive geometric scale for the box and leaves the ulp and repair-policy questions separate. [Chazal, Cohen-Steiner and Lieutier](https://geometrica.saclay.inria.fr/team/Fred.Chazal/papers/ccsl-stcse-09/ccsl-stcse-09.pdf)
- **Wrap offset.** A single convex box has convex outer parallel bodies at all positive offsets. Two parallel boxes separated by exterior gap g have disjoint closed offset neighborhoods for r < g/2 and meet at r = g/2. By varying g independently of t, exterior clearance can be arbitrarily small at fixed wall thickness. This is an offset-set statement. The discrete wrap also chooses alpha and has its own enclosure and passage requirements. [Alpha wrapping notes](../design/ALPHA_WRAP_PAPER_NOTES.md) · [Passage-wrap conditions](../design/PASSAGE_WRAP.md)

Park a general local-feature-size computation for these decisions. The exact obstruction settles the direct replacement on the first fixture family, with no assumption about the efficiency of a medial-axis algorithm. Any future study of weak feature size or exterior clearance should specify which guarantee the caller needs and account for sharp features, component separation, and coordinate resolution.
