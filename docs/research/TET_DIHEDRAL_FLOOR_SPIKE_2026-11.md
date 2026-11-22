# Tet meshing: a proven dihedral floor, and what surface chamfering adds (research spike, 2026-11-22)

**Status:** research spike for the owner's decision (HP 5 / stage P5(b), plan #1281). No library code. The prototype is a
benchmark mode (`benchmarks/TetMeshBenchmark/Stuffing.cs`, modes `floor` and `floornoise`).
Owner decisions in force and not touched: P4(c) (#1250, input-conforming CDT) stays parked; no higher default target; the
#1154 work-bound default is the owner's.

Sources read (paper text only, no code): L. Diazzi, J. Dai, D. Panozzo, M. Attene, *Surface chamfering for robust tetrahedral
meshing*, ACM TOG 45(4), SIGGRAPH 2026, DOI 10.1145/3811395 (the authors' PDF); F. Labelle, J. R. Shewchuk, *Isosurface
stuffing: fast tetrahedral meshes with good dihedral angles*, SIGGRAPH 2007 (the author's PDF, in full). The chamfering
paper's reference implementation was not opened (licence not checked, so paper only). No CGAL, TetGen, Stellar, fTetWild,
libigl or Triangle code was read.

## 1. Result in short

1. **Which inputs miss the floor today.** None of the 21 procedural inputs: every one has every tet >= 10 degrees at the
   default, including wedges with 5 to 60 degree creases, a 10 degree cone, a plate 1/50 as thick as wide and two cubes
   0.01 apart (section 4). The reason is the pipeline, not luck: the tets fill the **alpha wrap**, an offset surface, and an
   offset surface has no acute boundary angle (section 3.2). The wrap already is a chamfer of every acute input feature.
   The only known misses are 2 of the 500 Thingi10K sample meshes (38781 at 9.43, 282598 at 9.53 degrees): local optima of
   the hill climbing on needles and wedges at short wrap edges (the diagnosis of
   [TET_MESHING.md](../design/TET_MESHING.md) 16.1), a sizing effect, not an input angle.
2. **Surface chamfering (SIGGRAPH 2026) does not give a dihedral floor.** It proves termination of Delaunay refinement and a
   bound on the **face** angles of the chamfered model; the paper says itself that dihedral angles can be arbitrarily small,
   and reports an average smallest dihedral angle of 0.13 degrees (4.22 with its optional sliver rule, which has no
   termination proof) before the removed parts are put back, 0.09 (0.69) after. It needs a clean input and a constrained
   Delaunay tetrahedralization, which is the parked P4(c). For this topic it contributes nothing we can use; its value is
   for P4(c), if that is taken up again (section 2.1).
3. **What can be proven.** For a mesh that conforms to the input: nothing better than the smallest input dihedral angle,
   and no algorithm proves even that (section 3.1). For a boundary that only approximates the input: isosurface stuffing,
   for any continuous inside / outside function. Its proven numbers are **not** the abstract's 10.7 degrees without a
   condition: 10.78 to 164.74 degrees holds for every output tet with parameters under which tets may overlap where the
   lattice does not resolve the surface; the parameters that exclude overlap on every input give **9.31** to 161.64 degrees
   (9.77 with an ordered warp). So "a proven floor of 10 degrees on 100 % of inputs" needs either a per-run exact check on
   top of the 10.78 parameters or the owner accepting 9.3 as the proven number (section 2.2).
4. **Measured upper bound** (section 4). The prototype holds the bound on all 169 meshed runs (worst smallest angle 12.5 degrees,
   worst largest 161.5) and on 1.55 million tets from random labels. The gain on the procedural inputs is **0 inputs**
   (21 of 21 reach the floor already); what a lattice mesher adds is the proof and better worst angles (smallest about 20
   instead of 10.0, largest about 150 instead of 165). Its price, uniform lattice against the default
   `MeshTetrahedralization`: at the same tet count (cell = diagonal / 20) the input-to-boundary distance is 10.7 times
   larger; at 9.5 times the tets (cell / 40) still 6.5 times larger; at 69 times the tets (cell / 80) 1.8 times larger. A
   plate thinner than a cell vanishes, and thin parts give a non-manifold boundary (8 of 169 runs; 282598 at every cell
   size tried).
5. **Leaning: stop here (option A).** Plan #1281's own stop rule is met: the uniform lattice needs far more than 10 times
   the tets to match the default's fidelity. Keep the certified per-run statement P5(a) as the claim. Build a lattice mesher
   only on a concrete request for a mathematical bound with a rounded, approximate boundary (option B, then with a graded
   interior); it would be a second mesher beside the wrap pipeline, not a fallback inside it.

## 2. The methods

### 2.1 Surface chamfering (Diazzi, Dai, Panozzo, Attene 2026)

- **Problem.** Delaunay refinement (Ruppert; Shewchuk 1998 in 3D) terminates only if no two input constraints meet at an
  acute angle: edge to edge, face to face (dihedral), edge to face (the projection condition). Real models have such angles.
- **Chamfering.** Around every acute vertex and acute edge a piece of the incident faces is cut away, up to a distance of a
  third of a local feature size (the distance to the vertex's or edge's link), so the faces no longer meet there. The cut
  lines are built from short "bridges" at right angles to the edges so that no new acute angle appears (proof in the
  paper's appendix); a later pass shortens bridges where that is safe. Unlike 2D corner lopping, the cut points are not
  joined by new faces: the hole is simply left open.
- **Refinement.** Standard 3D Delaunay refinement of the chamfered faces inside a box (segments, then faces, then tets with
  a radius-edge ratio above 2, which is a smallest face angle of 14.47 degrees). The tets inside the original input are kept.
- **Conformity.** The refined vertices, the input vertices and the cut-away face parts go into a constrained Delaunay
  tetrahedralization (Diazzi et al. 2023). The result conforms to the input exactly and equals the refined mesh away from
  the acute features; near them the angle bound is lost.
- **Robustness.** New points on edges and faces are implicit points (a new barycentric point type) evaluated by indirect
  predicates; circumcentres are rounded doubles.
- **Guarantees, in the paper's words:** quality guarantees concern face angles only, "but not dihedral angles which can be
  arbitrarily small". Its table 1 (3942 valid Thingi10K meshes): average smallest dihedral angle 0.13 degrees after
  refinement and 0.09 in the final mesh; with the optional sliver rule (a tet is bad if shortest edge / shortest distance of
  opposite edges exceeds 7.2; no termination proof) 4.22 and 0.69. Among its limitations it names that no algorithm,
  its own included, guarantees all output angles >= min(smallest input angle, target).
- **Cost.** About 114 to 125 us per tet, 77 to 82 s and 0.6 to 0.7 million tets on average; 73 % of the models take 1 to 10
  minutes. An input filter drops features below 1e-4 of the diagonal from the refinement and puts them back in the final CDT.
- **Fit.** It needs a closed, manifold, oriented, intersection-free input and a CDT with implicit points: exactly P4(c)
  (#1250), parked. Our library has the pieces P4(c) would use (`ExactPoint3`, the indirect LPI / TPI predicates,
  `ExactMeshArrangement`), but no CDT. For the dihedral floor it gives nothing: it bounds face angles, which our stage 1
  already does away from the fixed surface, and it leaves slivers.
- **The idea we already have.** Chamfering removes acute angles so that refinement terminates and puts the removed parts
  back. Our pipeline removes them once and for all: the wrap is the input's offset surface (section 3.2). That is why
  acute creases do not show up in the measurements of section 4, and also why the boundary does not conform.

### 2.2 Isosurface stuffing (Labelle, Shewchuk 2007)

- **Algorithm.** A body-centred cubic lattice (cube corners and centres; its Delaunay tets are all congruent, dihedral
  angles 60 and 90 degrees). (1) Evaluate the inside / outside function at the lattice points. (2) On every edge with a
  positive and a negative end, find a cut point. (3) A lattice point with a cut point closer than a fraction alpha of the
  edge (one alpha for the axis edges, one for the diagonal edges) is moved onto such a cut point and counts as "on the
  surface". (4) Every lattice tet with a positive vertex is filled from a small stencil (1 to 3 tets), with two rules that
  pick the diagonal of each quadrilateral face. No Delaunay structure, no iteration, no predicate that can fail.
- **The angle theorem** (computer-assisted: interval arithmetic over all positions of the cut points and warped vertices,
  tight to 1e-4 degrees). The bounds depend on the two alphas (the paper's table 1):

  | parameters (axis / diagonal alpha) | dihedral angles | tets cannot overlap on any input ("safe") |
  |---|---|---|
  | 0.28511 / 0.39882 | 10.78 to 164.74 degrees | no |
  | 0.26649 / 0.36918 | 8.97 to 158.74 | no |
  | 0.24999 / 0.41189 | **9.31** to 161.64 | yes |
  | 0.24999 / 0.42978, ordered warp | 9.77 to 163.57 | yes |
  | 0.24999 / 0.40173 | 9.06 to **160.53** | yes |

  The abstract's "10.7 to 164.8" is the first row. "Not safe" means: a lattice tet whose four vertices were all warped can
  turn inside out where the lattice is too coarse for the surface, and then a few output tets may overlap; the bound on each
  tet's angles still holds. Tets with four warped vertices are outside the bounds and are discarded when they are bad.
- **What it assumes.** Only that the function is continuous; the proof covers every position of a cut point on its edge, so
  an approximate cut point (bisection, or linear interpolation) keeps the angle bound. The boundary is within about one
  cell of the zero surface (theorem 2); it is homeomorphic to it only if the surface is smooth and the cell is small
  against its curvature and thickness (theorem 3). Sharp creases and corners are rounded off; the paper names this as the
  method's main shortcoming.
- **Grading.** With an octree, the interior may be coarser while the boundary layer stays uniform, and the bounds of the
  table above still hold (some need a few extra octants). Grading the boundary too leaves only 1.66 to 174.72 degrees.

### 2.3 Prior art checked (about 15 minutes; search terms: guaranteed dihedral angle tetrahedral mesh sharp features, lattice, octree)

- C. Doran, A. Chang, R. Bridson, *Isosurface stuffing improved: acute lattices and feature matching*, SIGGRAPH 2013 talk
  (abstract): the A15 lattice instead of BCC and vertices snapped to feature curves and points; better typical angles,
  features below the lattice spacing are not matched. A heuristic for features, not a bound.
- J. Wang, Z. Yu, *Feature-sensitive tetrahedral mesh generation with guaranteed quality*, CAD 44(5), 2012 (abstract from
  the search result only, not read): an adaptive octree variant with a stated smallest dihedral angle above 5.71 degrees.
  Shows the direction (adaptive boundary) and its price (a weaker bound), as Labelle and Shewchuk's 1.66 degrees does.
- Sliver exudation (Cheng, Dey, Edelsbrunner, Facello, Teng 2000): a proven but unquantified, tiny constant; needs a
  weighted Delaunay triangulation and non-acute input ([TET_MESHING.md](../design/TET_MESHING.md) 3.3, 16.1: at most 25 of
  the 361 bad tets were slivers).
- Diazzi et al. 2026, above: the newest robust conforming mesher states that no algorithm bounds the output angles by the
  input's.
- How the established tools state it: TetGen a radius-edge target without a dihedral bound, CGAL Mesh_3 and fTetWild
  optimisation without a bound ([TET_MESHING.md](../design/TET_MESHING.md) 17.6). Nothing found that proves a dihedral
  floor near 10 degrees together with sharp features or an adaptive boundary.

## 3. What can be proven, and under which preconditions

### 3.1 A boundary that conforms to the input

- **Upper limit.** At an input edge with interior dihedral angle theta, the tets around that edge share theta, so one of
  them has a dihedral angle <= theta: no conforming mesh has a floor above the smallest input dihedral angle. The column
  `input_min_dihedral` of the benchmark rows is that limit (5 to 90 degrees on the hard inputs). Small **face** angles and
  thin parts alone do not force a small dihedral angle (a needle has good dihedral angles), they force small elements.
- **Lower bound.** None is known. Delaunay refinement bounds the radius-edge ratio and face angles, not dihedral angles
  (slivers); with acute input angles even that needs protection or chamfering. Chamfering restores the face-angle bound
  away from the features only.
- So a floor F on a conforming mesh would need at least: every input dihedral angle >= F (necessary, by the upper limit),
  and a method that turns this into a proof, which does not exist. This route is closed for a proof, with or without P4(c).

### 3.2 The wrap boundary (today's pipeline)

- The wrap approximates the outer offset surface of the input at distance `Offset`. Every point of an offset surface has a
  ball of radius `Offset` inside the offset solid touching it (centred on the nearest input point), so the solid has **no
  convex crease or corner at all**: its interior angles are 180 degrees or more, whatever the input's angles are. Measured
  on the wrap's triangulation ([benchmarks/TetMeshBenchmark/README.md](../../benchmarks/TetMeshBenchmark/README.md),
  "Inside cells of the wrap"): smallest surface dihedral 86 to 171 degrees at alpha / 20.
- This is the precondition a chamfering step would establish, and it holds for 100 % of inputs by construction. It explains
  the measurement: an acute crease or a thin part of the input never forces a bad tet here.
- What is still missing for a **proof** is not a property of the input. Stage 1 bounds the radius-edge ratio only away from
  the fixed surface; stage 2 is hill climbing with exact safety checks (it never makes the worst angle smaller, it cannot
  promise to reach 10 degrees). The 2 misses of 500 are such local optima. No theory turns this pipeline into a proven floor;
  what it has is the per-run certificate P5(a) (`Targets.MinDihedralMet`).
- The crease option (`CreaseAngleDegrees`) did not lower the floor on any input here (21 of 21 at >= 10.00), and on 282598
  it happened to lift the worst angle from 9.53 to 10.0004.

### 3.3 A lattice boundary

- Proven for every continuous inside / outside function: 9.31 degrees with the safe parameters; 10.78 degrees per tet with
  the first row, where validity (no overlap) needs either a resolved surface or a check. The check is cheap and exact: if
  every warped lattice tet that touches the output keeps its positive orientation (`Orient3d`), the warped lattice is still
  a triangulation and nothing overlaps. In all 169 runs and the random-label stress no such tet turned over
  (`inverted_background` 0), so "10.78 parameters, checked per run, safe parameters as the fallback" would in practice
  always return 10.78; the proven statement for 100 % of inputs would then be "9.31, and 10.78 whenever the check passes".
- Preconditions and what they cost:
  - **Fidelity is about one cell, features are rounded.** Parts thinner than about a cell are lost (the plate is empty at
    cell / 20 and / 40), creases recede (wedge 5: the tip is 0.37, 0.20, 0.09 of the diagonal short at cell / 20, / 40, / 80).
  - **A manifold boundary is not guaranteed** where the lattice does not resolve the surface (8 of 169 runs, 282598 at
    every cell size among them). Removing or separating tets at a pinch does not change any angle, so the floor survives such a
    repair, but it is a further step with its own rule (which side to keep).
  - **Rounding.** The theorem is about exact positions. Doubles move a vertex by about one ulp of its coordinates; the
    angles move by about that divided by the cell size. With the bound tight to 1e-4 degrees a stated floor of, say, 10.7
    (or 9.3) degrees leaves ample room, but the argument has to be written down with a condition on cell size against
    coordinate magnitude (UTM-sized coordinates in `Similarity.All()`).
  - **The proof must be ours.** The angle theorem is a computer-assisted case analysis of the paper's stencils. The
    prototype derives its stencils from the paper's rules in text; an implementation that claims the bound has to re-run an
    interval verification over its own stencil table, or it only cites a theorem about someone else's code.

## 4. Measurement

Prototype: `Stuffing.cs`, uniform lattice, the paper's steps 1 to 4, unordered warp, tets with four warped vertices
discarded. Cut function: signed distance to the input, sign from the fast winding number, shifted outward by diagonal / 600
(the wrap's default offset, so both boundaries enclose the input). Evaluation as for every other row of this benchmark
(`TetFiles.Evaluate`: exact orientation, closed manifold boundary, exact self-intersection test, vertex-sampled deviation).
Inputs: the 8 procedural inputs of the benchmark and 13 chosen to provoke a miss (wedges of 60, 30, 20, 12 and 5 degrees, a
10 degree cone, a thin plate, two cubes 0.01 apart, the five sharp solids of `SharpFeatureProbe`). One thread except the
field evaluation, Release, net10.0, the 4-core cloud container (shared: times are orders of magnitude).

```
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floor 20 40 80          > results/tet-floor-spike-2026-11-22.csv
G3_STUFF_OURS=0 G3_STUFF_ALPHA=0.24999,0.41189 dotnet run ... -- floor 20 40 80        > results/tet-floor-spike-safe-2026-11-22.csv
G3_STUFF_OURS=0 G3_STUFF_OFFSET=0 dotnet run ... -- floor 40 80                        > results/tet-floor-spike-offset0-2026-11-22.csv
python3 benchmarks/TetMeshBenchmark/compare/summarize_floor_spike.py <the csv files>
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floornoise 20
```

### 4.1 The 21 procedural inputs (10.78 degree parameters)

| | `MeshTetrahedralization`, default | with `CreaseAngleDegrees` 30 | lattice, cell = diagonal / 20 | / 40 | / 80 |
|---|---:|---:|---:|---:|---:|
| meshed / valid (exact) | 21 / 21 | 21 / 21 | 20 / 19 | 20 / 20 | 21 / 21 |
| every tet >= 10 degrees | **21** | **21** | 20 of 20 | 20 of 20 | 21 |
| smallest dihedral: worst / median over inputs | 10.00 / 10.02 | 10.00 / 10.02 | 15.2 / 21.0 | 15.9 / 20.4 | 16.2 / 20.8 |
| largest dihedral, worst | 168.4 | 169.0 | 152.5 | 157.2 | 156.8 |
| tets, median | 6.0k | 6.0k | 9.8k | 75k | 563k |
| input to boundary, max: median / worst (diagonal) | 0.0017 / 0.185 | 0.0017 / 0.185 | 0.024 / 0.375 | 0.014 / 0.200 | 0.0050 / 0.183 |
| per input against the default, median of ratios: tets / input-to-boundary max / time | 1 | | 1.23 / 10.7 / 0.75 | 9.5 / 6.5 / 3.1 | 69 / 1.8 / 15 |
| time, median (s) | 0.22 | 0.28 | 0.19 | 0.81 | 3.3 |

- Not meshed or not valid, lattice: the plate at / 20 and / 40 (thinner than a cell: no positive lattice point, empty
  mesh); the thin torus at / 20 (a boundary edge used twice). With the safe parameters also wedge 5 at / 40 (a non-manifold
  boundary vertex); with offset 0 the two cubes at / 80 (the 0.01 gap is a pinch). No tet is inverted in any run.
- Safe parameters (0.24999 / 0.41189): worst smallest dihedral 12.5 degrees (bound 9.31), worst largest 147.7.
  Offset 0 (the input surface itself): worst 15.0, input-to-boundary max 0.0139 / 0.0076 at / 40 and / 80, about the same
  as with the offset.
- The worst input-to-boundary distance of the default and of the lattice at / 80 (0.18) is "two spheres (soup)", whose inner
  vertices no enclosing boundary reaches; at / 20 and / 40 the lattice's worst is the receded tip of wedge 5.
- The deviation is sampled at the input's vertices, which on the polyhedral inputs are exactly the corners a lattice
  rounds off, so the ratio is the lattice's worst case. On the smooth inputs (spheres, tori) the lattice is as close as the
  default or closer from cell / 40 on (0.0016 to 0.0025 of the diagonal against 0.0016 to 0.0034), and at / 20 except on
  the thin torus and the coarse sphere; there it only costs tets (1.6 to 2.6 times at / 20 for the spheres, 12 to 20 times
  at / 40).
- A graded interior would not close the gap: the boundary layer alone has 1.14 times (cell / 40) and 4.65 times (/ 80) as
  many **boundary triangles** as the default mesh has tets (median), and every boundary triangle is the face of a tet.
- Times of the prototype are not a design value: the cut points use 40 function evaluations each and the stencil code
  allocates; the paper's point that the method is fast is not contested (6 to 29 us per tet here).

### 4.2 Random labels (the bound without a surface)

`floornoise 20`: a cut function that is white noise on a grid finer than the lattice, so signs are independent per lattice
point and cut points fall anywhere on their edges; 20 seeds with four inside densities.

```
alpha_long,alpha_short,seeds,tets,min_dihedral,max_dihedral,inverted_tets,inverted_background,rule_conflicts,quadzero_discarded
0.28511,0.39882,20,773644,14.4636,159.3380,0,0,0,298902
0.24999,0.41189,20,776780,12.5600,151.6538,0,0,0,295760
```

No tet below the proven bound, none inverted, and the two diagonal rules never conflict (the prism of every stencil has a
triangulation). The measured worst angles stay above the bounds because the bound's extreme configurations are specific.

### 4.3 The two sample meshes that miss 10 degrees today

Fetched by hand for this run and not kept (`G3_STUFF_FILE=<id>.stl ... floor 40 80 160`), rows in
`results/tet-floor-spike-thingi2-2026-11-22.csv`.

| | 38781 | 282598 |
|---|---|---|
| default: smallest dihedral / tets / input-to-boundary max | 9.43 / 20.7k / 0.0017 | 9.53 / 18.6k / 0.039 |
| with `CreaseAngleDegrees` 30 | 9.43 (recovery fell back) | **10.0004** / 18.7k |
| lattice, cell / 40 | 18.1 / 13.3k / 0.124, boundary not manifold | 16.6 / 3.0k / 0.244, boundary not manifold |
| lattice, cell / 80 | 17.1 / 104k / 0.0064, valid | 12.6 / 115k / 0.022, boundary not manifold |
| lattice, cell / 160 | 16.3 / 841k / 0.0016, valid | 13.7 / 937k / 0.0099, boundary not manifold |

The lattice reaches the floor on both, as it must. 38781 needs 40 times the tets to match the default's fidelity; 282598
has parts so thin that the lattice boundary is not a manifold at any of the three cell sizes.

## 5. Constraints

| Constraint | Lattice mesher | Chamfering + refinement + CDT |
|---|---|---|
| CPU only, netstandard2.0 + net10.0 | yes: integer lattice logic and doubles | yes in principle; needs a CDT with implicit points (P4(c)) |
| bit-for-bit determinism, serial equals parallel | yes: the field evaluation is the only parallel part and is per point; the warp is sequential in lattice order | serial; as P4(c) |
| 2^k scale invariance bit for bit | yes if the lattice origin and cell come from the bounds by scale-covariant operations | as P4(c) |
| rotation, translation (`Similarity.All()`) | the mesh is **not** invariant (the lattice is axis-aligned): the tests can only check the bound, validity and the counts within a tolerance | invariant up to rounding |
| exact code rules (no FMA, no float, no parallel loops) | not exact code; `Orient3d` only in the per-run check | new exact code (implicit points, predicates): tier L |
| no neural training | none | none |
| licences | paper only (Labelle and Shewchuk 2007); no third-party code needed | paper only; the reference code's licence was not checked |

## 6. Options (ranked by leaning)

| # | Option | Gives | Effort | Risk |
|---|---|---|---|---|
| A | **Stop: keep P5(a) as the statement** (per-run certificate, 498 of 500 on the sample, 21 of 21 procedural). Record the corrected facts (9.31 / 10.78, chamfering has no dihedral bound) in the design note and plan #1281 | no proof; nothing changes | this PR | none |
| B | Opt-in lattice mesher (uniform first, graded interior as its second step), `TetMesh3` output, the 10.78 parameters with the exact per-run check and the safe parameters as fallback, a pinch repair, a status for parts lost below the cell size, the interval verification of our own stencil table | the proven floor (9.31, and 10.78 when the check passes) on every input, largest angle below 165, with a rounded boundary about one cell from the input and 10 to 70 times the tets of the default at comparable fidelity | uniform 1.5 to 2 weeks; + 1 week for the verification program and the rounding argument; + 1.5 to 2 weeks for the graded interior (large model: the bound is the product) | medium: the method is simple and the prototype works; the risks are the proof we must own, the manifold repair, and that the result is too coarse or too large to be wanted |
| C | The lattice as an automatic fallback inside `MeshTetrahedralization` when the target is missed (2 of 500) | a mesh at >= 10 degrees also for those runs | B plus the switch | high for little: the fallback's boundary is a different, coarser surface than the caller asked for (38781 needs 40 times the tets, 282598 has no manifold lattice boundary at three sizes), and it is an output change (owner) |
| D | Chamfering as in Diazzi et al. 2026 | termination and a face-angle bound for an input-conforming mesher; **no dihedral floor** | part of P4(c) (3 to 4 months, parked) | high; out of scope by the owner's decision |
| E | Sliver exudation or perturbation on the current pipeline | a proven but tiny, unquantified constant, or none | 2 to 3 weeks (weighted `Delaunay3`, power predicate) | high for no usable number; the diagnosis found at most 25 slivers among 361 bad tets |

How others solve it: section 2.3. Not an option of this spike but noted: a further improver lever for the 2 meshes left
is empirical work on the certified claim, not a proof; 282598's reaction to the crease pass (10.0004) shows these last
cases are sensitive to any change of the surface triangulation.

**Leaning: A.** The trigger of plan #1281 (a caller who needs a mathematical bound and accepts an approximate boundary) has
not occurred, the measured gain on our inputs is zero inputs, and the price is the fidelity that stages P1 to P4 were built
for. If the trigger occurs: B, scoped as a separate mesher, with the stop rule "graded interior, cell chosen for the
caller's fidelity, at most 10 times the default's tets"; C and E not at all; D only as part of a P4(c) decision.

## 7. Open points for a later implementation (only if B gets a go)

- The interval verification of our own stencil table, including tets with three warped vertices and the discard rule for
  four; the rounding condition (cell size against coordinate magnitude).
- The cut function for open and dirty inputs: the prototype's sign comes from the winding number at 0.5, which is not
  continuous in distance terms on open soups; the distance to the wrap (closed, and 1-Lipschitz) is the clean choice and
  costs the wrap.
- Lattice traversal from seed points instead of the whole box, and the field in batches (`MeshBatchQueries`).
- The pinch repair rule and the status for lost thin parts (a volume or component comparison against the winding number).
- Building blocks found in the library: `ExactPredicates.Orient3d`, `TetMesh3` and the writers, `TetMeshQuality`
  (`CheckTargets` for the per-run statement), `DMeshAABBTree3.FastWindingNumber`, `MeshBatchQueries`, `WindingNumberOctree`
  (an octree for the graded variant would be new: its octants need the paper's partial refinement). No lattice or
  marching-tetrahedra code exists.
