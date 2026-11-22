# Spike: DeFillet (fillet detection and removal) for CAD meshes

Date: 2026-11-22. Study and measurement; no library change. Code and raw output:
[benchmarks/FilletDetection](../../benchmarks/FilletDetection/README.md). Sources:
[sources/meshing-and-repair.md](../sources/meshing-and-repair.md).

Paper: J.-E. Jiang, H. Wang, M. Zhao, D.-M. Yan, S. Chen, S. Xin, C. Tu, W. Wang, *DeFillet: Detection and Removal of Fillet
Regions in Polygonal CAD Models*, ACM TOG 44(4) (SIGGRAPH 2025), [doi:10.1145/3731166](https://doi.org/10.1145/3731166), read in
full. The authors' code is AGPL-3.0 (with CGAL and libigl): licence checked first, code not read, nothing cloned or run.

## Result in short

- **The problem is real and measured.** On a filleted solid with 4 or more chords per quarter circle the dihedral crease test
  of the quad and tet code (30 degrees) finds practically none of the rounded design edges: 0.3 of 17.6 length units on the box
  with 4 chords and 0 from 8 up, 15.4 of 19.6 on the L profile, 21.4 of 41.3 on the gear (only the rims that were never
  rounded). A lower threshold does not help: at 20 or 10 degrees it finds two to six parallel lines per edge instead of one.
- **The existing curvature does not solve it.** A threshold on `MeshPrincipalCurvature` reaches F1 0.72 on CAD strip
  tessellations and 0.45 on uniformly subdivided ones; the best possible curvature band (chosen with the answer) 0.87 and
  0.55. It marks sharp rims as fillets (precision 0.28 on the L profile), it marks a plain cylinder, and its radius is off by
  40 % and more.
- **The paper's detector, as implemented here from the text, does not carry over to these inputs**: with the radius bound
  set, F1 0.43 on CAD strips and 0.48 on subdivided meshes, it marks 58 to 100 % of a plain rod, and its score changes
  under rotation (box, subdivided: F1 0.75 and 0.55). With the paper's default bound it cannot see the box and the L profile
  at all (their radii are above the bound). It needs dense, uniform samples, its tolerance and radius bound are model
  specific, the paper gives no detection numbers, and the cylinder test it describes is not in the released code (the
  authors' README calls it unstable). This is a statement about an untuned reimplementation on these solids, not about the
  authors' program (section 3 lists what differs).
- **A medial-ball detector built on the paper's idea works on all of them**: precision 1.00, recall 1.00 (worst case F1 0.99)
  on 30 cases with fillets, nothing marked on the rod and the gear's bore, radius within 1.2 % with 8 or more chords, the same under rotation. It is per facet, has no global optimisation and one user parameter (a radius bound; 1.1 to
  10 times the fillet radius gave the same precision). It misses a few triangles of coarse sphere octants (vertex fillets
  with 2 or 4 chords: recall there 0.81 and 0.95).
- **Sharp-edge reconstruction works where it is easy and shows what a real one needs.** With right labels one closed-form
  step puts the surface back on the design solid on the box, the plate and the gear (sampled Hausdorff distance below
  0.00005; crease length equal to the design's up to 8 chords, 1 to 4 % above it and up to 30 degenerate triangles at 16 and
  32). It breaks (flipped triangles) where a face shares vertices with a fillet, and a handful of missed fillet triangles
  ruin it.

**Leaning: go for detection as an opt-in analysis class (the medial-ball detector, not the Voronoi pipeline), after a
second measurement on real CAD tessellations; no-go for the paper's detection pipeline; reconstruction only as a later,
separate step.** Options at the end.

**Outcome of the second measurement (2026-11-22): stop, nothing is built.** On 63 ABC models (27 with fillets under the
project's heuristic) the medial-ball detector reaches recall 0.90 with a bound of 0.1 or 0.2 of the diagonal, but a mean
precision of only 0.45 (4 of 27 models at 0.95 or more), and it marks 24 of the 36 models that have no fillet. The stop rule
fixed before the run (precision below 0.95 or recall below 0.9) is met under all six bounds, so option A, and the options
B and C that build on its labels, are not built (option D). Numbers and reading:
[benchmarks/FilletDetection](../../benchmarks/FilletDetection/README.md#result-of-the-second-measurement-2026-11-22).

## 1. Prior art and building blocks

### The paper

Idea: a fillet is the envelope of a ball rolling between two faces, so the ball's centre defines an osculating sphere; the
Voronoi vertices of surface samples are candidates for such centres and crowd along the centre curve.

Detection (section 4.2): scale into the unit sphere; samples = triangle centroids; Voronoi vertices of the samples (CGAL), in
1000 overlapping patches; a vertex with radius r is related to the samples at distance r to r + epsilon (default 0.006);
vertices are dropped if outside the bounding box, if r is above a bound (default 0.06), if the related samples are too close
together or not a disk on the mesh; each sample takes the related vertex of the highest density (k = 30 nearest vertices);
its score is the rate of change of that radius to its three neighbours; a graph cut (smoothness = edge length) gives the
labels. Cylindrical holes are to be told apart by the angle the related samples span (below or above 180 degrees).

Removal (section 4.3): each fillet triangle gets the normal of the nearest face outside the fillet (geodesic distance);
vertex positions minimise an edge term (edges perpendicular to the target normals of both sides), a face term (centroid on
the target plane) and a step term, iterated as a sparse linear solve.

What the paper does not give: precision / recall of the detection (figures only), any result on meshes that are not densely
and uniformly sampled (it assumes them and names Poisson sampling as the remedy), run times below 71k triangles. Stated
limits: fillets that meet at a corner are missed when the edges turn by less than 108 degrees; removal depends on triangle
quality. Run times given: 3 to 36 s for the detection on 71k to 873k samples, 9 to 335 s for the removal (i9-13900K).

### How others solve it

- Parametric models: blend recognition on the B-rep (rolling-ball conditions on faces: Venkataraman et al. 2001, Li et al.
  2009, patents US 6760038, US 8935130), removal by extending the support faces to their intersection. Needs the B-rep.
- Meshes: fillets fall out of primitive segmentation as "rolling-ball blends" (Wu and Kobbelt 2005) or as tori and cylinders
  (RANSAC, Schnabel et al. 2007); the paper shows these give rough regions only. Curvature-line circle fitting is a Dassault
  patent application (US 2024/0135733). Learned detectors work on B-rep faces (FilletRec, arXiv:2511.05561) or point clouds.
- Sharpening without detection (Attene et al. 2005, feature-preserving reconstruction) recovers only fillets below the
  sample spacing.
- The medial axis view (a fillet's medial sheet degenerates to the centre curve, with constant radius) is standard; no
  published detector that tests medial balls per facet on the mesh was found (terms searched: fillet / blend detection,
  medial axis, rolling ball, triangle mesh, defeaturing).

### In the library

| building block | use | finding |
|---|---|---|
| `MeshTopology.CreaseAngle` (used by `CrossField`, `QuadRemesher`, `PositionField`), `AlphaWrapFeaturePass.CreaseAngleDegrees` (tet S1-F) | the crease test fillets defeat | section 2 |
| `MeshPrincipalCurvature` (Rusinkiewicz 2004) | curvature baseline | section 3 |
| `MeshWallThickness.InscribedSphere` (shrinking ball of Ma et al. 2012 with the angle condition of Dey and Zhao 2004) | the medial ball; the prototype repeats its iteration because it needs both sides, a radius bound instead of a first ray, and the ball's centre | a library version would share that code |
| `Delaunay3` | the paper's Voronoi vertices (circumcentres) | works: 94k samples, 328k cells, 3 to 8 s for the whole detector; no sample was refused |
| `DMeshAABBTree3`, `DistPoint3Triangle3` | ball queries | as is |
| `PrimitiveFit3` | not used: the ball already gives the radius; it is a consumer (section 6) | - |
| graph cut | none in the library (`QuadFlowRepair` has a flow for another purpose) | the prototype has its own Dinic max flow; the medial detector needs none |

## 2. Upper bound: what the crease test sees

`dotnet run -c Release --project benchmarks/FilletDetection -- crease` (`results/crease.md`). Length of the edges with a
dihedral angle above the threshold, on the filleted solid: inside fillets (folds between fillet facets or to the tangent
face), elsewhere (real design creases that were never rounded), and on the solid before filleting.

| solid | chords per 90 degrees | in fillets, 30 degrees | elsewhere | design | in fillets, 20 degrees | 10 degrees |
|---|---|---|---|---|---|---|
| box | 2 | 16.18 | 0.00 | 17.60 | 46.94 | 49.69 |
| box | 4 | 0.30 | 0.00 | 17.60 | 47.89 | 82.77 |
| box | 8 | 0.00 | 0.00 | 17.60 | 0.00 | 110.51 |
| box | 16, 32 | 0.00 | 0.00 | 17.60 | 0.00 | 0.00 |
| lprofile | 2 | 3.60 | 15.34 | 19.60 | 10.80 | 10.80 |
| lprofile | 4 to 32 | 0.00 | 15.40 | 19.60 | 10.80 (4), 0.00 | 18.00 (4), 25.20 (8), 0.00 |
| gear | 2 | 28.80 | 24.36 | 44.39 | 48.00 | 48.00 |
| gear | 4 to 32 | 0.00 | 21.4 | 41.3 | 48.00 (4), 0.00 | 86.40 (4), 105.60 (8), 0.00 |

Reading: a fillet with n chords folds by 90 / n degrees per chord. Above the threshold every chord line is a crease (several
parallel feature lines per design edge: 110 length units instead of 17.6 on the box at 10 degrees); below it there is none.
No threshold gives one line per design edge. With the default 30 degrees and 4 or more chords, the rounded edges are
invisible to `PreserveCreases`, `AlignCreases` and the tet feature pass: 100 % of the box's design edges, 21 % of the L
profile's, 48 % of the gear's. That share is what a detector plus reconstruction could give back to those consumers.

## 3. Detection

`dotnet run -c Release --project benchmarks/FilletDetection -- detect` (`results/detect.md`, 200 rows; summary by
`summarize.py` in `results/summary.md`; the bound sweep in `results/bound.md`). Five solids with the answer per triangle (box, thin plate, L profile, gear with a
bore, a plain rod), 2 to 32 chords per quarter circle, as CAD strips and uniformly subdivided, 8 chords also rotated; see
the benchmark README. Scores by area. Every detector gets the same hint, a radius bound of twice the largest fillet
radius; "paper defaults" uses the paper's bound instead.

| detector | tessellation | cases | precision | recall | F1 mean | F1 worst | radius error | rod marked (worst) | s per 10k triangles |
|---|---|---|---|---|---|---|---|---|---|
| curvature threshold | CAD strips | 24 | 0.76 | 0.80 | 0.72 | 0.34 | 0.43 | 1.00 | 0.0 |
| curvature, oracle band | CAD strips | 24 | 0.89 | 0.87 | 0.87 | 0.58 | 0.46 | 0.00 | 0.0 |
| DeFillet, paper defaults | CAD strips | 24 | 0.50 | 0.02 | 0.05 | 0.00 | 0.37 | 0.00 | 1.3 |
| DeFillet, radius bound set | CAD strips | 24 | 0.65 | 0.63 | 0.43 | 0.10 | 0.54 | 1.00 | 2.2 |
| medial ball | CAD strips | 24 | 1.00 | 1.00 | 1.00 | 0.99 | 0.08 | 0.00 | 13.0 |
| curvature threshold | subdivided | 6 | 0.59 | 0.48 | 0.45 | 0.35 | 0.46 | 0.87 | 0.0 |
| curvature, oracle band | subdivided | 6 | 0.52 | 0.65 | 0.55 | 0.39 | 0.78 | 0.00 | 0.0 |
| DeFillet, paper defaults | subdivided | 6 | 0.42 | 0.77 | 0.50 | 0.17 | 0.49 | 0.85 | 0.6 |
| DeFillet, radius bound set | subdivided | 6 | 0.36 | 0.88 | 0.48 | 0.19 | 0.049 | 0.75 | 1.2 |
| medial ball | subdivided | 6 | 1.00 | 1.00 | 1.00 | 1.00 | 0.025 | 0.00 | 5.8 |

("Paper defaults" is no fair row for the box and the L profile: their fillet radii, 0.07 to 0.14 of the bounding sphere
radius, are above the paper's default bound of 0.06, so rule 1 removes every true centre; it is listed to show that the
defaults are model specific. The row to read is "radius bound set". Means over the cases with fillets; a detector that marks
nothing counts with precision 0. Radius error: mean over the cases
of the area weighted median of abs(r - r_true) / r_true on the true positives. The subdivided set is small because the
subdivision is capped at 150k triangles: box 4 and 8 chords, L profile 4 and 8, two rotated.)

Per solid at 8 chords (precision / recall / F1):

| solid | curvature | oracle band | DeFillet, bound set | medial ball |
|---|---|---|---|---|
| box, strips | 1.00 / 0.88 / 0.94 | 1.00 / 1.00 / 1.00 | 0.56 / 0.55 / 0.56 | 1.00 / 1.00 / 1.00 |
| box, subdivided (94k); rotated | 0.94 / 0.37 / 0.53 | 0.73 / 0.62 / 0.67 | 0.61 / 0.95 / 0.75; 0.39 / 0.95 / 0.55 | 1.00 / 1.00 / 1.00 |
| plate, strips | 1.00 / 0.88 / 0.93 | 1.00 / 1.00 / 1.00 | 1.00 / 0.06 / 0.11 | 1.00 / 1.00 / 1.00 |
| lprofile, strips | 0.28 / 0.91 / 0.42 | 0.96 / 0.62 / 0.75 | 0.07 / 1.00 / 0.13 | 1.00 / 1.00 / 1.00 |
| lprofile, subdivided (128k); rotated | 0.27 / 0.50 / 0.35 | 0.36 / 0.58 / 0.44 | 0.21 / 0.85 / 0.34; 0.11 / 0.87 / 0.19 | 1.00 / 1.00 / 1.00 |
| gear, strips; rotated | 0.61 / 0.69 / 0.65 | 0.63 / 0.75 / 0.68 | 1.00 / 1.00 / 1.00; 0.52 / 1.00 / 0.68 | 1.00 / 1.00 / 1.00 |
| rod (share of the cylinder marked), strips / subdivided | 1.00 / 0.58 | 0.00 / 0.00 | 1.00 / 0.58 | 0.00 / 0.00 |

### Reading

**Curvature.** Good on the one solid without a sharp edge and with strip tessellation (box: the curvature is exactly the
fillet's). It fails for three reasons that no threshold removes: (1) a sharp edge has a large discrete curvature whose value
depends on the triangle size, so rims fall into any band (L profile: precision 0.28; the oracle band buys precision with
recall 0.62); (2) on a subdivided faceted fillet the curvature is zero inside a facet and concentrated on the chord lines
(the stripes the paper shows in its figure 21): recall 0.37 on the box; (3) a cylinder has the curvature of a fillet. The
radius from 1 / curvature is 30 to 160 % off except on the strip box and plate. The oracle band is the ceiling of every
curvature-threshold design: 0.87 on strips, 0.55 subdivided.

**The paper's detector** (as reimplemented: one global Voronoi diagram instead of patches, the three filter rules, density,
transform, score, graph cut, duplicate vertices merged; no cylinder test; the paper's defaults for tolerance, k, sigma and
lambda, nothing tuned).

- On CAD strips the samples are too few and too regular: the centroids of the strips of one fillet lie on two circles, their
  Voronoi vertices are degenerate, and the relation of eq. 1 holds for few vertices (box: recall 0.55 with the bound set;
  L profile: recall 1.00 at precision 0.07). The unrotated gear is the one good case (F1 1.00 from 4 chords; 0.68 rotated).
- On the subdivided meshes the recall is there (0.88, radius within 5 %) but the precision is 0.36: flat faces next to a
  fillet and near sharp edges also get nearly constant radii, from vertices in the wedge of two faces. The graph cut does
  not remove them: with the paper's lambda = 1 its smoothness (edge lengths of about 0.01 to 0.03 in the unit sphere)
  is far below the data term (0 to 1 per triangle), so the labels are in effect the score thresholded at 0.5. A larger
  lambda or an area weight might help; it was not tried.
- It is not stable under rotation (table above: 0.75 against 0.55, 0.34 against 0.19, 1.00 against 0.68). The paper's
  bounding-box rule is axis dependent, and so is which of several equally dense vertices a sample takes.
- The rod is marked to 58 to 100 % with the bound set: without the cylinder test a cylinder is a fillet to this method.
- Not reproduced from the paper: the patches (a speed-up and a help with occluding inner walls), the hole test of rule 3, the
  cylinder test, a sampling as dense as the paper's examples. Each could improve the numbers; the dependence on dense
  uniform samples and on two model-specific lengths would stay.
- Cost: a 3D Delaunay triangulation of all centroids plus a k-nearest-neighbour pass, 0.6 to 2.2 s per 10k triangles here
  (3 to 8 s on the 94k box, 6 to 25 s on the 128k L profile).

**The medial-ball detector** (own construction on the paper's idea; `MedialDetector`). Per planar facet (triangles
connected without a fold) and side: the largest empty ball that touches the facet, by the shrinking-ball iteration of
`MeshWallThickness` (bounded by the radius bound, largest over 7 points per triangle). The ball is a rolling ball if (a) its
radius is below the bound, (b) the triangles around that touch it (within 10 % of the radius, connected over shared
vertices) turn away from the facet's normal to two opposite sides by between 0.5 and 50 degrees, and (c) none of them
turns by more than 135 degrees. (b) rejects the ball in the wedge of a sharp edge (contact to one side), between parallel
walls (no turning contact) and on the flat bottom of a slot or blind hole (contacts 90 degrees away); (c) rejects cylinders
and spheres. No sampling density assumption, no global step, no graph cut; parallel and deterministic by construction.

- 30 of 30 cases with fillets at precision 1.00; recall 1.00 except on the sphere octants with 2 and 4 chords (0.81, 0.95:
  the facets of a coarse sphere patch are single triangles and the ball at their 7 sample points is up to 10 % too small).
- Nothing marked on the rod (10 cases: the 135 degree rule) and on the gear's bore (excluded by the radius bound in the main
  run; with a bound of 0.4, above the bore's 0.3, also nothing: `results/bound.md`).
- Radius error 0.2 to 1.2 % with 8 or more chords (the inscribed radius of the chord polygon), 2 to 11 % with 4, 23 % with 2
  (83 % on the gear with 2: one or two chords per fillet there).
- Radius bound 1.1, 2, 4 and 10 times the fillet radius at 8 chords (strips, `results/bound.md`): precision 1.00 each time; recall 1.00 from 2 up, 0.75
  to 0.93 at 1.1 (the bound must clear the radius by more than the 10 % tolerance).
- Rotated and moved: the same scores.
- Cost of the prototype: 6 to 13 s per 10k triangles on 4 cores in the mean, up to 24 s (plate, 32 chords: the contact search
  is repeated per triangle of a sphere patch), and about twice as long on the rotated subdivided meshes (one tree traversal
  per ball step). Not optimised; the ball queries are those of `MeshWallThickness`.

### What these numbers do not cover

- Only procedural solids: constant radius, planar supports (and one cylinder / bore as a negative), exact tangency, no noise.
- Not measured: variable radius, fillets between curved faces, fillets that fade out, full rounds (a half cylinder between
  two parallel faces is a fillet by design intent and is rejected or split by the 135 degree rule), chamfers, remeshed or
  scanned surfaces with noise, real CAD exports (ABC, Fusion 360 Gallery, Thingi10K), meshes with defects.
- The thresholds of the medial detector (10 % contact tolerance, 0.5 / 50 / 135 degrees, 10 degrees separation) were set on
  these solids. A second, independent set is needed before they are called defaults. The paper's detector got no such
  tuning (section 3).
- The medial detector is deterministic, but its verdict per facet starts from the facet's triangle with the largest ball
  (ties: the lowest id) and its contact search stops at 20000 triangles: the result is not yet proven independent of the
  triangle numbering, which the invariance tests of a library version would have to cover.
- The subdivided set is small (2 solids, 4 meshes plus 2 rotations) and its triangle size follows the fillet chord, about 5
  times the paper's tolerance; the paper's examples are denser (71k to 873k samples on one part).
- The oracle band is the best of 40 x 40 log-spaced bounds, so the true ceiling of a curvature band can be slightly higher.

## 4. Sharp-edge reconstruction

`dotnet run -c Release --project benchmarks/FilletDetection -- defillet` (`results/defillet.md`). Built only after the
detection worked, as the smallest version that can answer "are the labels good enough to rebuild the design edge": each
fillet triangle takes the plane of the nearest tangent support (nearest along the mesh edges, as the paper's target normals);
each vertex of a fillet triangle moves to the least squares point of the planes of its triangles, held in place where they
leave it free. One closed-form step, connectivity unchanged. Compared with the solid before filleting.

| solid (strips unless noted) | labels | Hausdorff before | after | crease length after / design | degenerate | flipped |
|---|---|---|---|---|---|---|
| box, 8 chords (also rotated, also subdivided 94k) | known answer and medial ball | 0.1098 | 0.0000 | 17.60 / 17.60 | 0 | 0 |
| box, 16 / 32 chords | both | 0.1098 | 0.0000 | 17.75 / 17.60, 18.28 / 17.60 | 0 | 0 |
| plate, 8 chords | both | 0.0293 | 0.0000 | 14.40 / 14.40 | 0 | 0 |
| plate, 16 / 32 chords | both | 0.0293 | 0.0000 | 14.44, 14.58 / 14.40 | 12, 30 | 0 |
| gear, 2 to 32 chords | both | 0.0092 to 0.0137 | 0.0000 | equal (41.28 / 41.28 at 8) | 0 | 0 |
| lprofile, 2 and 4 chords | both | 0.0828 | 0.0000 | 19.60 / 19.60 | 0 | 0 |
| lprofile, 8 to 32 chords | both | 0.0828 | 0.0000 | 20.1 to 20.9 / 19.60 | 0 to 68 | 6 to 10 |
| lprofile, subdivided (128k) | both | 0.0828 | 0.0459 | 20.49 / 19.60 | 0 | 110 |
| box, 2 / 4 chords | known answer | 0.1098 | 0.0000 | 17.60 / 17.60 (subdivided, 4 chords: 24.65, a jagged label border) | 0 | 0 |
| box, 2 / 4 chords | medial ball | 0.1098 | 0.29 / 0.64 | 44.8, 51.7 / 17.60 | 0 | 16, 28 |
| plate, 2 / 4 chords | medial ball | 0.0293 | 0.41 / 0.75 | 38.5, 51.8 / 14.40 | 0 | 16, 36 |

Reading:

- The Hausdorff distance is sampled (vertices, edge midpoints, centroids, both ways) and printed to four decimals; the
  vertex step has a regularisation of 1e-6, so "0.0000" means below 0.00005, not exact, and it says nothing about flipped
  triangles (the L profile rows have both).
- With right labels and supports that keep their vertices (box, plate, gear) the surface comes back on the design solid,
  corners included, and the crease test then sees every design edge. At 16 and 32 chords the crease length is 1 to 4 % above
  the design's and the plate gets 12 and 30 degenerate triangles (thin triangles at the corners, not cleaned). The gear's 41.28 against 21.4 before is the gain of section 2
  realised.
- **Faces that share vertices with a fillet fold.** On the L profile the caps are triangulated to the arc's vertices; when
  those move to the design corner the cap triangles around them flip (6 to 110) and, on the subdivided mesh, the surface is
  0.046 off. A real reconstruction has to retriangulate the support faces (or work on a remeshed band), and collapse the
  degenerate triangles it creates. The paper moves fillet vertices only as well and names triangle quality as its limit.
- **Missed fillet triangles are fatal.** On the coarse sphere octants the medial detector misses 5 to 19 % of the corner
  area; a missed triangle counts as a support with a wrong plane and the result is worse than the input (Hausdorff 0.29 to
  0.75 against 0.11 / 0.03). Reconstruction needs labels that are consistent per region (the paper's graph cut has this
  role), a check of every support (large, planar or fitted), and a refusal when the result is not clean. Detection alone
  does not have this risk.
- Not covered: curved supports (taken as tangent planes here), variable radius, fillets of a concave-convex mix at one
  corner, and any guarantee (closed, manifold, no self-intersection).

## 5. Go / no-go

| item | verdict | why |
|---|---|---|
| The paper's detection pipeline (Voronoi vertices, density, graph cut) | **no-go** | untuned F1 0.43 to 0.48 here against 0.72 for a plain curvature threshold on strips, not stable under rotation; needs dense uniform samples and model-specific lengths; cylinder test missing; a 3D Delaunay triangulation per mesh. The licence allows no look at how the authors resolve these |
| Curvature threshold as the detector | **no-go** | ceiling F1 0.87 / 0.55 with the answer known; marks sharp rims and cylinders |
| Medial-ball detector | **go for a second measurement, then an opt-in analysis class** | 1.00 / 1.00 on every procedural case, one parameter, local, reuses `MeshWallThickness`; open: real CAD exports, variable radius, coarse sphere patches, speed |
| Sharp-edge reconstruction | **not now** | exact in the easy cases, but needs support retriangulation, region-consistent labels and guarantees; a project of its own, and it changes geometry |

Order of the work: the coordinator asked for the numbers of section 2 before the Delaunay prototype and for a stop if
curvature alone reached the target; the message arrived after the prototype was built. The curvature ceiling (section 3)
does not reach it, so the prototype was due, but the check-in was skipped.

Honest size of the evidence: 40 procedural cases from 5 solids, built by the same session that built the detector. The
detection result is strong enough to justify the second measurement, not to justify defaults.

## 6. Options (owner's call)

How others solve it: CAD systems detect blends on the B-rep and remove them by extending the supports; on meshes the
published route is DeFillet's (measured above) or learned B-rep / point classifiers; commercial mesh tools offer "remove
fillet" as an interactive, radius-bounded selection. Nothing found that feeds fillet labels into remeshing or simplification
automatically.

**Option A (leaning): a second measurement, then `MeshFilletDetection` as an analysis class.** No consumer changes its
output. Sketch (names and defaults are proposals):

```csharp
public class MeshFilletDetection
{
    public MeshFilletDetection(DMesh3 mesh);
    public double MaxRadius = 0;             // the largest fillet radius to look for; 0: derived from the input's scale
    public double MinRadius = 0;
    public Func<bool> CancelF;
    public bool Compute();                   // false: empty mesh or cancelled

    public bool[] IsFillet;                  // per triangle id
    public double[] Radius;                  // per triangle id, NaN outside fillets
    public IReadOnlyList<FilletRegion> Regions;
}
public class FilletRegion
{
    public int[] Triangles;
    public double Radius;                    // area weighted median
    public bool Convex;                      // ball inside the solid
    public int[] SupportTriangles;           // the tangent faces next to it
}
```

It would follow the house rules for a new operation (invariance over `Similarity.All()`, reuse, determinism serial against
parallel, a result check) and get a g3cli command and a guide page. Before that, the second measurement: 20 to 50 CAD
exports with fillets (ABC or Fusion 360 Gallery STEP files tessellated outside the repository, labels from the B-rep face
types; fetched by a script, not committed), plus procedural variable-radius and curved-support solids. Stop rule: below
precision 0.95 or recall 0.9 on that set without a principled fix, the class is not built.

**Option B: consumers of the labels, each opt-in and each its own PR after A.** In the order of expected gain per effort:

1. `PrimitiveFit3` / segmentation: exclude fillet triangles when fitting planes and cylinders to the supports, and report
   fillets as their own primitive with a known radius. No geometry change; the paper shows the same use (its figure 23).
2. `Reducer`: treat the two boundary lines of a fillet region as feature curves, so that a reduction keeps the fillet as a
   band of long strips instead of eating into the faces (today nothing marks them: 0 creases at 30 degrees). Needs a
   measurement against the current result at equal triangle count.
3. `QuadRemesher` / `CrossField`: align the field with the fillet's axis and lay lattice lines on its boundary lines (a
   softer `AlignCreases`). `CurvatureAlignment` already gives the axis direction on smooth fillets; the gain is on faceted
   ones (the stripes of section 3). To be measured first.
4. Tet meshing S1-F: no use for the labels alone; the feature pass wants sharp creases. Only option C serves it.

**Option C: reconstruction (`MeshDefillet`), later.** For users who want the design edges back (CAE defeaturing, the tet
feature pass, re-filleting with another radius). Needs: support fitting and retriangulation, region-consistent labels, a
clean-result check with refusal (`MeshRobustRepair.NotCleanReason` style), curved supports. Estimated as a multi-PR feature;
a design note first.

**Option D: stop here.** The crease options keep their documented limit ("a filleted edge is not a crease"); the study and
the benchmark stay as the record.

Open questions for the owner:

1. A or D (the second measurement costs one session and needs a source of real filleted meshes outside the repository)?
2. If A: the radius bound is derived from the input's scale by default (coordinator, 2026-11-22: no model-specific
   parameter in the API). Which scale is part of the second measurement: a share of the bounding box diagonal (the paper
   uses 0.06 of the bounding sphere radius; the box here needs 0.11), or a multiple of the median wall thickness, which the same
   ball queries give. Not measured yet; the bound only has to clear the
   radius (1.1 to 10 times gave the same precision), so a generous default costs time, not correctness, on these solids.
3. Is reconstruction (C) wanted at all, given that it changes the geometry the user gave?
