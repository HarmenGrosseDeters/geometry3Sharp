# Tetrahedral meshing: "any input -> valid tet mesh" (design, no library code)

**Status:** design for the owner's decision (Phase 5 priority 3, owner 2026-11-08 / 2026-11-09), tier L. Nothing is built.
Licence rules: TetGen (AGPL), fTetWild / TetWild (MPL), libigl (MPL), the CDT of Diazzi et al. (GPL / LGPL) were used as
**papers only**; no CGAL, Triangle or Triangle.NET code was read. Numbers marked *planning* are estimates, not
measurements (no .NET SDK was installed in this session, so nothing was run); stage 0 below measures them first.

## 1. Result in short

- **Recommended: option (a)**, "alpha wrap -> Delaunay refinement of its interior", built in three stages. Its decisive
  property: the wrap's result is a union of facets of its own `Delaunay3`, so **the inside cells of the wrap already are a
  valid, boundary-conforming tet mesh** (exactly positive volumes). Boundary recovery, the hard part of every
  constrained approach, does not exist here. What is missing is quality (radius-edge, slivers) and interior size
  control, which is classical Delaunay refinement.
- **Stage 0 (about 2-3 days)** gives "any input -> valid tet mesh" without any quality claim, and measures what stage 1 has to
  fix. **MVP = stages 0 + 1 (about 3 weeks, large model)**: valid + radius-edge bound + size control + surface
  preserved exactly (up to a verified rounding), no sliver removal. Stage 2 (sliver removal) is a further 1-2 weeks.
- The price of (a): the boundary of the tet mesh is the **wrap surface**, not the input surface (distance about
  `Offset` .. `Offset + Alpha`, features narrower than 2 Alpha are rounded off). For "the tet mesh must reproduce the
  input surface exactly" only (b) delivers, at several times the effort and risk (section 4).
- Owner decisions needed: section 8.

## 2. Building blocks in the library

| Block | What it gives | What is missing for tets |
|---|---|---|
| `Delaunay3` (`comp_geom/Delaunay3.cs`, 423 lines) | Bowyer-Watson insertion with exact `Orient3d` / `InSphere`, cells positively oriented exactly, adjacency, `Locate` (walk), `Circumcenter`, `CellVolume`, `Validate(checkDelaunay)`, insertion reports removed / added cells, `minSeparation` | no vertex removal, no undo of an insertion, no per-cell labels, no weights (regular triangulation), no constraint recovery, points must lie strictly inside the initial box |
| `MeshAlphaWrap` (`mesh_ops/MeshAlphaWrap.cs`) | closed, oriented, 2-manifold, intersection-free surface for any input; its vertices are triangulation vertices (no rounding); cell labels inside / outside (`label`, private); `internal Delaunay3 Triangulation`; vertex separation >= `min(Offset, Alpha - Offset)` (class doc, same argument as Chew / Ruppert); triangle min angle >= `asin(Offset / (2 Alpha))` | an accessor for the inside cells; the interior has no quality (its cells are as large as the surface sampling allows, with slivers and flat cells) |
| Exact predicates (`ExactPredicates.Orient3d` / `InSphere`, `AdaptivePredicates`, `tools/PredicateCompiler`) | scale-invariant (2^k bit for bit) signs | a power (weighted in-sphere) predicate only if sliver exudation is chosen (section 3.3) |
| Implicit points (`ExactPoint3`, `ExactMeshArrangement`, `Orient3dLPI` / `TPI`) | exact Steiner points as rationals; the base of Diazzi et al. style recovery | not needed for (a); needed for (b) |
| Winding numbers, `MeshBatchQueries` (`IsInsideBatch`, `IntersectBatch`, `NearestPointBatch`) | inside / outside of any input, batched 3-5x on 4 cores | tet labelling for (b) / (c) / (d); not needed for (a) |
| `MeshRobustRepair`, `PolyhedralEnvelope` / `MeshEnvelope` | cleaned input; exact envelope test (prototype) | the envelope test is about 100x too slow for (c): 7109 us against 62 us per triangle in `ReducerBenchmark` (research.md Phase 5 #2) |
| `QualityMesh2` (2D Ruppert / Shewchuk with off-centres, concentric shells, snap) | the algorithm skeleton, encroachment rules, queue discipline, oracle / golden / fuzz infrastructure, provenance process | everything 3D; its small-angle handling is a geometric approximation with measured, not proven, termination |
| `TriangleQualityStatistics` | min / max angle, aspect, scaled Jacobian for triangles (Shewchuk 2002 measures) | no tet measures at all (radius-edge, dihedral, volume-to-edge ratio, AMIPS): new, small (about 150 lines) |

Search for tets in the library: nothing beyond `Delaunay3`, `MeshAlphaWrap` and the `VolumeSampleCells` voxel cells.

## 3. Literature: what each method guarantees and costs

How others solve it (prior art, section 2 of AGENT_WORKFLOW): TetGen (constrained Delaunay + refinement, Si), CGAL Mesh_3
(restricted Delaunay on an implicit domain + exudation / perturbation; papers only), fTetWild (envelope, local
operations), isosurface stuffing (lattice), Stellar (topological and smoothing improvement). All were read as papers.

### 3.1 Constrained and conforming Delaunay, boundary recovery

- **Existence.** A constrained Delaunay tetrahedralization of a PLC need not exist without Steiner points (Schönhardt
  polyhedron). Shewchuk, *A condition guaranteeing the existence of higher-dimensional constrained Delaunay
  triangulations*, SoCG 1998, gives a sufficient condition (every input facet is "strongly Delaunay"-recoverable);
  Shewchuk, *Updating and constructing constrained Delaunay and constrained regular triangulations by flips*, SoCG 2003,
  gives flip-based construction when it exists.
- **Recovery with Steiner points.** Si and Gärtner, *Meshing piecewise linear complexes by constrained Delaunay
  tetrahedralizations*, IMR 2005, and Si, *TetGen*, ACM TOMS 41(2), 2015: segments split first (Steiner points
  on input edges), then facets by cavity re-triangulation; terminates in exact arithmetic under their conditions, with
  a gap in the theory that Diazzi, Panozzo, Vaxman, Attene, *Constrained Delaunay Tetrahedrization: A Robust and
  Practical Approach*, ACM TOG 2023, close: implicit Steiner points (rational combinations of input points, so
  never irrational), parameter free, conforming without approximation, 100 % of the 4408 valid Thingi10K models, "empirically
  manageable" Steiner counts. This is the closest match to our exact / implicit infrastructure (and the licence trap:
  paper only).
- **Cost.** Delaunay of n points about 1-2 us per point for good implementations (planning); recovery is the
  unpredictable part (cavity sizes), and it needs exact predicates on implicit points (our `Orient3dLPI` / `TPI` take
  47-84 ns filtered, 1-2 us exact).

### 3.2 Delaunay refinement of tetrahedra

- Shewchuk, *Tetrahedral mesh generation by Delaunay refinement*, SoCG 1998: circumcentre insertion for tets with radius-edge
  ratio above ρ, encroached boundary segments and facets split first. **Guarantee:** terminates with all
  radius-edge ratios <= ρ (ρ >= 2) **if all input angles are >= 90 degrees** (facets meeting at acute dihedral angles
  or small segment angles can make it loop). Radius-edge does not exclude **slivers** (dihedral angles near 0).
  Also Cheng, Dey, Shewchuk, *Delaunay Mesh Generation*, CRC 2012 (chapters 7-8 for 3D and for small angles).
- Small angles: Cheng and Poon, *Three-dimensional Delaunay mesh generation*, DCG 2006 (protecting balls); Pav and
  Walkington 2004 / 2005. Cost of the guarantee: complexity and weighted Delaunay.
- Smooth surfaces: Boissonnat and Oudot 2005, Oudot, Rineau, Yvinec 2005 (restricted Delaunay: topology and
  approximation, not exact surface; the CGAL Mesh_3 route).
- Size: Si 2015 extends this with size fields and the constrained setting; vertices are about n, tets about 6 n.

### 3.3 Slivers

- **Exudation** (Cheng, Dey, Edelsbrunner, Facello, Teng, *Sliver exudation*, J. ACM 2000): weights chosen so that
  slivers leave the *weighted* Delaunay triangulation; gives a provable (very small) dihedral bound, in practice a
  few degrees up to about ten (Edelsbrunner and Guoy's experiments; value to verify). Needs a regular triangulation:
  a power-test predicate and a weighted `Delaunay3`, and the boundary must stay protected (weights zero on the boundary).
- **Perturbation and ODT / Lloyd** (Li and Teng 2001; Tournois, Srinivasan, Alliez, *Perturbing slivers in 3D Delaunay
  meshes*, IMR 2009; Tournois, Wormser, Alliez, Desbrun, *Interleaving Delaunay refinement and optimization*, SIGGRAPH
  2009): keeps the Delaunay triangulation, moves vertices; best practical dihedral angles of the Delaunay family, no
  hard guarantee; the boundary must be excluded or constrained.
- **Topological and smoothing improvement** (Klingner and Shewchuk, *Aggressive tetrahedral mesh improvement*, IMR 2007;
  Shewchuk, *Two discrete optimization algorithms for the topological improvement of tetrahedral meshes*): 2-3, 3-2,
  4-4 flips, edge removal, vertex smoothing by non-smooth optimization. Leaves the Delaunay property, so the
  boundary stays conforming only because boundary facets are never flipped; needs `Orient3d` only. Best measured
  quality of the classical methods; no guarantee.

### 3.4 Envelope, lattice

- **TetWild / fTetWild** (Hu et al., SIGGRAPH 2018; Hu, Schneider, Wang, Zorin, Panozzo, TOG 39(4) 2020): any input
  (soups, self-intersections), the output boundary lies inside an ε-envelope of the input (default tied to the bounding box),
  not conforming; BSP / Delaunay start, then local operations (split, collapse, swap, smoothing) on the AMIPS energy with
  an envelope check at every move. The preprint states success on 98.7 % of the tested models in under 2 minutes and
  that all input triangles are always inserted; the dihedral and AMIPS tables were not checked here (to read in the
  paper at stage 1 of any envelope work). Cost driver: the envelope test per operation, which is exactly what our
  prototype is too slow for.
- **Isosurface stuffing** (Labelle and Shewchuk, SIGGRAPH 2007): a body-centred-cubic lattice cut by the surface, vertices warped
  to the surface, guaranteed dihedral angles in about [10.7, 164.8] degrees (value from the paper's abstract; conditions on the
  surface and lattice resolution to be read), fast, simple, no Delaunay; the boundary only approximates the surface and the
  size is uniform (graded versions use octrees).

## 4. Options for "any input -> valid tet mesh"

Time per tet: *planning* figures, upper bounds to confirm in stage 0. Delaunay insertion at 1-2 us per point and about
6.5 tets per vertex gives 0.2-0.4 us per tet for a plain triangulation; refinement (circumcentre, encroachment checks, queue) is
5-10 times that: **2-5 us per output tet, so 1M tets in 2-5 s plus sliver work (a factor 2-5) = 10-25 s per 1M tets** single
threaded. The wrap itself is the larger cost today (148 s for 17 big meshes in the repair benchmark, minutes for the
largest).

| | (a) wrap + interior refinement | (b) CDT of a cleaned input + recovery | (c) envelope (fTetWild-like) | (d) lattice stuffing from winding numbers |
|---|---|---|---|---|
| Input | any (wrap) | clean closed (after `MeshRobustRepair`, whose fallback is the wrap, see `benchmarks/MeshRepairBenchmark`) | any | any (winding number) |
| Valid output | yes: exact positive volumes, conforming by construction | yes if recovery terminates (Diazzi: 100 % on valid models) | yes (reported 98.7 %, time-limited) | yes by construction |
| Boundary | wrap surface (offset, rounded features), preserved exactly except in-plane splits | the input, exactly | within ε of the input | within about h of the surface |
| Quality | radius-edge <= ρ outside exempt zones (provable, floor argument below); slivers need stage 2 | same, with more Steiner points; same sliver problem | best practical (optimisation on every move) | dihedral bound guaranteed (conditions to read) |
| Risk | small dihedral angles at the wrap surface (section 5.2); rounding of Steiner points | recovery theory, implicit points in a Delaunay kernel (large), cavity blow-ups | envelope speed (100x), parameters, long tail | uniform size, many tets for thin features, fidelity |
| Effort | stage 0 + 1 about 3 weeks, stage 2 1-2 weeks | 3-4 months (kernel with implicit points, recovery, then the same refinement) | 3+ months, plus a fast envelope (Phase 5 #2, open) | 1-2 weeks, M |
| Reuses | `Delaunay3`, wrap, predicates, `QualityMesh2` ideas and test infrastructure | `ExactMeshArrangement`, implicit points, `Delaunay3`, repair | envelope prototype, exact booleans, repair | winding numbers, `MeshBatchQueries`, `MeshWindingNumberGrid` |

Option (b) is the only one that preserves the input exactly; (c) the only one with the best practical element quality
on arbitrary input; (d) the cheapest way to a quality *guarantee*. For a first release, (a) has by far the best ratio
because it removes the hardest sub-problem by construction, and its surface (the wrap) is already what the library
calls "valid output for any input". A later (b) can reuse the stage 1 refinement unchanged (the refinement only
needs a conforming Delaunay start); (d) can be added as an independent, small alternative for implicit / SDF
domains.

## 5. Design of the recommendation (a)

### 5.1 Pipeline

1. `MeshAlphaWrap.Compute()` (unchanged). Expose, internally first, the final cell labels and `Delaunay3`
   (`Triangulation` exists). Inside cells = tets (cells touching box corners cannot be inside when the wrap is a
   Success; status `PointLimitReached` is passed on as a non-success).
2. Extract: vertices = triangulation vertices used by inside cells, tets = inside cells (positively oriented already),
   boundary = facets with an outside neighbour = exactly the wrap's triangles. **Stage 0 deliverable**, checked by an
   independent validity checker (section 7).
3. Refine (stage 1), inside only, outside cells stay in the triangulation as a mantle so the Delaunay kernel
   stays a convex-hull Delaunay (their circumspheres must not be allowed to swallow boundary facets, see 5.2).
4. Improve (stage 2): flips + smoothing (3.3 third bullet), boundary vertices fixed.

### 5.2 Refinement rules and the termination argument

Shewchuk 1998 with these changes:

- **Bad tet**: radius-edge ratio > ρ (default 2) or circumradius > local size. Size: `TargetEdgeLength` (default
  the median wrap edge length) with grading from the boundary (`SizeFunction`, as `QualityMesh2.SizeFunction`).
- **Insert** the circumcentre p of the worst bad tet. If p lies in the diametral ball of a boundary facet f (it
  encroaches f), insert the circumcentre of f instead (the usual facet split), and likewise for the diametral
  balls of boundary edges. Facets are only split *in their plane*, so the surface is unchanged as a point set.
- **Rounding.** The circumcentre of a facet is exactly in its plane but not a double; the rounded point is off the
  plane by about 1e-16 of the scale. Rule: insert the rounded point with `Delaunay3.Insert`, then check exactly
  (`Orient3d`, `InSphere` on the cavity boundary) that all boundary facets in the cavity are still Delaunay facets and
  all new tets have positive volume; if not, **undo** (needs the cavity snapshot, a new `Delaunay3` feature) and mark
  the facet "unsplittable" (counted, reported). Fidelity to the wrap: Hausdorff <= 2^-40 of the diagonal, asserted by a
  test. An exact alternative (implicit points inside `Delaunay3`) is rejected: it is the (b) kernel.
- **Termination (the floor argument).** The wrap gives a natural length scale: every wrap vertex is at least
  `s = min(Offset, Alpha - Offset)` from the others (the insertion radius of the paper's Lemma 2, the same
  separation argument as Chew 1993 / Ruppert 1995). Refinement never splits an edge or facet whose circumradius is
  below `MinFeature = s * c` (c about 1, parameter) and never accepts a Steiner point closer than `MinFeature` to a
  vertex (`Delaunay3.Insert(minSeparation)` already exists). Vertices are then packed with a minimum distance in a
  bounded domain, so the number of insertions is finite: **termination is guaranteed**, at the price that the radius-edge
  bound is guaranteed only for tets not touching a "floor" decision. These are counted (`UnrefinedBadTets`) and
  reported, not hidden. Shewchuk's 90-degree condition is *not* assumed; it is replaced by the floor, and the claim is
  measured, not proven, on acute wrap dihedrals (compare `MESHING2D_SMALL_ANGLES.md`, where the 2D analogue is
  measured, not proven either).
- Open risk: acute dihedral angles of the wrap surface (the manifold step produces triangles with angles up to 179
  degrees; `RefineNonManifold` removes most). Stage 0 measures the dihedral distribution of the wrap first; if
  the tail is long, a protection step (weighted vertices, Cheng-Poon) is out of scope for the MVP and the result
  reports the floor counts.

### 5.3 What `Delaunay3` needs (additive, it is not upstream API)

(1) per-cell label array with its own bookkeeping on insertion; (2) `TryInsert` with undo (restore removed cells from the
`removed` / `added` lists plus a saved copy of the removed cells' vertices and neighbours); (3) an `Insert` that
accepts points on the box boundary is *not* needed; (4) a facet-is-Delaunay query `IsFacet(a, b, c)` for the check
above. Vertex removal is not needed for MVP (ODT smoothing would want it).

## 6. Measurable targets

| # | Target | Measure | Value (to calibrate in stage 0) |
|---|---|---|---|
| T1 | Validity | `NotValidReason(tets)` empty: all tets exactly positive, every interior facet shared by two tets, boundary facets = a closed 2-manifold, volume of tets = volume of the boundary surface | 100 % of the procedural corpus, exact |
| T2 | Boundary fidelity | tet boundary equals the wrap surface as a point set (Hausdorff, `MeshDeviation`); to the input: <= `UsedOffset + UsedAlpha` plus the wrap's own guarantee | <= 2^-40 of the diagonal to the wrap; input as the wrap |
| T3 | Radius-edge | max over tets, excluding the counted floor tets | <= ρ = 2, `UnrefinedBadTets` = 0 on smooth procedural inputs |
| T4 | Dihedral angles | min / max dihedral (stage 2 for the tail) | stage 1: reported only; stage 2: min >= 10 degrees on sphere / torus / ellipsoid wraps, >= 5 degrees on the mechanical set |
| T5 | Speed (excluding the wrap) | refinement + improvement, single thread | <= 25 us per output tet; 100k tets <= 3 s |
| T6 | Determinism | bit-identical result under `Similarity.All()` (2^k scales), repeated runs, serial = parallel if a parallel path exists | exact equality of vertex and tet lists up to the transform |
| T7 | Size control | element count follows `TargetEdgeLength` | count within a factor 2 of volume / (0.12 h^3) on a cube |

## 7. Test strategy

- **Independent validity oracle** (`NotValidReason` plus a test-side checker that shares no code with `Delaunay3`):
  sorted-triple face dictionary (each interior facet exactly twice, boundary facets once, with opposite orientation),
  `BigInteger` orientation for every tet, the boundary vs the wrap's `DMesh3` triangles, Euler characteristic, volume by
  the divergence theorem against the sum of tet volumes in exact rationals. This is the "check the result, not only the status"
  rule of CLAUDE.md.
- **Procedural inputs** (no mesh files): cube, icosphere (several levels), torus, two overlapping spheres, a box with a
  cylindrical hole, a thin plate (thickness near `2 Alpha`), and defect variants through the existing QA generators
  (open sphere, soup, duplicate triangles, self-intersecting union) which exercise the any-input path.
- `Similarity.All()` invariance over every Compute, reuse test (Compute twice on one instance), determinism (repeated
  runs, parallel = serial if a parallel path exists), Delaunay property on stage 1 output (`Validate(true)` of the
  triangulation before the improvement stage), a mutation set (drop encroachment, skip the post-insert facet check, drop the
  floor, wrong orientation) killed by the tests; `Delaunay3` additions get their own unit and fuzz tests.
- All tests <= 5 s: meshes of 10^3 to 10^4 tets; a larger soak and the 100k-tet speed target go to a benchmark
  (`benchmarks/TetMeshBenchmark`, before / after numbers in its README), not to the unit tests.
- A writer for TetGen-style `.node` / `.ele` or VTK would be output only; a *reader* would need `ReaderFuzzTests`.

## 8. API sketch (additive)

```csharp
public sealed class TetMesh3 {                       // new, small, immutable-ish
    public List<Vector3d> Vertices;
    public List<Index4i> Tets;                       // positively oriented (Orient3d > 0)
    public List<Index3i> BoundaryTriangles;          // outward oriented, = the surface
    public DMesh3 ToSurfaceMesh();
}
public static class TetMeshQuality {                 // new
    public static string NotValidReason(TetMesh3 m); // null = valid
    public static TetQualityStatistics Measure(TetMesh3 m);   // radius-edge, dihedral, volume/edge^3
}
public class MeshTetrahedralization {                // new, mirrors MeshAlphaWrap
    public MeshTetrahedralization(DMesh3 mesh);  public MeshTetrahedralization(IEnumerable<Vector3d> points);
    public double RelativeAlpha, RelativeOffset;     // passed to the wrap
    public double MaxRadiusEdgeRatio = 2;            // rho
    public double TargetEdgeLength;                  // 0 = auto
    public Func<Vector3d, double> SizeFunction;
    public double MinFeatureFactor = 1;              // the floor
    public int MaxVertices;  public Func<bool> CancelF;
    public bool Compute();  public TetMesh3 Result;
    public TetStatus Status;                         // Success, WrapFailed, PointLimitReached, Cancelled
    public Dictionary<string, long> Counts, PhaseMilliseconds;   // incl. UnrefinedBadTets, unsplittable_facets
}
```

No change to existing public types except additive members of `Delaunay3` (section 5.3) and an internal accessor in
`MeshAlphaWrap`; `dotnet pack` API validation stays green.

## 9. Plan, estimate, owner decisions

| Stage | Content | Effort (large model, one PR each) |
|---|---|---|
| 0 | inside-cell extraction, `TetMesh3`, `NotValidReason`, quality measures, benchmark: Delaunay3 insert rate and tet quality / dihedral distribution of wraps on the procedural set | 2-3 days |
| 1 (MVP with 0) | refinement with encroachment, floor, rounding with undo, size control, `Delaunay3` additions | about 2 weeks |
| 2 | flips + smoothing sliver removal; T4 | 1-2 weeks |
| later | (d) lattice stuffing for implicit domains (1-2 weeks); (b) conforming CDT of clean inputs (months) | not planned |

**MVP estimate: about 3 weeks (stage 0 + 1), 4-5 weeks with sliver removal.** Sonnet takes the bounded parts (quality
measures, benchmark, writers, oracle); the termination / rounding argument and the reviews stay on the large model.

Owner decisions (2026-11-09, relayed by the coordinator):
1. **Option (a)**, alpha wrap + Delaunay refinement inside; the wrap surface is the boundary (documented distance
   `Offset` .. `Offset + Alpha` to the input).
2. **Stage 0 starts now** as a probe; its measurements replace the planning numbers of this note.
3. Sliver strategy: topological flips + smoothing (planning decision, to be confirmed with the stage-0 numbers).
4. Quality targets T3-T5 as proposed, minimum dihedral 10 degrees (planning, to be confirmed with stage 0).
5. New types `TetMesh3` / `TetMeshQuality`, `Delaunay3` only additive (planning, to be confirmed with stage 0).
6. **(d) lattice stuffing later** as an alternative for implicit domains.

## 10. Stage 0 result (2026-11-09)

Built: `MeshAlphaWrap.ExtractTetMesh()`, `TetMesh3`, `TetMeshQuality` (`NotValidReason`, `Measure`), the independent
BigInteger oracle `tests/.../Harness/TetOracle.cs`, `TetMeshingStage0Tests` (18 tests, under 1 s in total: five inputs
valid + conforming to the wrap, reuse, `Similarity.All()` with bit-for-bit 2^k scales, measures of known tets, eight
mutants of a valid cube mesh rejected) and `benchmarks/TetMeshBenchmark` (raw numbers in its README). Mutation
checks: dropping a boundary facet, accepting zero volume (first survived, test added), a wrong dihedral formula are
killed; a volume-identity check was an equivalent mutant (it follows from the facet matching) and was removed.

Measured, replacing the planning numbers:

| Quantity | Measured | Planning figure of section 4 |
|---|---|---|
| `Delaunay3` insertion, spatially coherent order | **13 us per point, 1.9 us per cell**, constant to 400k points | 1-2 us per point |
| `Delaunay3` insertion, random order | 26 us (20k) .. 68 us (400k) per point: point location dominates | not considered |
| Validity of the inside cells | 8 of 8 procedural wraps valid, exactly | expected |
| Interior of the wrap | only 1.4k .. 5k tets per wrap (all vertices lie on the surface) | expected: coarse |
| Tets with radius-edge above 2 | **87-99 %** of the tets; median 1.6 .. 8.8; worst 4.8 .. 18 | quality missing |
| Smallest dihedral angle per mesh | 4e-11 .. 0.46 degrees; 5 % percentile 1.0 .. 9.6 degrees; median 4 .. 31 degrees | slivers expected |
| Tets with a dihedral above 170 degrees | 0.8 % (box) .. 58 % (sphere n=40) | |
| Surface dihedral angles (alpha 20, offset 600) | minimum 86.4 .. 170.7 degrees; only 3 of 20k edges below 90 | risk of acute angles (5.2) |
| Surface triangle min angle | 8.6 .. 30 degrees | wrap bound 0.95 degrees |

Consequences for the stage-1 decisions:

1. **Raw inside cells are not usable as an FEM mesh**: almost every tet violates the radius-edge bound and the dihedral
   tails reach 0 and 180 degrees. Stage 1 is needed, as planned.
2. **The termination risk of 5.2 is smaller than feared on this set**: the wrap surfaces have no acute dihedral angles
   to speak of (86 degrees at the worst) and triangle angles of at least 8.6 degrees, so Shewchuk's 90-degree condition holds to
   within 4 degrees. The floor argument stays as the safety net. This set is smooth or convex; sharp
   concave inputs (a thin slot, a notch) are not covered and should be added to the corpus before stage 1 is closed.
3. **The speed target T5 (25 us per output tet) is tight**: a plain triangulation costs 1.9 us per cell, and refinement
   adds circumcentre computation, encroachment checks and a queue. A realistic expectation is 5-10x the plain
   insertion, 10-20 us per tet, before sliver removal. The planning figure of 2-5 us per tet is withdrawn. Decision
   for the owner: keep T5 at 25 us per tet for refinement alone and measure it in stage 1, or relax it to 50 us.
4. The point-location walk matters: stage 1 should insert in a spatially coherent order (the queue by position, or a
   Hilbert order for the size-driven phase), and pass hints.
5. **Not measured**: Thingi10K (hosts blocked here; the `file` mode of the benchmark takes any mesh), the wrap time share
   (the wrap takes 40-240 ms for these inputs and dominates tiny meshes, not large ones).
