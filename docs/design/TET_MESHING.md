# Tetrahedral meshing: "any input -> valid tet mesh" (design, no library code)

**Status:** design for the owner's decision (Phase 5 priority 3, owner 2026-11-08 / 2026-11-09), tier L. Written before any build; stage 0 is built since (#487, hand-over #500).
Stage 1 prerequisites (owner go 2026-11-13): the fTetWild black-box comparison (section 12), the sharp-feature check of #736
(section 13) and the stage-1 plan with its owner decisions (section 14). Stages 1 and 2 are built since (sections 14.8-16);
the production-quality gap study and plan (owner 2026-11-17) is section 17; stage P1 (default alpha diagonal / 40) is 17.8; stage P2 (output and API) with P5(a) is 17.9.
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

- **Bad tet**: radius-edge ratio > ρ (default 2) or circumradius > local size. Size: `TargetEdgeLength` (built as `MaxCircumradius`, a bound on the circumradius; default
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
| T4 | Dihedral angles | min / max dihedral (stage 2 for the tail) | stage 1: reported only (with the round carving of #774 the box wrap gives 9.28 degrees after peeling, before 10.0; the stage 1 test pins >= 9.0); stage 2: min >= 10 degrees on sphere / torus / ellipsoid wraps, >= 5 degrees on the mechanical set |
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
| Tets with radius-edge above 2 | **28 % (box) .. 99 %** of the tets (88 % sphere n=6, 69 % cylinder, 79 % torus); median 1.6 .. 8.8; worst 4.8 .. 18 | quality missing |
| Smallest dihedral angle per mesh | 3e-14 .. 0.46 degrees; 5 % percentile 1.0 .. 9.6 degrees; median 4 .. 31 degrees | slivers expected |
| Tets with a dihedral above 170 degrees | 0.6 % (box) .. 58 % (sphere n=40) | |
| Surface dihedral angles (alpha 20, offset 600) | minimum 86.4 .. 170.7 degrees; only 3 of 20k edges below 90 | risk of acute angles (5.2) |
| Surface triangle min angle | 8.6 .. 30 degrees | wrap bound 0.95 degrees |

Consequences for the stage-1 decisions:

1. **Raw inside cells are not usable as an FEM mesh**: most tets violate the radius-edge bound (28-99 %) and the dihedral
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
5. The wrap time share: the wrap takes 40-240 ms for the procedural inputs and dominates the extraction (2 ms) by far.
   Thingi10K: see 10.1 (measured later the same day; this item had said it was blocked).

### 10.1 Thingi10K subset and fTetWild's published numbers

**Ours** (stage 0, raw inside cells, wrap at relative alpha 20 / offset 600, one thread, cloud container): the subset is
every 250th id of the dataset's `metadata/file_ids.txt` (40 meshes: ids 100026 .. 97168; downloaded from the Hugging Face
mirror `Thingi10K/Thingi10K`, `raw_meshes/*.stl`, not committed). Per-mesh rows (numbers only):
`benchmarks/TetMeshBenchmark/results/thingi10k-subset-2026-11-09.csv`.

| Measure (40 meshes, 3 .. 2.5M input triangles) | Result |
|---|---|
| Wrap succeeded, tet mesh exactly valid (`NotValidReason` null) | **40 of 40** (no failures, no timeouts; the largest, 120628 with 2.53M triangles, took 13.4 s) |
| Wrap time | median 0.33 s, maximum 1.09 s (13.4 s for the 2.5M-triangle mesh) |
| Output size | median 3.4k tets, maximum 13.2k (raw inside cells are coarse) |
| Minimum dihedral angle per mesh | 0 of 40 meshes reach 10 degrees (median of the per-mesh minimum 0.22 degrees; median of the 5 % percentile 6.7 degrees); 3.0 % of the tets are below 5 degrees, 9.3 % below 10 (medians over meshes) |
| Radius-edge above 2 | 5 % .. 59 % of the tets per mesh (median 25 %); median radius-edge 1.5 |

Surface of the wraps (the boundary a refinement has to respect): the interior dihedral angle is below 90 degrees at 1129 of 113889
edges (1.0 %), below 45 degrees at 79, below 20 degrees at 5; the smallest is 12.0 degrees; the smallest surface triangle angle is 1.29 degrees
(median of the per-mesh minimum 3.8 degrees). **This revises consequence 2 above**: on real meshes the wrap surfaces do have acute dihedral
angles, so Shewchuk's 90-degree condition does not hold and the floor argument of 5.2 (plus a per-mesh report of
`UnrefinedBadTets`) is not a safety net but the working mechanism; the sharp-feature gap is known (coordinator: handled
with Alpha Wrap 3.0, not in stage 0).

**fTetWild's published numbers** (Hu, Schneider, Wang, Zorin, Panozzo, ACM TOG 39(4) 2020, arXiv 1908.03581 read from the
arXiv PDF, section 4, Table 2; paper only). Settings: envelope epsilon = 1e-3 d, target edge length d/20 (d = bounding
box diagonal), stop at max AMIPS energy below 10 or 80 optimisation iterations; Xeon E5-2690v4 2.6 GHz, up to 8 threads, 3 h and 32 GB per model.

| Quantity | fTetWild |
|---|---|
| Success on Thingi10K (10 000 meshes) | 99.97 % in Table 2 (3 models over 3 h, 2 out of memory); the text of section 4.1 says 100 %, and 100 % with 11 h; TetWild 99.89 %, CGAL 79 %, TetGen 49.5 % |
| Average time over all models | 49.8 s (8 cores), 80.4 s serial; TetWild 360 s |
| Reduced set (4540 models all methods handle) | 18.5 s; TetGen 22 s, CGAL 95 s, TetWild 107 s |
| Tail | 98.7 % within 2 minutes; only 4 models above 16 minutes (TetGen 20, CGAL 122, TetWild 25); all within 32 minutes in the preprint abstract |
| Quality | stopping criterion max AMIPS energy below 10; minimum dihedral, radius-to-edge, aspect and volume-to-edge ratios are shown only as histograms (Fig. 17) over the dataset, **no table**: whether the output reaches 10 degrees is not stated numerically in the paper |
| Surface | within the envelope (epsilon = 1e-3 d), not conforming |

Comparison with the owner's targets (100 % valid, min dihedral >= 10 degrees, same order of time): validity 40 of 40 is on
target for the subset (fTetWild reports 99.97 %); the dihedral target is **not met at stage 0** (0 of 40; this is what stages 1 and 2 are
for); the time of stage 0 (wrap median 0.33 s) is far below fTetWild's 18-50 s, but it produces a coarse tet mesh without
quality, so the comparison becomes meaningful only after stages 1 and 2 (their cost is the open part, section 10 item 3).
Note that the dihedral target cannot be checked against fTetWild's own output from its paper; the black-box run of fTetWild on
the same subset is in section 12 (2026-11-14).

## 11. Hand-over (2026-11-09): where stage 1 would start

**Status.** Stage 0 is done (#487). Stage 1 is **on hold** (owner 2026-11-09) until (1) the fTetWild black-box comparison
on the 40 Thingi10K meshes has run (`runs/tet-meshing`, `jobs/JOB-1.md`, `jobs/JOB-2.md`; evaluator modes `export` / `eval` of
`TetMeshBenchmark` on branch `claude/tet-fetwild-eval`, to be merged with the next PR on this topic) and (2) the alpha wrap keeps
sharp edges (Alpha Wrap 3.0, dual-contouring lever): without sharp edges the tet mesh would carry rounded features and is
probably not worth building.

**What exists.** `MeshAlphaWrap.ExtractTetMesh`, `TetMesh3`, `TetMeshQuality` (`NotValidReason`, `Measure`), the independent oracle
`TetOracle`, `TetMeshingStage0Tests`, `benchmarks/TetMeshBenchmark` (modes `insert`, `wrap`, `file`; `export` / `eval` on the branch above).

**What stage 1 would start from (measured, section 10 / 10.1).**
- Raw inside cells of the wrap: exactly valid on 8 of 8 procedural and 40 of 40 Thingi10K subset meshes; quality missing
  (min dihedral per mesh median 0.22 degrees, 0 of 40 meshes at 10 degrees, radius-edge above 2 for a median 25 % of the tets).
- `Delaunay3` costs 13 us per point = 1.9 us per cell in coherent order (random order is up to 5x slower per point at 400k):
  stage 1 must insert in a spatially coherent order; T5 (25 us per tet for refinement alone) is kept and to be measured.
- Real wrap surfaces have acute dihedral angles (1.0 % of the edges below 90 degrees, smallest 12.0 degrees) and triangle angles down to 1.29 degrees
  (procedural inputs: >= 86 and >= 8.6 degrees). Shewchuk's 90-degree condition does not hold, so the minimum-length floor
  of 5.2 is the working termination mechanism; `UnrefinedBadTets` per mesh must be reported.
- Design of stage 1 (5.2, 5.3): circumcentre insertion with encroachment, in-plane facet splits with rounded Steiner points
  and exact post-check with undo (needs `Delaunay3` cavity undo, per-cell labels, `IsFacet`), size control, no sliver removal.

**Open decisions for the owner when work resumes.**
1. Result of the fTetWild comparison: does fTetWild reach 10 degrees on all 40 meshes (`dih_ge10_all`)? If not, the target is a floor, not a copy.
2. Sharp features: only after Alpha Wrap 3.0; the corpus must then include sharp concave inputs (slot, notch, re-entrant corners),
   agreed but not yet added.
3. Sliver strategy for stage 2 (flips + smoothing recommended) and the dihedral target (10 degrees assumed).
4. Whether option (d) lattice stuffing (1-2 weeks, guaranteed dihedral bound) is worth building first for implicit domains.

## 12. fTetWild black-box comparison (2026-11-14)

fTetWild run as a black box on the GitHub-hosted runner (MPL-2.0: built from its README steps, run with its defaults, its
output read back by our evaluator and deleted; no source read, nothing ported), workflow `.github/workflows/tet-compare.yml`,
run 43547804151 on master 6b172b1, fTetWild commit 8118f81. Data branch `runs/tet-meshing`,
`compare/2026-11-14-run43547804151/` (SUMMARY.md, one CSV per variant). The command per mesh is
`FloatTetwild_bin -i <id>.stl -o <out>.mesh --max-threads 4 --no-binary --level 3`, with a 1800 s timeout. `--level` is only
the log level; the meshing parameters are fTetWild's defaults, ideal edge length `-l 0.05` and envelope `-e 1e-3` of the bbox
diagonal. Four shards ran on four ubuntu-latest runners (4 vCPU, 15 GB each) with four different CPUs (`machines.txt`): AMD
EPYC 9V74, Intel Xeon Platinum 8573C, Intel Xeon 6975P-C and AMD EPYC 9V45. Each mesh ran both meshers, one at a time, on its
shard's runner, so per-mesh times compare on one machine, while the medians below mix the four machines. Both outputs
went through the same checks (`TetFiles.Evaluate`: exact orientation, matched facets, closed and consistently oriented
2-manifold boundary, exact boundary self-intersection test). The `ft1` column of SUMMARY.md is empty: the single-thread
fTetWild variant was not run (`ft_threads` = 4).

The stage-0 row is from the same run. The stage-1 and stage-2 rows are the local subset runs of sections 14.8 and 15
(`benchmarks/TetMeshBenchmark/results/thingi10k-subset-stage1-peel-2026-11-13.csv`, `...-stage2-lever2-free050-2026-11-13.csv`,
the default parameters). Those ran on a different machine (this project's shared cloud container, one thread), so their
times are comparable to the runner's only in order of magnitude.

| 40 Thingi10K meshes | fTetWild (4 threads) | ours stage 0 (1 thread, same runners) | ours stage 1 (local) | ours stage 2, default (local) |
|---|---:|---:|---:|---:|
| output | 40 | 40 | 40 | 40 |
| valid (our definition, see below) | **33** | 40 | 40 | **40** |
| inverted tets (sum) | 0 | 0 | 0 | 0 |
| time median / max (s) | 8.48 / 318 | 0.26 / 6.45 | 0.49 / 10.3 | 0.60 / 13.4 |
| tets, median | 14,690 | 3,444 | 4,485 | 4,441 |
| smallest dihedral per mesh: median / best (degrees) | 11.4 / 23.2 | 0.22 / 1.29 | 1.18 / 10.0 | 9.54 / 10.5 |
| p01 dihedral, median (degrees) | 20.2 | 2.31 | 9.3 | 10.9 |
| meshes with p01 >= 10 degrees | 40 of 40 | 0 | 16 | 40 |
| meshes with every tet >= 10 degrees (valid or not) | **34** of 40 | 0 | 1 | **16** |
| share of tets below 10 degrees, median | 0 | 0.094 | 0.013 | 0.00025 |
| boundary to input, max, median (rel. bbox diagonal) | 0.00054 | 0.00167 | 0.00167 | 0.00249 |
| input to boundary, max, median (rel. bbox diagonal) | 0.00099 | 0.0313 | 0.0313 | 0.0313 |

- **Validity.** "Valid" is our definition: every tet exactly positive and a closed, consistently oriented 2-manifold boundary
  without self-intersections. It is not the measure behind fTetWild's published 99.97 % (section 10.1). fTetWild returns a
  mesh for all 40 meshes, but 7 of them fail our check with "a boundary edge occurs twice with the same direction": 110372,
  265729, 44041, 46460, 49884, 51809 and 58733. Their tets are all positive; the boundary they bound is not a consistently
  oriented 2-manifold. Two of them also leave the envelope: their boundary is up to 0.0082 (44041) and 0.0286 (49884) of the
  diagonal from the input. This is reported as measured; whether an option of fTetWild changes it was not checked.
- **Dihedral angles.** fTetWild does not keep every tet at 10 degrees on all 40 meshes: 6 meshes go below. 5 of them are
  valid (186204, 55429, 88564, 91589, 94240; lowest 7.84 degrees on 88564), and 49884 (9.2 degrees) is one of the invalid
  ones. So the answer to section 11, open decision 1, is no, and as decided there the 10-degree target is a floor, not a
  copy of fTetWild. On the measure of the stage-2 target, p01 >= 10 degrees, both reach 40 of 40. fTetWild's angles are
  clearly better: median p01 20.2 against 10.9 degrees, and 34 against 16 meshes with every tet at 10 degrees. Its best mesh,
  23.2 degrees, is 100026, which has only 23 tets.
- **Surface.** fTetWild tracks the input within its envelope: boundary within 0.00054 of the diagonal, input within 0.00099.
  Our boundary is the alpha wrap, 1/600 of the diagonal outside the input (stage 2: up to 1.5 offsets). The input-to-boundary
  distance of 0.031 is the wrap's resolution (alpha = diagonal / 20): the wrap does not follow concave detail finer than alpha.
  This is the main quality gap, and a property of the wrap, not of the tet stages. Sharp features: section 13.
- **Time and size.** Per mesh, on one runner, stage 0 is 36x faster than fTetWild at the median of the ratios (4.5x to 620x;
  per-shard medians 28x to 45x; fTetWild on 4 threads, ours on 1). Stage 2 measured locally is 0.60 s median and 13.4 s max,
  about an order of magnitude below fTetWild's 8.48 s median on the runners. Our meshes have about 3.3x fewer tets (4.4k
  against 14.7k at the median). The run does not measure why; fTetWild's default ideal edge length (`-l 0.05` of the
  diagonal) is one setting that sets its size. So "time in fTetWild's order" is met, with the surface-fidelity and tet-count
  caveats above.

Workflow notes, so the next run starts green:
- The build pins CMake 3.29. The runner's CMake 4.x, and 3.31, fail on CMP0169 in a fetched libigl recipe (#849, #862).
- The shard runners get the build-tree `libtbb.so.12` bundled; without it the loader exits 127 on every mesh (#877).
- The directories `compare/2026-11-13-run43517643335`, `compare/2026-11-14-run43533334096` (rc127) and
  `compare/2026-11-14-run43537131856` (30 s smoke test) on the data branch are not results.

## 13. Sharp features: does the Alpha Wrap 3.0 feature pass (#736) give the surfaces stage 1 needs? (2026-11-13)

`AlphaWrapFeaturePass` (draft #736) works on the raw wrap **as a surface mesh** (`DMesh3`): it moves vertices onto the offset
surface's planes and creases, splits edges at crease points and flips edges onto creases, every operation checked exactly
(no self-intersection, no contact with the input, angle floors 10 / 150 degrees). On its own metrics it does what stage 1 wants
from a CAD-like surface (T1, 20 meshes: concave creases on the miter line within one offset 0.02 -> 0.75, crease normal error
14.7 -> 4.7 degrees; procedural solids: concave 0.016 -> 0.981; benchmarks/SharpFeatureProbe README, variant KR).

But it **breaks the property stage 0 and the plan of 5.1 rest on**: the result is no longer a union of facets of the wrap's
`Delaunay3`, and in general not even of the Delaunay tetrahedralization of its own vertices. Local probe (branch
`claude/aw3-feature-pass` at 018ab24, not committed: a fresh `Delaunay3` of the surface's vertices, then counting the surface
triangles and edges that are not Delaunay facets / edges; the probe's six procedural solids):

| relative alpha 20 / offset 200 | raw wrap | after the feature pass |
|---|---|---|
| surface triangles that are not Delaunay facets | 0 of 934 .. 1298 (all six) | **3.8 .. 7.5 %** (40 .. 91 per solid) |
| surface edges that are not Delaunay edges | 0 | 1.3 .. 2.8 % |
| smallest interior surface dihedral | 59 .. 93 degrees | **21 .. 90** (vnotch 20.9, lshape 42, octant 49) |
| surface edges with a dihedral below 90 degrees | 0 .. 4 | 13 .. 50 |
| smallest triangle angle | 12 .. 16 degrees | 10.0 .. 11.4 (the pass's floor) |

At relative alpha 60 / offset 600 the share of missing facets is 1.5 .. 2.3 %, missing edges 0.5 .. 0.8 %, and the smallest
dihedral drops to 11.6 degrees (octant) and 16.1 (vnotch): the pass restores exactly the acute creases the wrap had rounded off.

Answer: **the feature pass gives the right boundary, but not in the form stage 1 can mesh directly.** The inside cells of the
wrap are a valid tet mesh of the *rounded* surface only. A tet mesh of the *featured* surface needs a boundary recovery step:
the featured surface is a clean PLC (closed, 2-manifold, intersection-free, triangle angles >= 10 degrees, vertex spacing from
the wrap), but 2-8 % of its triangles are missing from its Delaunay tetrahedralization and its dihedral angles go down to
12-21 degrees, so Shewchuk's 90-degree condition fails at exactly the features we want. Section 14 plans this as conforming
Delaunay refinement with a fallback to the rounded surface. Not measured yet: the same counts on the Thingi10K T1 meshes (first
task of stage 1, once #736 has merged).

## 14. Stage 1 plan (2026-11-13, for the owner's decision; no code yet)

### 14.1 Scope

Stage 1 = **Delaunay refinement of the interior with a radius-edge bound, size control, a sliver step, and the boundary either
the rounded wrap (S1-R) or the featured wrap (S1-F, conforming recovery)**, both through the same refinement core. Flip and
smoothing improvement (stage 2 of section 9) stays a separate step; section 14.5 says why the 10-degree target needs it.

Sources (papers only): J. R. Shewchuk, *Tetrahedral mesh generation by Delaunay refinement*, SoCG 1998; S.-W. Cheng, T. K. Dey,
J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012 (the algorithm DelTetPLC for PLCs, its insertion-radius proof, and the
chapters on small input angles and on sliver exudation); X.-Y. Li, S.-H. Teng, *Generating well-shaped Delaunay meshes in 3D*,
SODA 2001 (sliver-avoiding point placement); S.-W. Cheng, T. K. Dey, H. Edelsbrunner, M. A. Facello, S.-H. Teng, *Sliver
exudation*, J. ACM 2000; A. Rand, N. Walkington, *Collars and intestines: practical conforming Delaunay refinement*, IMR 2009,
and D. Cohen-Steiner, É. Colin de Verdière, M. Yvinec, *Conforming Delaunay triangulations in 3D*, SoCG 2002 (conforming
recovery with acute input angles). TetGen (AGPL), the CDT of Diazzi et al. (GPL / LGPL) and fTetWild (MPL) are paper / black
box only. Building blocks searched (section 2 and since): `Delaunay3` (insert, locate, exact predicates; no undo, no labels),
`QualityMesh2` (2D queue discipline, concentric shells, encroachment rules, the oracle / fuzz harness), `TetMesh3`,
`TetMeshQuality`, `TetOracle`; nothing for 3D refinement or tet improvement exists.

### 14.2 Algorithm (DelTetPLC with a floor)

State: one `Delaunay3` holding the wrap's (S1-R) or the featured surface's (S1-F) vertices; the boundary as **subsegments**
(surface edges whose two triangles meet at a dihedral off 180 degrees by more than `FeatureAngle`, default 20 degrees, plus all
edges in S1-F recovery) and **subfacets** (surface triangles, split in their plane); cell labels inside / outside, derived by a
flood fill from the box corners that never crosses a subfacet once every subfacet is a Delaunay facet.

Queue order (Shewchuk 1998; CDS 2012):
1. an **encroached subsegment** (a vertex inside its diametral ball, or the subsegment missing from the triangulation) is split,
   at its midpoint, or by the concentric-shell rule (split at a power-of-two distance from a shared endpoint, as `QualityMesh2`
   does in 2D) when its endpoint is shared with another subsegment at an angle below 90 degrees;
2. an **encroached or missing subfacet** gets the circumcentre of the subfacet (in its plane); if that point encroaches a
   subsegment, it is not inserted and the subsegment is split instead;
3. a **bad inside tet** (radius-edge > rho = 2, or circumradius above the size field) gets a point near its circumcentre (14.4);
   if it encroaches a subsegment or subfacet, it is not inserted and those are split instead.

Insertion is in queue order with the previous cell as the locate hint and the queue keyed by a coarse spatial key, so that
insertions stay coherent (stage 0: random order costs up to 5x per point).

**Rounding.** Midpoints and facet circumcentres are rounded to doubles, off the exact segment / plane by an ulp. The tets stay
exactly valid regardless (every cell of `Delaunay3` is exactly positive); what the rounding can change is the boundary. Rule (as
5.2): after the insertion, the two halves of a split subsegment and the new subfacets must be Delaunay edges / facets (an exact
`IsFacet` query); if not, the insertion is undone (cavity snapshot, new in `Delaunay3`) and the element is marked unsplittable
(counted). The output's boundary is the recovered surface; its distance to the S1-R / S1-F surface is asserted <= 2^-40 of the
diagonal in the tests, and the boundary is checked exactly for self-intersection (as the evaluator of section 12 does).

### 14.3 Termination

- **Without acute angles** (S1-R on smooth inputs; section 10: all procedural wraps have surface dihedrals >= 86 degrees):
  DelTetPLC's insertion-radius argument (CDS 2012) gives termination for rho > 2 and every new vertex at distance >= c * lfs(p),
  so the output size is O(integral of lfs^-3) and all inside tets meet rho.
- **With acute angles** (real wraps: 1 % of edges below 90 degrees, down to 12 degrees; S1-F: down to 11.6 degrees), the
  theorem does not apply. The **floor** makes termination unconditional: no point is inserted closer than `h_min` to an existing
  vertex (`Delaunay3.Insert(minSeparation)`), with `h_min = MinFeatureFactor * min(Offset, Alpha - Offset)` (the wrap's own
  vertex separation, section 2) and in S1-F also bounded by the featured surface's shortest edge times a factor. All vertices
  then lie in a bounded domain with pairwise distance >= `h_min`, so their number is at most
  vol(domain grown by h_min / 2) / vol(ball of radius h_min / 2): **every run terminates.** The price: a bad tet whose point is
  refused by the floor stays (`UnrefinedBadTets`, counted per mesh), and the radius-edge bound holds for all other tets.
- **Recovery cannot be left half done** (a missing subfacet means no valid boundary). S1-F therefore has its own budget: if a
  subsegment or subfacet is still missing when the floor refuses its split (or after `MaxRecoverySteiner` points), S1-F stops and
  the mesh is produced by S1-R instead (status `FeatureRecoveryFailed`, counted). So **validity stays 100 %** by construction:
  S1-R always succeeds (its boundary is conforming from the start, recovery is never needed), and S1-F is only returned when its
  recovery finished. A guaranteed S1-F needs protecting balls and weighted Delaunay (CDS 2012, small-angle chapter;
  Cohen-Steiner et al. 2002) or collars (Rand and Walkington 2009): not in stage 1; the fallback rate on the corpus decides
  whether it is worth it.

### 14.4 Slivers in stage 1

A radius-edge bound does not exclude slivers. Three options, papers only:

| | Li-Teng point placement (recommended for stage 1) | Sliver exudation | Flips + smoothing (stage 2, kept) |
|---|---|---|---|
| Idea | insert not the circumcentre but one of a few candidate points in a ball of radius delta * R around it, the first whose new tets contain no sliver | weights on the vertices so that slivers leave the weighted Delaunay triangulation | 2-3 / 3-2 / 4-4 flips, edge removal, vertex smoothing (Klingner and Shewchuk 2007) |
| Guarantee | a constant sliver bound (tiny in the proof), termination kept (the insertion radius shrinks by a constant factor) | a proven constant bound (tiny), in practice a few degrees | none; best practical results |
| Library cost | none new: `Delaunay3` + `TetMeshQuality.Measure` on the would-be cavity; deterministic candidates (a fixed point set scaled by R, no random numbers: 2^k invariant) | weighted `Delaunay3` and a power predicate in the exact code (new generated predicate, review of the bound) | non-Delaunay mesh operations, `Orient3d` only |
| Fit | same kernel, same termination argument, cheap | large; also the boundary must stay protected (zero weights) | needed for >= 10 degrees anyway (14.5) |

Recommendation: Li-Teng placement in stage 1, no exudation; flips + smoothing remain stage 2.

### 14.5 Targets for stage 1 (from the owner targets: 100 % valid, min dihedral >= 10 degrees, time in fTetWild's order)

- **Valid: 100 %** (40 / 40 Thingi10K subset and the procedural corpus incl. sharp concave inputs), by construction (14.3) and
  checked by the independent oracle and the evaluator of section 12.
- **Radius-edge <= 2** for all inside tets except `UnrefinedBadTets`; target 0 of those on the procedural set, reported on
  Thingi10K.
- **Dihedral**: stage 1 alone is not expected to reach 10 degrees on every tet: Delaunay refinement with a radius-edge bound
  leaves slivers near 0 degrees, and point placement lifts the tail to a few degrees in the literature, not 10. The stage-1 target
  is therefore p01 >= 10 degrees and min >= 1 degree per mesh (to calibrate on the subset); **min >= 10 degrees on every tet is the
  stage-2 target** (flips + smoothing), against fTetWild's measured values in section 12.
- **Time**: refinement <= 25 us per output tet single-threaded (T5); end to end (wrap + refinement) in the order of fTetWild's
  time on the same runner (section 12).
- Boundary: S1-R = the wrap's surface, S1-F = the feature pass's surface, both up to the rounding of 14.2; surface deviation
  reported both ways as in section 12.

### 14.6 Steps (one PR, one commit per step, stage 1 of section 9)

1. `Delaunay3` additions (additive): per-cell labels, `IsFacet`, cavity snapshot and undo; unit and fuzz tests (undo restores
   bit for bit, `Validate(true)` after random insert / undo sequences).
2. Refinement core on S1-R: queue, encroachment, floor, rounding rule with undo, size field, `MeshTetrahedralization` with
   `Counts` (`UnrefinedBadTets`, `unsplittable`, insertions per rule); tests of 6 / 7 (validity oracle, radius-edge, reuse,
   `Similarity.All()` bit for bit, determinism; mutants: no encroachment check, no floor, no undo, wrong queue order).
3. Li-Teng placement (14.4) with a switch for the A/B in the benchmark.
4. S1-F: recovery of the featured surface (needs #736 merged), the fallback to S1-R, counts on the procedural sharp solids and
   on T1; the corpus gets the sharp concave inputs of 11 (slot, notch, re-entrant corners).
5. Benchmark: `TetMeshBenchmark` mode for the refined mesh, the comparison workflow of section 12 with a variant `stage1`; before /
   after numbers in its README.

Estimate: steps 1-3 about 2 weeks (as section 9), step 4 about 1 week more, large model for 2 and 4 (termination, rounding).

### 14.7 Owner decisions (stage 1; decided 2026-11-13, go on all four, relayed by the coordinator)

1. **Boundary**: S1-F with fallback to S1-R (recommended; sharp features where recovery succeeds, rounded otherwise, validity
   unaffected) or S1-R only (simpler, features rounded as in stage 0)?
2. **Dihedral target**: 10 degrees on every tet as the stage-2 target, stage 1 with p01 >= 10 / min >= 1 degree (recommended,
   14.5), against the fTetWild numbers of section 12.
3. **Sliver step**: Li-Teng placement in stage 1, no exudation (recommended, 14.4).
4. **Order**: stage 1 starts with steps 1-3 now; step 4 after #736 has merged.

Decided: (1) S1-F with fallback to S1-R; (2) stage 1 targets p01 >= 10 degrees and min >= 1 degree, 10 degrees on every tet
in stage 2; (3) Li-Teng placement, no exudation; (4) steps 1-3 now, step 4 after #736.

### 14.8 Built so far (steps 1-3, 2026-11-13) and a change of plan for S1-R

- **Step 1** (`Delaunay3.RefinementSupport`, `Clone`): cell labels, incident cells, `IsEdge` / `IsFacet`, undo of the last insertion
  (restores the id allocation and the walk's random state, so the same insertion again gives the same ids). Tests
  `Delaunay3RefinementSupportTests` (fuzz against a twin triangulation and brute-force stars; 9 hand mutants killed, one
  equivalent removed by simplifying the undo).
- **Steps 2-3** (`MeshTetrahedralization`): **the surface is kept fixed** instead of the encroachment splits of 14.2. A point is
  kept only if every cell its insertion removes is inside (exact, on labels), otherwise it is undone. So (before the peeling of 14.9) the boundary is the
  wrap's surface triangle by triangle (no rounding at all, T2 exact) and validity holds by construction. Reason: the splits of
  14.2 were built and tried first; on curved wrap surfaces the midpoint splits cascaded (missing edges between near-coplanar
  triangles, flips did not settle them) into the floor on 6 of 8 procedural inputs (`RecoveryFailed`), even with the floor at
  s / 8. Surface splits move to step 4, where the featured surface needs recovery anyway, and need a 2D Delaunay
  refinement per surface patch rather than independent midpoint splits.
- Sliver step (Li-Teng, 14.4): candidates are the circumcentre and 2 x 14 fixed directions at 0.3 R and 0.6 R; the first whose new
  tets have no dihedral below 10 degrees is taken, else the best. Points towards the centroid for tets refused at the surface were
  tried and removed (they made the box and torus worse).
- Measured (`TetMeshBenchmark refine proc`, alpha 20 / offset 600, one thread): all 8 procedural inputs valid (exact oracle),
  boundary = wrap. Spheres n=16 / 40: **every tet radius-edge <= 2 and dihedral >= 10.0 degrees**. Box, cylinder, tori, soup:
  p01 dihedral 5.2 .. 10.9 degrees, 33 .. 315 tets (0.8 .. 6.5 %) left bad, **all of them with at least two vertices on the
  surface** (none inside). These are fixed only by changing the surface (step 4 surface refinement, or the stage-2 flips),
  which is the remaining gap to the stage-1 target (p01 >= 10, min >= 1 degree).
- Thingi10K subset (the 40 meshes of 10.1, fetched at run time; `TetMeshBenchmark refine <mesh> 20 600`, commit 0b732ec, one
  thread, 4-core cloud container; rows in `benchmarks/TetMeshBenchmark/results/thingi10k-subset-stage1-2026-11-13.csv`):

| Measure (40 meshes) | stage 0 (raw inside cells, 10.1) | stage 1 so far |
|---|---|---|
| valid (exact), boundary = wrap | 40 / 40 | **40 / 40**, all `Success` |
| tets, median / max | 3.4k / 13.2k | 4.6k / 18.0k |
| time wrap + refinement, median / max | 0.33 s / 13.4 s (wrap) | 0.41 s / 8.8 s; refinement median 0.13 s, 26 us per tet |
| radius-edge above 2, median share | 25 % | 8.9 % |
| dihedral below 10 degrees, median share | 9.3 % | **2.5 %** |
| smallest dihedral per mesh, median | 0.22 degrees | 0.35 degrees; 5 of 40 at >= 1 degree |
| p01 dihedral per mesh, median | (p5 6.7 degrees) | 5.2 degrees; 2 of 40 at >= 10 |
| tets left bad (all touching the surface), median share | | 9.7 % |

  So validity and time are on target, the interior is clean, and **the stage-1 dihedral target is not met on real meshes**
  (p01 >= 10 on 2 of 40, min >= 1 on 5 of 40) because of the tets on the fixed wrap surface, whose triangles reach 1.3 degrees
  (10.1). The next step for both S1-R and S1-F is therefore a surface refinement that keeps the conformity (a 2D Delaunay
  refinement per surface patch with the 3D facet check and undo), before or together with step 4.

### 14.9 Surface refinement round (2026-11-13, coordinator go: "2D Delaunay refinement per patch with the 3D facet check and undo")

**Decision (coordinator, 2026-11-13): option (a) below, target kept.** The target p01 >= 10 degrees and min >= 1 degree moves
from the end of stage 1 to the end of stage 2; the measured stage-1 values stand as they are: p01 >= 10 on 16 of 40, min >= 1 on
23 of 40, median p01 9.3, median min 1.18 degrees. Stage 2 (flips + smoothing, Klingner and Shewchuk 2007 from the paper only,
boundary vertices sliding on the wrap surface) keeps the surface invariants exactly (closed, manifold, intersection-free, input
strictly inside: an exact check per move, undo on failure), is measured on the 40-mesh subset after every lever, and is its
own PR after this one.

Building blocks searched first: `QualityMesh2` / `DelaunayKernel2` / `ConstrainedDelaunayMesh2` are planar (one plane, their own 2D
kernel) and do not apply to a curved wrap surface as a whole; the wrap itself has a surface quality refinement on the offset
surface (`MeshAlphaWrap.RefineMinAngleDegrees`, Chew 1993 / Boissonnat-Oudot 2005), now passed through as
`SurfaceMinAngleDegrees`. Tried, in this order (procedural set, alpha 20 / offset 600):

1. **In-plane surface splits** (circumcentre of a surface triangle in its plane, else the midpoint of its longest edge), each kept
   only if afterwards no other surface triangle is cut and every new surface triangle is a facet (3D check, undo): runs away.
   A point in the plane of a curved surface's triangle makes flat tets with its neighbours, whose circumcentres lie beyond the
   surface again; after 20000 points the sphere n=6 had 68 % bad tets. Rejected (kept out of the code).
2. **A higher wrap surface angle** (`SurfaceMinAngleDegrees` 25 / 30 instead of 15): more tets and more bad ones on every input
   but the fine spheres. Kept at 15.
3. **Peeling boundary slivers** (built, `PeelBoundarySlivers`, default on): the bad tets left are almost all on the surface
   (Thingi10K: 8.6k with no surface facet but at least two surface vertices, 11.0k with one, 1.2k with two surface facets after
   peeling). A bad inside tet with exactly two surface facets is moved outside, a 2-2 flip of the surface onto its other two
   facets, if the new surface edge is not a surface edge yet (else the surface would stop being a manifold) and the tet shares
   no point with any input triangle (exact: `ExactTriangleIntersection` with its faces, and a vertex-inside test). The surface
   stays closed, 2-manifold, intersection-free (the new facets lie inside the removed cell) and around the input; it moves inward
   by at most the sliver's height (its fourth vertex's distance to the opposite surface facet, a convexity bound). The boundary
   keeps only wrap vertices. A final sweep repeats peeling until nothing changes. Tests: postcondition "no peelable bad tet
   left", the exact input test (unit cases incl. a triangle through one face only), the edge rule against a brute-force check
   on random labels, a thin torus where 157 peels are refused for the input; hand mutants all killed.

Thingi10K subset again (rows: `benchmarks/TetMeshBenchmark/results/thingi10k-subset-stage1-peel-2026-11-13.csv`):

| Measure (40 meshes) | stage 1 fixed surface (14.8) | with peeling |
|---|---|---|
| valid (exact), boundary closed / no self-intersection | 40 / 40 | 40 / 40, 40 / 40 |
| p01 dihedral >= 10 degrees | 2 of 40 (median 5.2) | **16 of 40** (median 9.3) |
| smallest dihedral >= 1 degree | 5 of 40 (median 0.35) | **23 of 40** (median 1.18) |
| dihedral below 10 degrees, median share | 2.5 % | 1.3 % |
| time wrap + refinement, median / max | 0.41 s / 8.8 s | 0.49 s / 10.3 s |
| peels / refused for the input / refused for the edge | | 5412 / 6181 / 225 (attempts, the sweep repeats them) |

**The stage-1 target (p01 >= 10, min >= 1 on every mesh) is still missed** (16 and 23 of 40). What is left are tets with one or
no surface facet that touch the surface with an edge or a vertex; neither peeling (it would move the surface by a whole tet) nor
interior points (their cavities reach the surface) removes them. Options for the owner, before stage 2: (a) take stage 2's
flips + smoothing now, with the boundary vertices allowed to slide on the wrap surface (the classical answer for boundary
slivers; Klingner and Shewchuk 2007); (b) surface points on the offset surface instead of in the triangles' planes (the wrap's
own refinement rule, run on request from the tet refinement: a restricted-Delaunay surface mesher in the manner of
Boissonnat-Oudot, more work in `MeshAlphaWrap`); (c) relax the stage-1 target to the measured values and meet 10 degrees in
stage 2 only.

## 15. Stage 2: flips and smoothing (2026-11-13; coordinator: option (a), target kept)

Built: `TetMeshImprover` (internal), run by `MeshTetrahedralization` when `Improve` (default on) after stage 1. After B. M.
Klingner, J. R. Shewchuk, *Aggressive tetrahedral mesh improvement*, IMR 2007 (paper only; Stellar's code not read): hill
climbing on the smallest dihedral angle over the tets below `SliverDihedralDegrees` (10), worst first, in passes until nothing
changes (at most 20):

- **edge removal**: the m <= 8 tets around an interior edge replaced by the 2 (m - 2) tets of the best triangulation of the ring
  polygon (dynamic programming over the ring; m = 3 is the 3-2 flip);
- **face removal**: the 2-3 flip of an interior facet;
- **smoothing of interior vertices**: steps along the numerical gradient of the worst tet of the star, or to the star's centroid;
- **boundary vertex moves**: the same steps for boundary vertices, in any direction (`BoundaryMovesTangentOnly` = false) or in
  the tangent plane of the vertex's boundary fan, each move capped cumulatively at `BoundarySlideFactor` (0.5) offsets from
  the vertex's original position on the wrap.

What is enforced, per operation (an operation that fails a check is not made):
- every tet it creates is exactly positively oriented (`ExactPredicates.Orient3d`), and the smallest dihedral angle of the tets
  it replaces strictly increases;
- edge and face removal never change the boundary facets (they work on interior edges and facets);
- a boundary move is kept only if the swept tets (old position, new position, each boundary link edge of the vertex) share
  no point with any input triangle (exact, as the peeling test), and the moved boundary triangles intersect no other boundary
  triangle beyond shared corners (`ExactTriangleIntersection.IntersectsBeyondShared`, exact).

So, **proven by construction**: every tet positive, the boundary closed and 2-manifold (no operation changes its topology), free
of self-intersections, and the input strictly inside it (no boundary point ever passes over an input point). **Bounded by
construction**: every boundary vertex is at most 0.5 offsets from its wrap position (cumulative cap; test
`BoundaryDisplacement_IsCumulativelyCapped_FromTheOriginalWrapPosition`). **Measured, not proven**: the distance from the
boundary to the input, at most 1.5 offsets on the subset (the wrap's own distance to the input is about one offset but has no
proven upper bound in `MeshAlphaWrap`, so no bound wrap + 0.5 offset is claimed).

Measured on the 40-mesh subset after every lever (one thread; rows `benchmarks/TetMeshBenchmark/results/thingi10k-subset-stage2-*.csv`):

| Measure (40 meshes) | end of stage 1 | lever 1: interior flips + smoothing | lever 2a: + boundary moves, tangent only, cap 0.25 offset | lever 2b (default): + boundary moves, any direction, cap 0.5 offset |
|---|---|---|---|---|
| valid (exact), boundary without self-intersection | 40 / 40 | 40 / 40 | 40 / 40 | 40 / 40 |
| p01 dihedral >= 10 degrees | 16 | 38 | 39 (839690: 8.57) | **40** |
| smallest dihedral >= 1 degree | 23 | 40 | 40 | **40** (worst 2.86) |
| smallest dihedral >= 10 degrees | 1 | 7 | 9 | 16 |
| median smallest / p01 dihedral | 1.18 / 9.3 | 6.2 / 10.8 | 6.8 / 10.8 | 9.5 / 10.9 |
| boundary to input, max (offsets) | 1.0 | 1.0 | 1.25 | 1.5 |
| time wrap + refinement + improvement, median / max | 0.49 s / 10.3 s | 0.53 s / 9.9 s | 0.65 s / 15.0 s | 0.60 s / 13.4 s |

The stage-2 target (p01 >= 10 and min >= 1 degree on every mesh of the subset) is met by lever 2b, the default; lever 2a, which keeps
the boundary vertices in their tangent planes, is the conservative option (switch `BoundaryMovesTangentOnly`, and
`BoundarySlideFactor` = 0.25) and misses one mesh (839690, p01 8.57). Face removal is tested (a constructed 2-3 case) but was never
an improvement on the subset; edge removal (3.3k) and smoothing (2.2k interior, boundary moves) do the work.

## 16. The dihedral floor: diagnosis and the lever (2026-11-17)

Target (owner, research.md item 5): every tet >= 10 degrees on 100 % of the 40-mesh subset. At the start (master d1b86232, one
process at a time, 4-core cloud container): 17 of 40 have every tet >= 10 degrees, all 40 have p01 >= 10 degrees, 361 tets
below 10 degrees in all, the worst 3.17 degrees (839690).

### 16.1 Diagnosis: where the 361 tets are, what shape, why stage 2 stops

`TetMeshImprover.Diagnose()` (internal; `MeshTetrahedralization.DiagnoseImprovement`, benchmark `G3_TET_DIAG=1`) classifies every
tet left below 10 degrees after stage 2 and tries every operation on it side-effect free (dry run), recording why each one fails.
Shapes after S.-W. Cheng et al., *Sliver exudation* (J. ACM 2000, fig. 1): sliver (no short edge, no vertex over the opposite
face), cap (a vertex over the opposite face's interior), needle and wedge (a short edge, shortest / longest < 0.3; wedge with two
small angles).

| Finding (the 361 tets of the 23 meshes that miss it) | count |
|---|---|
| **shape**: needle / wedge (a short edge) / sliver / cap | **196 / 139** / 25 / 1 |
| the short edge of the needles and wedges: a boundary edge / boundary vertex to interior vertex / chord between two boundary vertices / interior | **168** / 98 / 60 / 9 |
| **touch the boundary**: by a facet (one / two) / by an edge only / by a vertex only / not at all | 181 / 59 / 102 / 19 / **0** |
| an operation would still succeed (the 20-pass cap of stage 2 stopped it) | 142 |
| boundary vertex moves: no step improves / cap used up / refused by the exact input test / by the self-intersection test | **760** / 138 / 12 / 0 (per vertex) |
| edge removal: boundary edge / ring > 8 / best ring triangulation not better | 1001 / 13 / 1151 (per edge) |

So **every tet left touches the boundary, and the exact surface invariants are almost never the reason**: input refusals 12, self
intersections none. The blockers are local optima of the hill climbing on needles and wedges whose short edge is a wrap surface
edge (3-10 offsets) next to interior edges of about 50 offsets (one to two alpha): a size jump at the boundary that a fixed surface
triangulation cannot grade. Probes (rows not kept, all invariants checked):

| Probe (all 40) | every tet >= 10 degrees | worst |
|---|---|---|
| stage 2 as it is | 17 | 3.17 |
| 500 passes instead of 20 | 20 | 3.17 (hill climbing creeps, the optima are real) |
| stage-1 size bound 15 / 6 offsets (`MaxCircumradius`, uniform) | no change on the four worst; at 6 offsets 6.5x the tets | 4.12 on 113956 unchanged |
| edge contraction (an end of a short edge into the other end) | 18 | 3.18 |
| **edge splits with roll-back (16.2)** | **31** (20 passes), **35** (100 passes) | 6.56 |
| edge splits + contraction, 50 passes | **38** | 7.64 |

- Interior refinement does not reach these tets: inserting near the surface is refused (the point's cavity reaches an outside cell),
  so a finer interior leaves the boundary layer as it is.
- Contraction of the short boundary edges is refused by the slide cap on all of them (173 in that run; the edge is 3-10 offsets,
  the cap 0.5 offsets);
  only contractions of an interior vertex into a neighbour take place, so contraction is kept for interior vertices only (16.2).
- The surface triangulation, not its geometry, is the lock: splitting boundary edges **in place** (the boundary as a point set does
  not move) refines the boundary layer and frees the hill climbing.

Sliver exudation (Cheng et al. 2000) and perturbation (Tournois, Srinivasan, Alliez 2009) target slivers of a Delaunay
triangulation; 25 of the 361 are slivers and none is interior, so neither reaches the bulk (upper bound <= 25 tets, and both would
have to act on a fixed, non-Delaunay boundary layer). 14.4's recommendation (no exudation) stands.

### 16.2 The lever: vertex insertion by edge splits, and interior edge contraction (stage 2)

After Klingner and Shewchuk 2007 (vertex insertion with roll-back; paper only) with a simpler insertion: for a tet still below 10
degrees after edge removal and face removal, in `TetMeshImprover`:

- **Edge contraction** (`ContractEdges`, edges shortest first, both directions): an interior vertex u merged into a neighbour w (w
  keeps its position, the boundary does not change) under the link condition of T. K. Dey, H. Edelsbrunner, S. Guha, D. V. Nekhayev,
  *Topology preserving edge contraction* (1999), every new tet exactly positive and the smallest dihedral angle of u's star raised.
- **Edge split** (`SplitEdges`, edges longest first): the edge's midpoint m becomes a vertex, each tet around the edge is cut in two.
  A boundary edge is split in place: its two boundary triangles become four in the same planes (up to the rounding of m); the flat
  tet (a, b, c, m) of each old triangle is checked exactly against the input (`TetTouchesMesh`) and the four new triangles against the
  other boundary triangles (exact, as boundary moves). Then the new vertex is smoothed (8 steps; boundary moves with all their exact
  checks for a boundary vertex) and the edges at it get edge removal. The split is kept only if the smallest dihedral angle of all
  tets at the edge's ring vertices strictly increases; otherwise it is undone exactly (journal of tets, boundary
  triangles and grid entries; the free list of tet ids, the order of the per-vertex lists and the vertices' versions are restored,
  so a refused split leaves the improver's state as it was; test: the result is bit for bit the one before). A failed split is not retried until a vertex of its tet's 1-ring changes.
- Order per tet: edge removal, face removal, contraction, split, smoothing, boundary moves; at most 50 passes (`ImprovePasses`,
  was 20). Splits before smoothing measured better (31 against 21 of 40 at 20 passes): smoothing first spends the slide budget.

What holds, by construction: every tet exactly positive; the boundary closed and 2-manifold (contractions leave it as it is, splits
replace two triangles by four in place); free of self-intersections and the input strictly inside it (the exact checks); the smallest
dihedral angle of the mesh never decreases (every operation raises the minimum of the tets it changes). The slide cap: a split
vertex's cap is measured from the midpoint of its edge's first positions, which lies on the stage-1 surface (boundary edges are
only ever split, so each lies in one stage-1 triangle), minus a margin for that midpoint's rounding, so every boundary vertex stays
within `BoundarySlideFactor` offsets of the **stage-1 surface** (the wrap after peeling), and so within the peel depth plus
`BoundarySlideFactor` offsets of the wrap (restated 2026-11-17 by owner decision, 16.4, where the peel depth is defined; the text said
"of the wrap surface", which peeled triangles break; the margin was needed: at UTM coordinates the vertex was 0.5000061 offsets out,
test `Similarity_ValidAndPow2ScalesBitForBit`). Scale-free and serial: 2^k scalings give the same mesh bit for bit (tested).

### 16.3 Result on the 40-mesh subset (one process at a time; rows `results/thingi10k-subset-stage2-{master,splits}-2026-11-17.csv`)

| Measure (40 meshes) | master (stage 2) | with splits and contractions (default) |
|---|---|---|
| valid (exact), boundary without self-intersection | 40 / 40, 40 / 40 | 40 / 40, 40 / 40 |
| **every tet >= 10 degrees** | 17 | **36** |
| p01 >= 10 degrees / smallest >= 1 degree | 39 / 40 | 40 / 40 |
| tets below 10 degrees, all meshes | 361 | 63 |
| worst smallest dihedral / median | 3.17 / 9.57 | 6.11 (839690) / 10.02 |
| tets, median / max | 4.4k / 17.0k | 4.4k / 17.7k |
| time (wrap, refinement, improvement), median / max / sum | 0.69 s / 12.2 s / 41.2 s | 0.81 s / 14.6 s / 54.9 s |
| refinement + improvement per tet, median | 55 us | 78 us |
| splits / contractions, all meshes | | 2014 / 493 |

Left: 839690 (6.11 degrees, 4.6 s instead of 1.6 s: most split attempts there fail), 120628 (8.84), 46460 (9.10), 113956 (9.57).
The count is sensitive to choices between equal candidates: versions of this code that differed only in the order of lists after
a refused split, or in the rounding margin of the cap, gave 37 and 38 (113956 and 120628 at >= 10.0); 46460 reaches 10.01 with a
slide cap of one offset; 839690 stays below 8 degrees in every variant tried (needles on short boundary edges with rings of more
than 8 tets around their long edges).

`dev_out_max_rel` (boundary vertices to the input) rises from 0.0025 to 0.050 of the diagonal: a sampling effect, not a moved
surface. The split vertices lie on the wrap's triangles, and the wrap's edge midpoints are already 0.050 of the diagonal from the
input on master (alpha = diagonal / 20 in concave places); master's vertex sample saw only the wrap vertices (one offset away).
The boundary itself stays within 0.5 offsets of the wrap surface, as before.

Time: T5 (25 us per tet) was already missed by stage 2 on master (median 55 us); this adds about 40 %. Not done: the per-attempt cost
of a split on meshes like 839690 (smoothing with the exact checks dominates).

Owner decisions: (1) the default: `SplitEdges` and `ContractEdges` on, `ImprovePasses` 50 (this PR; the output of
`MeshTetrahedralization` changes, more tets on the boundary layer; off gives master's result); (2) the last four meshes: accept
36 of 40, or a next lever for 839690 (rings > 8, or a cavity insertion as in Klingner and Shewchuk instead of edge splits).

### 16.4 The slide bound on peeled surfaces (owner decision 2026-11-17)

Found by the default-alpha tests of stage P1 (17.8): on the thin torus at alpha / 40 under the similarity "rotated, scale 1e-3, utm"
a boundary vertex was **0.642 offsets** from the wrap surface, against the 0.5 that 16.2 stated for the wrap. Validity is not affected
(exact positive tets, closed manifold boundary, no self-intersection, input inside). Cause: peeling (14.9) replaces two wrap triangles
by the sliver's other two faces, which lie below the wrap; a split of an edge in such triangles measures its vertex's cap from a
point below the wrap. Probe on that case: peeling off 0.498 offsets, splits off 0.499, both on 0.642; at alpha / 20 the same case is
0.500: the code is master's, and only the finer alpha exposes it.

**Decision (owner, 2026-11-17): the bound is restated, the code is unchanged**: every boundary vertex stays within
`BoundarySlideFactor` offsets of the stage-1 surface (the wrap after peeling), so within the peel depth plus `BoundarySlideFactor`
offsets of the wrap (16.2). The **peel depth** is the largest distance from a point of the stage-1 surface to the wrap. One peel moves
the surface inward by at most the peeled sliver's height (14.9), but the final sweep repeats peels, so the depths can add up, and no
parameter bounds the total. Reason: both sound ways to keep the old bound cost quality, while the measured fidelity is the same either way.

| Every tet >= 10 degrees (all valid, no self-intersection) | master (kept) | refuse splits of edges whose two triangles are peeled | cap reduced by the midpoint's depth below the wrap |
|---|---:|---:|---:|
| 40-mesh subset, alpha / 20 | 36 (worst 6.11) | 34 (3.98) | 34 (5.09) |
| 40-mesh subset, alpha / 40 | 40 | 39 (75915 5.22) | 39 (75915 4.70) |
| 500-mesh sample, alpha / 40 | **494** (7.12) | not run | 488 (3.35) |
| sample, input to boundary max, median | 0.0228 | | 0.0228 |

The rejected alternatives (not built into the library; measured on a local branch): stage 1 flags the boundary triangles that are
wrap facets (the `Refiner` records them before any peel), stage 2 carries the flag through splits and undo, and for an edge whose two
triangles are both peeled either refuses the split or measures the depth of its midpoint below the wrap (an AABB tree of the wrap) and
takes it off the new vertex's cap; both restore the 0.5-offset bound to the wrap (hand mutants killed, the UTM case fails without
them). The cap reduction does not save the splits because peeled chords are often deeper than the 0.5-offset cap.

Test: `TetMeshingStage2Tests.Invariants` checks every boundary vertex against the stage-1 surface (a second run of the same parameters
without stage 2, deterministic for every `MaxThreads`, gives that surface; tests that already have that run pass it in). Mutants of the stage-1 cap (the limit 1.5x; a split vertex's cap
measured from its own start instead of its edge's first-position midpoint) are killed. The wrap-side bound (peel depth + 0.5 offsets)
is not checked separately: the peel depth is not exposed, and with the vertex on the stage-1 surface it follows by the triangle
inequality. A distance check cannot see a move along the surface beyond the cap: the cumulative cap of never-split vertices is checked
by `BoundaryDisplacement_IsCumulativelyCapped_FromTheOriginalWrapPosition`, and that of split vertices only through this distance
check.

## 17. Production quality: gap study and staged plan (2026-11-17; study only, no library code)

Task (owner 2026-11-17, "tet meshing to production quality"): measure and rank what `MeshTetrahedralization` lacks for production
use, propose stages with effort, risk and owner decisions, and fix the first stage's go / no-go criteria before it is built. The
dihedral floor on the last 4 of the 40 meshes (16.3, decision 2) is worked on in another session in `TetMeshImprover` and is not
part of this plan, but 17.2 bears on it: at a finer default alpha the floor holds on all 40 without a new lever.

Base: master 86e94029 (stage 2 with splits and contractions, the defaults of section 16). New in this section: one alpha sweep
on the 40-mesh subset (17.2) and a small classification probe (17.2, last item). Everything else is from sections 10-16 and the code.

### 17.1 Gap table (ranked)

Rank = distance from what a user of a production tet mesher expects (fTetWild, TetGen, CGAL Mesh_3 as reference points) times
how many uses it blocks. Numbers: the default (alpha = diagonal / 20) unless a column says otherwise; "17.2" = the sweep below.

| Rank | Gap | Today, measured | Production expectation | Best lever measured or known |
|---|---|---|---|---|
| 1 | **Surface fidelity** | input to boundary, max per mesh (vertex sampled), median **0.031** of the diagonal, worst 0.093; boundary to input median 0.030 (16.3: split vertices on the wrap's chords). Concave detail and thin parts that the wrap's alpha ball does not enter are bridged (17.2, probe). Sharp creases rounded (section 13) | fTetWild on the same meshes: 0.00099 / 0.00054 (section 12), 31x / 56x better | alpha: at diagonal / 80 the median input-to-boundary max is 0.0137 and boundary-to-input 0.0017 (= one offset), at x1.55 the time and x4.7 the tets; / 160: 0.0087 at x17 the tets (17.2). Beyond that: a local alpha, S1-F creases, option (b) |
| 2 | **Evidence at scale** | 40 meshes (every 250th Thingi10K id) and the procedural corpus | fTetWild publishes 10 000 Thingi10K meshes (99.97 % in Table 2, section 10.1) | a run on a larger sample on GitHub-hosted runners (the `tet-compare` pattern, without fTetWild) |
| 3 | **Quality: floor and guarantee** | every tet >= 10 degrees on 36 of 40 (worst 6.11), p01 >= 10 on 40; radius-edge above 2 on a median 7.3 % of the tets (refused at the fixed surface, 14.8); no guarantee: stage 2 is hill climbing, and the result is measured, not certified per run | a stated bound per run (TetGen `-q`: a radius-edge target, 2.0 by default, which its features page states without a proven dihedral bound; isosurface stuffing: proven dihedral bounds) | alpha diagonal / 40: 40 of 40 at 10 degrees, radius-edge above 2 on 2.5 %; diagonal / 80: 40 of 40, 0.4 % (17.2) |
| 4 | **Output and API** | `TetMesh3` (vertices, tets, boundary triangles) only; writers for Medit `.mesh` and Gmsh `.msh` 2.2 exist only in `benchmarks/TetMeshBenchmark/TetFiles.cs`; no VTK; no boundary markers, no region ids, no map from a boundary triangle to the input triangles it stands for; no g3cli command or MCP tool | FEM pre-processing needs a file format a solver reads and boundary tags to put loads and supports on | library writers + markers from `MeshBatchQueries.NearestPointBatch` (exists) + a g3cli `tetmesh` command |
| 5 | **Size control** | `MaxCircumradius` (uniform, interior only); the boundary's size is the wrap's (alpha); no size function, no grading, no target element count (section 8's `TargetEdgeLength` / `SizeFunction` were not built) | a target edge length and a size field (TetGen `-a` / `-m` size map, fTetWild `-l`, CGAL Mesh_3 size criteria) | interior: a size predicate in the refinement queue (small); boundary: only through the wrap's resolution |
| 6 | Time | 0.66 s median, 12.6 s max (120628, 2.5M input triangles, the wrap dominates) per mesh, one thread, local; fTetWild 8.48 s median on 4 threads on the runners (section 12); refinement + improvement 69 us per tet median (T5: 25 us) | "in fTetWild's order" (owner target) | met; alpha / 80 is 1.06 s median, 11.3 s max (17.2). T5 per tet is met at alpha / 40 and / 80 (23 / 10 us), because the improver's tail work shrinks |
| 7 | Determinism, 2^k scale invariance | done: serial result; the wrap's parallel carving gives the same result for every `MaxThreads` (tested on the wrap, `AlphaWrapRoundsTests` and others; no tet test varies `MaxThreads`, P1 adds one); `Similarity_ValidAndPow2ScalesBitForBit`, `ReuseAndRepeatedRuns_GiveTheSameTets` (`TetMeshingStage1Tests`, stage-2 suites) | the library's own rule (CLAUDE.md) | none needed; every new stage keeps these tests |
| 8 | Validity | 40 of 40 at every alpha of 17.2 and on the procedural corpus, exact; fTetWild 33 of 40 by our definition (section 12) | 100 % | none needed; it is the strongest property and every stage keeps it by construction |

### 17.2 Measurement: fidelity, quality, time and size against alpha (2026-11-17)

`TetMeshBenchmark refine <id>.stl <alpha> 600` per mesh, one process at a time, master 86e94029, Release net10.0, one thread, the
4-core cloud container (shared: read times as orders of magnitude); the 40 meshes of section 10.1, fetched at run time, not
committed. Offset fixed at diagonal / 600; alpha diagonal / 20 (the default), / 40, / 80, / 160. Rows:
`benchmarks/TetMeshBenchmark/results/thingi10k-subset-alpha{20,40,80,160}-2026-11-17.csv` (alpha 20 reproduces the
`stage2-splits` rows of 16.3: 36 of 40 at 10 degrees, worst 6.11). `time_s` is `MeshTetrahedralization.Compute` (wrap, refinement,
improvement). Deviation is vertex sampled (`TetFiles.Evaluate`: in = input vertices to the boundary, out = boundary vertices to
the input), relative to the diagonal: lower bounds of the one-sided Hausdorff distances, as in section 12.

| 40 meshes, offset diagonal / 600 | alpha / 20 (default) | alpha / 40 | alpha / 80 | alpha / 160 | fTetWild (section 12) |
|---|---:|---:|---:|---:|---:|
| valid (exact), boundary without self-intersection | 40 / 40 | 40 / 40 | 40 / 40 | 40 / 40 | 33 / 40 |
| **every tet >= 10 degrees** | 36 (worst 6.11) | **40** | **40** | **40** | 34 |
| p01 dihedral, median (degrees) | 10.9 | 11.4 | 11.9 | 11.9 | 20.2 |
| radius-edge above 2, median share | 7.3 % | 2.5 % | 0.4 % | 0.1 % | |
| **input to boundary, max, median / worst** | 0.031 / 0.093 | 0.022 / 0.075 | **0.014** / 0.060 | 0.0087 / 0.054 | 0.00099 |
| input to boundary, mean, median | 0.0058 | 0.0041 | 0.0025 | 0.0018 | |
| boundary to input, max, median | 0.030 | 0.0096 | 0.0017 | 0.0017 | 0.00054 |
| tets, median / max | 4.4k / 17.7k | 7.1k / 18.1k | 18.5k / 44.6k | 68k / 171k | 14.7k |
| time, median / max / sum (s) | 0.66 / 12.6 / 46 | 0.70 / 12.7 / 41 | 1.06 / 11.3 / 54 | 2.92 / 11.8 / 137 | 8.48 / 318 (4 threads, runners) |
| per mesh against alpha / 20 (median of ratios): time / tets / input-to-boundary max | 1 | 1.05 / 1.69 / 0.81 | 1.55 / 4.7 / 0.51 | 3.99 / 17.3 / 0.33 | |

Findings:

- **The quality floor is a resolution effect.** All four meshes left at alpha / 20 (839690 6.11, 120628 8.84, 46460 9.10, 113956 9.57)
  reach 10 degrees at alpha / 40, and 839690 takes 0.68 s instead of 4.1 s. This fits the diagnosis of 16.1 (needles and wedges on
  short wrap edges next to interior edges of one to two alpha: a smaller alpha shrinks that size jump). So the last-4 question of
  16.3 has a second answer besides a new improver lever: a finer default alpha (an output change: owner decision).
- **Uniform alpha buys fidelity slowly and tets fast.** The input-to-boundary max falls about as alpha^0.6 (0.031, 0.022, 0.014,
  0.0087 at / 20 .. / 160) while the tets grow 1.7x, 4.7x, 17x; the boundary-to-input side drops to one offset at alpha / 80 (the
  split vertices of 16.3 now lie on short chords). At alpha / 80 we have 1.3x fTetWild's tet count and are 14x off its
  input-to-boundary distance; at / 160, 4.7x its tets and still 9x off. Uniform alpha alone does not reach fTetWild's fidelity at a
  comparable size; a resolution that is fine only where the detail is (P4(a)) is the lever.
- **What is left is mostly concave detail and thin parts, not buried geometry.** Probe on six meshes picked by hand among those
  with a large input-to-boundary max at alpha / 40, including the ones whose max did not move from / 20 to / 40 (scratch, not committed: input vertices farther than
  alpha / 2 from the boundary at alpha / 40, classified by the input's winding number at v +- 1e-4 diagonal along the vertex normal;
  "buried" = both sides >= 0.5): 110372 612 open / 63 buried, 81132 492 / 0, 58733 1948 / 418, 103538 2948 / 0, 1066900 32 / 0, 75915 0 / 2. Buried vertices (part of 110372 and 58733, all of 75915's two) are input that no enclosing
  boundary can reach. So most of the gap is reachable by a boundary that
  follows the input more closely, at the price of more tets where the detail is.
- Time is not the constraint: alpha / 80 costs 1.06 s median; the max is the 2.5M-triangle input at every alpha, the wrap's input
  side.

### 17.3 Candidate stages

| Stage | Content | Effort | Risk | Owner decisions |
|---|---|---|---|---|
| **P1 Resolution default and evidence at scale** | (a) a finer default alpha (diagonal / 40, or / 80, chosen by 17.5's run) for `MeshTetrahedralization` only (the wrap's own default stays); (b) a fidelity-first parameter, eg `RelativeAlpha` documented with the 17.2 table, or a `MaxSurfaceDeviation` that picks alpha; (c) a sample of about 500 Thingi10K meshes (fixed list of ids, fetched at run time) on GitHub-hosted runners by `workflow_dispatch`, the `tet-compare` workflow pattern without fTetWild, CSVs on `runs/tet-meshing`; tests: the default pinned, the procedural suites re-baselined, invariance / reuse / determinism unchanged, plus a tet test over `MaxThreads` | 2-3 days + one runner run (about 4 runners x 1 h, planning) | low: validity is by construction; the risk is time and tet count on large inputs (measured x1.05-1.55 median on the subset) | the default (output change of `MeshTetrahedralization`); the sample size and runner budget |
| **P2 Output and API** | library writers (additive): Medit `.mesh` (with boundary triangle references), Gmsh `.msh` 4.1 (and 2.2), VTK (legacy `.vtk` unstructured grid); per boundary triangle a marker (the nearest input triangle's group id, else its id, by `MeshBatchQueries.NearestPointBatch`, plus the distance), per tet a region id (connected component); a g3cli `tetmesh` command (JSON report: status, counts, quality, deviation) and its MCP tool; readers only if wanted (then `ReaderFuzzTests`) | 3-5 days, mid-size model | low | formats; readers yes / no; marker semantics (group id vs triangle id) |
| **P3 Size control** | a size function (uniform target edge length, user `Func<Vector3d, double>`, and an automatic one from the distance to the boundary with a grading factor) as an extra bad-tet criterion in stage 1; tet count against target as T7 of section 6; the boundary's size stays the wrap's (alpha) | about 1 week | medium: the floor keeps termination, but a size below the boundary's spacing is refused at the fixed surface (14.8), so grading near the boundary is limited by alpha | whether boundary sizing (needs P4's local alpha) is in scope |
| **P4 Fidelity beyond uniform alpha** | (a) a local alpha: the wrap refined finer only where the input has concave detail or thin parts (17.2 shows that is where the distance is), so fidelity without x4.7 tets everywhere; (b) S1-F crease recovery (section 14.7, decided earlier; needs conforming recovery of 2-8 % missing facets, section 13); (c) option (b) of section 4, input-conforming CDT for clean inputs | (a) 2-3 weeks, large model (a change in `MeshAlphaWrap`'s carving criterion and its guarantees); (b) 1-2 weeks; (c) 3-4 months | (a) medium-high (the wrap's separation and angle bounds with a variable alpha are open; `docs/roadmap/research.md` hard problem 4 names a locally smaller alpha as one of its two levers); (b) high (recovery near acute angles); (c) high | which of (a) / (b) / (c), after P1's numbers |
| **P5 Quality statement per run** | (a) a certified postcondition: `MinDihedralDegrees` / `MaxRadiusEdgeRatio` as targets with a result flag (target met or not, and the counts), checked on the output, not assumed; (b) a proven bound: only a lattice method gives one for arbitrary domains (option (d), isosurface stuffing), with an approximate boundary | (a) 1-2 days (part of P2); (b) 1-2 weeks | (a) low; (b) medium (new mesher, uniform or octree size) | whether a proof (b) is needed or the certified flag (a) is enough |

What a guarantee would need (item 3 of the task): our stage 1 has a radius-edge bound only away from the fixed surface (tets whose
cavity reaches it are refused and counted), and stage 2 is hill climbing, so neither gives a bound on 100 % of inputs.
Delaunay refinement proves radius-edge bounds only without acute input angles (Shewchuk 1998; TetGen's own condition is "no input
angle below about 60 degrees"), and real wrap surfaces have acute angles (10.1). Dihedral bounds are proven only for lattice methods
(Labelle and Shewchuk 2007: 10.7 to 164.8 degrees for the uniform lattice, by the abstract) and, tiny, for sliver exudation. So the
practical form is (a): measure and report per run, and keep the corpus evidence (P1) as the claim; the proof route is (b) and
gives up the conforming wrap boundary.

### 17.4 Recommended order

1. **P1** first: one default and one runner run close the floor part of gap 3 on the subset (40 of 40 at either candidate) and
   reduce gap 1 (alpha / 40: input-to-boundary max 0.81x at 1.05x the time; alpha / 80: 0.51x at 1.55x), and P1(c) answers gap 2
   for every later stage. It also decides whether the last-4 improver lever is still needed at the new default.
2. **P2** next (with P5(a)): the cheapest step that makes the mesher usable in an FEM chain; independent of P1's default.
3. **P3** interior size control.
4. **P4** only after P1's run shows how much of gap 1 is left at the chosen alpha; (a) local alpha is the lever the probe points to,
   (b) S1-F for CAD creases.
5. P5(b) (lattice) only if a proven bound is required.

### 17.5 Stage P1: go / no-go criteria (fixed before the build)

Measured on the 40-mesh subset (local, one thread, one process at a time) and on the runner sample (fixed id list committed with
the PR; meshes fetched at run time), each candidate default (alpha / 40 and alpha / 80, offset / 600), against master's default:

- **No-go if any** of: a mesh that master meshes validly is not valid (exact oracle, `TetFiles.Evaluate`); a boundary
  self-intersection; a `Similarity.All()`, reuse or determinism test fails; a test above the 5 s rule.
- **Go** for the coarsest candidate that meets all of:
  1. every tet >= 10 degrees on 40 of 40 of the subset and on >= 99 % of the sample's meshes with `Status == Success` (every
     sample mesh that master meshes with `Success` must also be `Success`, else no-go);
  2. input-to-boundary max: the median over the sample of the per-mesh ratio candidate / master <= 0.85 (at least a 15 %
     gain, the least worth an output change);
  3. time median <= 2x master's on the same runner; any mesh above 20 s gets an issue with a diagnosis (CLAUDE.md), and one above
     20 s whose input has fewer than 1M triangles is a no-go;
  4. tets median <= 2x fTetWild's on the subset (<= 29k).
  The thresholds were set after the subset was measured (17.2), so the subset does not decide: there both candidates meet 1-4
  (criterion 2: 0.81 for alpha / 40, 0.51 for / 80; 0.72 and 0.44 as ratios of the medians), alpha / 40 with little margin on
  criterion 2. The sample decides; if alpha / 40 lands within 0.80-0.85 there, the go goes to the owner with both candidates' rows.
- If neither candidate passes, P1 ships (b) and (c) only (the parameter documented with the measured table, the default kept) and
  the floor goes back to the improver session.

### 17.6 Prior art (papers and project pages only; what each implies for the plan)

- **fTetWild** (Hu, Schneider, Wang, Zorin, Panozzo 2020) and **TetWild** (Hu et al., SIGGRAPH 2018): an envelope of size epsilon
  around the input (default 1e-3 of the diagonal), not conforming; success near 100 % on Thingi10K, 8.5 s median here (section 12).
  Their fidelity comes from the envelope check on every operation, the cost we could not afford (section 3.4); our route to the
  same fidelity is resolution where needed (P4(a)).
- **TetGen** (Si, ACM TOMS 41(2), 2015; project page features list): constrained Delaunay + refinement; `-q` radius-edge target
  (default 2.0), a boundary-conforming Delaunay mesh if no input angle is acute ("in practice ... no input angle smaller than 60
  degree"), adaptive sizing by a volume bound or a nodal size map (`-a`, `-m`). No dihedral guarantee is stated. Supports P3's form
  (size map) and P5(a) (a target, reported).
- **CGAL Mesh_3** (Jamin, Alliez, Yvinec, Boissonnat, *CGALmesh*, ACM TOMS 41(4), 2015; manual text): restricted Delaunay with size
  and approximation criteria, protecting balls (weighted points) on 1D features for their representation and for termination,
  followed by an optimisation phase that removes slivers. The approximation criterion is the analogue of P1(b) / P4(a): fidelity as
  a user parameter that drives local resolution. (Paper only; no code read.)
- **Alliez, Cohen-Steiner, Yvinec, Desbrun, *Variational tetrahedral meshing*, SIGGRAPH 2005**: ODT-based optimisation with a sizing
  field graded from the boundary; its numbers were not re-read for this note (the publisher page was not reachable), so P3's
  grading factor is cited as the idea only.
- **Tournois, Wormser, Alliez, Desbrun 2009** (interleaving refinement and optimisation): best practical dihedral angles of the
  Delaunay family (section 3.3); not needed while P1 closes the floor on the subset.
- **Labelle, Shewchuk, *Isosurface stuffing*, SIGGRAPH 2007** (abstract): uniform lattice, dihedral angles between 10.7 and 164.8
  degrees (8.9 and 158.8 with other parameters), boundary approximates the isosurface; the only proven dihedral bound of the list,
  so the P5(b) route. The conditions of the graded version were not re-read here.
- Cheng, Dey, Shewchuk 2012 and Shewchuk 1998 (section 3.2): radius-edge bounds need non-acute input angles; the reason P5 states a
  certified result, not a proof.

### 17.7 Owner decisions

1. **P1 default**: a finer default alpha for `MeshTetrahedralization` (alpha / 40 or / 80, by 17.5 on the runner sample), an output
   change; and the runner run (about 500 meshes, about 4 runner-hours, planning).
2. **The last four meshes** (16.3 decision 2): wait for P1 before a new improver lever, since at alpha / 40 all 40 reach 10 degrees.
3. **Order** P1 -> P2 (+ P5(a)) -> P3 -> P4, or P2 first if file output is needed sooner.
4. **P2 scope**: formats (Medit, Gmsh 4.1 / 2.2, VTK), readers or not, boundary marker semantics.
5. **P4 choice** after P1: local alpha, S1-F creases, or option (b); P5(b) only if a proof is required.

### 17.8 Stage P1 result: the runner sample and the default (2026-11-17)

Run: `.github/workflows/tet-sample.yml`, run 44122189889 on 637b148d, 6 shards on GitHub-hosted runners (ubuntu-latest), all alphas of
a mesh on one runner, timeout 600 s per mesh and alpha (none reached). The 500 ids are `benchmarks/TetMeshBenchmark/compare/thingi-sample-ids.txt`:
every 20th id of the dataset's `metadata/file_ids.txt` from index 7, disjoint from the 40-mesh subset. Meshes were fetched at run time
and not kept. Raw rows and SUMMARY.md: data branch `runs/tet-meshing`, `sample/2026-11-17-run44122189889/`; the same rows are
`benchmarks/TetMeshBenchmark/results/thingi10k-sample500-alpha{20,40,80}-2026-11-17.csv`. The rule is applied by
`benchmarks/TetMeshBenchmark/compare/summarize_sample.py`.

| 500 meshes, offset diagonal / 600 | alpha / 20 (old default) | alpha / 40 | alpha / 80 |
|---|---:|---:|---:|
| Success, valid (exact), boundary without self-intersection | 500, 500, 500 | 500, 500, 500 | 500, 500, 500 |
| every tet >= 10 degrees | 461 (92.2 %) | 494 (**98.8 %**) | 499 (99.8 %) |
| worst smallest dihedral (degrees) | 3.78 | 7.12 | 9.25 |
| input to boundary, max, median (diagonal) | 0.0343 | 0.0228 | 0.0135 |
| per-mesh ratio against / 20, median: input to boundary max / time / tets | 1 | 0.77 / 1.22 / 1.69 | 0.47 / 2.03 / 4.74 |
| time median / max (s) | 0.64 / 8.6 | 0.83 / 9.8 | 1.41 / 11.7 |
| tets, median | 4.1k | 7.3k | 19.5k |

The rule of 17.5 gives no go for both candidates: alpha / 40 misses criterion 1 by one mesh (6 meshes below 10 degrees, 5 allowed by
99 %) and passes 2 and 3 (0.77 <= 0.85; median time 1.30x <= 2x); alpha / 80 passes 1 and 2 and misses 3 (2.22x the median time).
No run took more than 20 s. Criterion 4 (subset): 7.1k tets at the median at / 40, below 29k.

**Owner decision (2026-11-17), a documented exception to the rule of 17.5: the default of `MeshTetrahedralization.RelativeAlpha` is
diagonal / 40** (was 20; `RelativeOffset` stays 600; `MeshAlphaWrap`'s own default is unchanged). It misses the 99 % of criterion 1 by
one mesh and costs 1.30x the time and 1.7x the tets for 461 -> 494 meshes at 10 degrees and 0.77x the input-to-boundary distance. The
6 meshes left below 10 degrees at / 40 go to the parked improver lever (#1072): 138102 (7.12 degrees, 2976 input triangles), 763398
(9.15), 38781 (9.43), 282598 (9.53), 83719 (9.60), 50656 (9.88). Tests: `TetMeshingDefaultTests` (the default pinned, every `MaxThreads`
the same result, `Similarity.All()` with 2^k bit for bit at the default); `TetMeshingStage2Tests` runs at the default (reuse, and every
tet >= 10 degrees with the invariants on the procedural set); the mechanism tests of peeling, splits and the diagnosis keep alpha / 20 (`MechanismAlpha`), because at the finer default
the bad tets they act on are gone. The procedural inputs already reach 10 degrees at / 20, so the unit test that fails on the old
code is the pinned default; the behaviour change is evidenced by the sample.

### 17.9 Stage P2 (output and API) with P5(a): built (2026-11-18)

Scope as decided (owner 2026-11-18): additive, no change to `MeshTetrahedralization`'s result (`ExistingResult_IsBitForBitTheSameAsBeforeP2` pins a
fingerprint of the vertices, tets, boundary triangles and counts of the box input, computed on master before the change).

- **Writers** (`TetMeshWriter`, `mesh_ops/TetMeshExport.cs`; text, ASCII, vertices in the "R" round-trip form, `\n` line ends, culture independent):
  Medit `.mesh` (version 2: Vertices, Triangles, Tetrahedra, End), Gmsh `.msh` 4.1 and 2.2, VTK legacy `.vtk` (unstructured grid, cell types 10 and 5).
  Our tets are exactly positive in the sense of all four formats (the normal of (v0, v1, v2) by the right-hand rule points to v3), so vertex
  order is written unchanged; boundary triangles are outward. Indices are 1-based in Medit and Gmsh, 0-based in VTK. **Readers are not provided**
  (so no `ReaderFuzzTests` entry); the benchmark's readers in `TetFiles.cs` stay a tool.
- **Refs** (Medit reference, Gmsh physical and elementary tag, VTK cell data `ref`): tet = region id + 1, boundary triangle = marker + 1, so
  refs start at 1 and 0 means "unmarked" (no markers given, or a marker of -1). Gmsh 4.1 gets one volume entity per region and one surface
  entity per distinct boundary ref, physical tag = the ref; the entities have no bounding topology (empty lists); all nodes are in one block.
- **Marker semantics** (`TetMeshMarkers.Compute(tetMesh, input)`): per boundary triangle the nearest input triangle to its **centroid**
  (`MeshBatchQueries.NearestPointBatch`, the same result serial and parallel); the marker is that triangle's **group id if the input has
  triangle groups, else its triangle id**; the distance of the centroid is kept. The boundary is the offset wrap (distance about offset ..
  alpha from the input, 17.2), so a marker is a label for loads and supports, not a claim that the boundary triangle lies on the input
  triangle; at a crease or a thin part a boundary triangle can take the label of the nearer of two input patches. Per tet a **region id**: the
  connected component by shared facets, numbered by the lowest tet index. An input without triangles gives -1 and an infinite distance.
- **P5(a)** (`TetMeshQuality.CheckTargets`, `MeshTetrahedralization.Targets`, `TetTargetReport`): the two targets are the run's own parameters,
  `SliverDihedralDegrees` (default 10; 0 = off) as the minimum dihedral angle and `MaxRadiusEdgeRatio` (default 2) as the radius-edge bound; no new
  input parameter was added (the refinement and the report must not disagree about the target). The report counts the tets below / above, the worst
  values and the flags `MinDihedralMet`, `RadiusEdgeMet`, `Met`, measured on `Result` after stage 2. An off target is met; a tet that is not
  exactly positive misses both. The flag is a statement about this mesh, not a proof. Example, cube `[-1,1]^3`, defaults: 10820 tets, worst
  smallest dihedral 10.01 (`MinDihedralMet`), 135 tets above radius-edge 2 (worst 4.38; the tets that the fixed surface leaves, 14.8):
  `RadiusEdgeMet` is false, which is the honest result, as 17.1 row 3 says (7.3 % at alpha / 20, 2.5 % at / 40 on the subset).
- **g3cli** `tetmesh` and its MCP tool (`tools/g3cli/TetMeshCommand.cs`): `--format auto|medit|msh41|msh22|vtk`, `--relative-alpha`,
  `--relative-offset`, `--min-dihedral`, `--max-radius-edge`, `--max-circumradius`, `--max-steiner-points`, `--improve`, `--markers`,
  `--deviation`, `--self-intersections`; the JSON report has `tet_status`, `wrap_status`, `valid` (exact, `TetMeshQuality.NotValidReason`),
  `counts` (vertices, tets, boundary triangles, regions), `steps` (the library's counters), `quality`, `targets`, `markers` (kind, distinct count),
  `deviation` (centroid distances to the input, relative to the diagonal; `hausdorff` with `--deviation`) and the metrics of the input and of the
  boundary. `point_limit_reached` is a valid result with a warning; other statuses write nothing.
- **Observation for the owner** (not changed here): `MaxRadiusEdgeRatio` near 1 does not terminate in practical time (cube, alpha / 10: ratio 1.2 and
  1.5 run in 0.5 s, 1.0 had not finished after 120 s); the floor bounds the points only by `MaxSteinerPoints`. The CLI therefore accepts
  `--max-radius-edge` in [1.5, 1000]; the library parameter itself is unchanged. If such a value should be refused or capped in the library, that is a
  separate change (it alters `MeshTetrahedralization`'s behaviour); see 17.10 (issue #1138).
- Tests: `TetMeshExportTests` (parsers of the four formats written for the test, golden text of a two-tet mesh in each format, round trip of a real
  result: counts, 1-based indices, orientation of every tet, refs; markers against brute force and against the serial single queries; groups vs ids;
  regions; `Similarity.All()` on markers of the same mesh and 2^k scales on the whole pipeline bit for bit; targets against brute force and against
  `TetMeshQuality.Measure`; reuse) and `TetMeshCliTests` / `McpTests.TetMeshTool_*` (report, formats, errors, limits, help).

### 17.10 The refinement's work bound (issue #1138, 2026-11-19)

**Measurement** (`MeshTetrahedralization`, serial, one process, local; 25 s stopwatch as `CancelF`, `MaxSteinerPoints` 300000; inputs box, sphere,
thin torus, cylinder of `TetMeshingStage2Tests.Input`, `RelativeAlpha` 12 and 40; v0 = vertices of the wrap's triangulation). Per wrap vertex v0, the **default path (ratio 2)
needs at most 2.5 trial insertions** (0.5 splits), ratio 1.5 at most 5, and the slowest runs that do end (ratios 1.1 to 1.3) 7.2; **runs that do not end
reach 370 to 1400 trial insertions per v0 within 25 s and keep rising** (35 to 250 splits per v0), with `refused_surface` growing along (50k to 130k) and
`refused_floor` staying small (under 350): the cascade is new bad tets next to the fixed surface that each insertion creates. Where it ends (the box is `TetMeshingStage2Tests.Input("box")`, not the g3cli cube of 17.9, whose ratio-1.2 run at alpha / 10 ended in 0.5 s):

| ratio | box 12 | box 40 | sphere 12 / 40 | thin torus 12 / 40 | cylinder 12 | cylinder 40 |
|---|---|---|---|---|---|---|
| 2, 1.5 | ends | ends | ends | ends | ends | ends |
| 1.3 | **no** | ends | ends | ends | **no** | ends |
| 1.2 | no | no | ends | ends | no | no |
| 1.1 | no | no | ends | no | no | no |
| 1.0 | no | no | no | no | no | no |

So the limit is not a number: it depends on the shape and on alpha (the same cube ends at 1.3 for alpha / 40 and not for alpha / 12), and shapes with
flat faces and right angles (box, cylinder) fail first. This is the 1998 condition at work: radius-edge bounds below 2 need input angles that are not acute
or right (Shewchuk 1998; 17.6), and the floor (`MinFeatureFactor`) only bounds the count by the packing (millions), not in practical time.

**Option (a), built**: `MaxTrialInsertionsPerWrapVertex` (default 48): the refinement stops after 48 * v0 + 2000 trial insertions, with status
`PointLimitReached`, a valid result, `Counts["work_limit_reached"] = 1` (the key exists only then, so every other `Counts` stays as it was) and
`Targets` saying which target was missed. 48 is 19 times the default path's maximum (2.5) and 6 times the slowest run that ends (7.2); it costs 1.7 s
on the cylinder / 12 runaway (measured) and, at the measured trial rate of about 20 000 per second, 3 to 5 s on a 2000-vertex wrap. It is not used with a size bound (`MaxCircumradius` > 0, where the point count follows the
volume), 0, a negative value or NaN turn it off; a run with more candidate points per split (a larger `SliverDihedralDegrees`, up to 30 trials per split instead of about 5) reaches it sooner, which was not measured; `MaxSteinerPoints` still applies (reported as before, without the work key). **Default outputs unchanged, bit for bit**: the
vertices, tets, boundary triangles and counts of 16 runs (4 inputs x alpha 12 and 40 x ratio 2 and 1.5) hash to the same values on master and on the
branch (`DefaultPathAndRadiusEdge15_AreBitForBitTheSameAsBeforeTheBound` pins them). Tests: `TetRefinementWorkBoundTests` (stopwatch as `CancelF`, so the
old code fails them as `Cancelled`; runaways stop valid with the target missed, terminating runs are not cut, the default path stays 16 times below
the bound, the parameter, the size bound, reuse and `MaxThreads`, `Similarity.All()` with 2^k bit for bit).

**Option (b), not built (an input change, owner's decision)**: a documented lower limit on `MaxRadiusEdgeRatio`. The table gives the numbers: 1.5 ends on
all 8 runs here (4 inputs x alpha 12 and 40), 1.3 on 6 of 8, 1.2 on 4 of 8, 1.1 on 2 of 8, 1.0 on none; the g3cli `tetmesh` already refuses below 1.5. With (a) in place a library
limit is no longer needed for the hang, only for the quality of the answer (a run cut at the bound has many bad tets); the owner can choose 1.5 (all
measured runs end), 1.3 or none.

