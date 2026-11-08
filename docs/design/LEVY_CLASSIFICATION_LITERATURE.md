# Mesh-boolean classification: literature and prior-art notes

Prepared 2026-11-08 for the design note on patch classification in the geometry3Sharp exact mesh boolean.
Time-boxed (about 40 min). Papers were read in full text where reachable. Code was read only from the BSD-3 code
(Geogram) and the Apache-2.0 code (Manifold). No CGAL, libigl (MPL-2.0), Cherchi/mesh_booleans, Trueform, TetWild or
other excluded code was opened.

Access notes: `www.cs.toronto.edu`, `www.cs.columbia.edu`, `alecjacobson.com` and `www.graphics.rwth-aachen.de` do not
resolve in this sandbox (blocked by the network allowlist). The EMBER PDF was supplied by the owner and section 5 was
written from the full paper.

Our current design, for reference: exact arrangement, pieces, patches (connected across non-constraint edges), then one
exact ray per (patch, candidate closed mesh) → winding number; for open meshes a fast GWN (Barill 2018) per patch,
thresholded at 1/2.

---

## 1. Lévy 2024/2025, "Exact predicates, exact constructions and combinatorics for mesh CSG"

Citation: Bruno Lévy. *Exact Predicates, Exact Constructions and Combinatorics for Mesh CSG.* ACM Transactions on
Graphics 44(5), 2025 (published online 2025-07-10), DOI [10.1145/3744642](https://doi.org/10.1145/3744642)
(Crossref metadata checked). Preprint: arXiv:2405.12949, v1 2024-05-21, v2 2025-06-04,
<https://arxiv.org/abs/2405.12949>, HTML <https://arxiv.org/html/2405.12949v2>. The TOG version is the "2025
follow-up"; I found no separate follow-up paper.

Pipeline (Sec. 2): (1) exact co-refinement of the triangle soup (CDT per triangle, symbolic perturbation for
co-cyclic points, so overlapping coplanar zones get identical triangulations and duplicates can be filtered); (2)
**Weiler model** (a 3-map: every triangle is duplicated into two oppositely oriented darts linked by α3, and the
darts are sewn around every non-manifold "radial" edge by a radial sort); (3) "(mostly) combinatorial" classification.

### Radial sort (Sec. 2.2.2)
- Two predicates: `orient(h1,h2)` = `orient_3d` of the two radial-edge end points and the two opposite vertices;
  `Norient(h1,h2)` = `sign(n1 · n2)` (dot product of the facet normals).
- Relative to a reference dart h0, the sign pair (`orient(h0,h)`, `Norient(h0,h)`) puts every dart into one of four
  quadrants (eight regions if the zero cases count). Darts in different regions are ordered by region; darts in the
  same region are ordered by `orient(h1,h2)`.
- Normals are taken from the **original input triangle** (a sub-triangle is coplanar and co-oriented with its input
  triangle), so they are exact in plain double arithmetic before being promoted. The radial edge end points are
  exact intersection points, so sorting is the expensive part; Lévy amortises it: "the order around a radial edge
  remains the same when traversing polygonal lines incident to the same set of patches", i.e. one geometric sort per
  radial polyline, then the order is copied.
- Duplicates/coplanar: identical triangles are merged during co-refinement; the merged triangle gets the union of the
  operand sets ("B(t) = B(t1) ∪ B(t2)", in code an XOR, see 2). So the radial sort never sees two coincident darts;
  two darts in the same half-plane are reported as a degeneracy.

### Classification (Sec. 2.2.3)
- Every input triangle carries a bit vector B(t) (one bit per operand). Each operand's boundary is covered by two
  charts linked by α3: one "inside", one "outside" of the operand.
- Inclusion bits I(d) per dart: unchanged across σ1/α2 (same volumetric region), and across α3:
  `I' = (I ∩ ¬B(t)) ∪ (¬I ∩ B(t))` = `I xor B(t)`. This is **parity**, not a signed winding number.
- Output: dart d is kept iff `not E(I(d)) and E(I(α3(d)))`, for an arbitrary boolean expression E (n-ary CSG).
- Rays: "only one ray launching per connected component of the volumetric mesh is required" (i.e. per connected
  component of the 3-map, not per patch), to decide nesting of components. Along the ray, I is XOR-ed with B(t) of
  each crossed triangle. Degeneracies: SoS was tried, but the paper recommends the simpler approach that "keeps
  launching rays with random directions until no degeneracy is encountered". (The current Geogram code instead uses a
  fixed vertical segment with SoS predicates, see 2.)
- Outer chart of each component: the chart that "encloses the largest volume" (linear time; in code a floating-point
  signed volume).

### Open and self-intersecting operands
- The paper assumes closed volumes: "the surfaces we consider are supposed to define closed volumes, hence they are
  orientable." Open operands are not handled (in code a single-dart radial bundle is "hemmed" to its own α3 twin,
  so an open sheet becomes a zero-thickness shell; the comment says a tiny hole merges the two shells into one).
- Self-intersecting operands: no winding-number semantics. With parity bits a region covered twice by the same operand
  counts as outside (even-odd rule). The "repair"/self-union use case (Fig. 11) keeps the outer shells of the
  components not enclosed by others.

### Timings
- Table 3 (ms, approximate parse of a flattened table): radial sort about 2–23 ms, classification 1–241 ms on the
  examples; classification is about 9 % of the 2.7 s dragon-bunny total, negligible elsewhere.
- Table 2 (s; ours / Zhou 2016 / Cherchi 2022): dragon-bunny 2.7 / 3.9 / 1.3; buddha∪lion 11.5 / 16.1 / 5.8;
  cyl∪cyl 3.7 / 25.8 / 28.2; 20rods 0.9 / 3.3 / 2.1 (Cherchi incorrect). "Up to 6x" faster on coplanar cases.
  Thingi10K: 4523 models with intersections processed in 1 h 45 min.
- On EMBER: "the integer-based method EMBER is still spectacularly more efficient" (20rods in ≤ 4.5 ms),
  "50x to 100x".
- Stated limitations: no snap rounding; expansion-kernel overflow/underflow on cascaded constructions.

---

## 2. Geogram (BSD-3-Clause), `MeshSurfaceIntersection`

Read at `BrunoLevy/geogram` `main` = commit `07ebd2561485c57d1b02c73016e75b7ad492f59b` (2026-11-08):
- <https://github.com/BrunoLevy/geogram/blob/main/src/lib/geogram/mesh/mesh_surface_intersection.cpp> (2311 lines)
- <https://github.com/BrunoLevy/geogram/blob/main/src/lib/geogram/mesh/mesh_surface_intersection.h> (1332 lines)
- `mesh_surface_intersection_internal.{h,cpp}` (MeshInTriangle = per-triangle CDT, CoplanarFacets; not about
  classification)
- `mesh_repair.cpp` (`detect_bad_facets`, duplicate merging)

**Licence header confirmed**: "Copyright (c) 2000-2022 Inria. All rights reserved. Redistribution and use in source
and binary forms, with or without modification, are permitted provided that ..." with the three BSD clauses (third
clause names "the ALICE Project-Team"). This is BSD-3-Clause and fine to port from (attribution in `docs/SOURCES.md`).

### Structure (`mesh_surface_intersection.cpp`)
- `intersect()` = `intersect_prologue()` → `intersect_get_intersections()` → `intersect_remesh_intersections()` →
  `intersect_epilogue()`.
- `intersect_prologue()` (l. 224): if no `operand_bit` attribute exists, every surface connected component becomes an
  operand (`operand_bit = 1 << component`; the TODO notes the 32-component limit). Sets `PCK::SOS_LEXICO` so that
  coplanar facets get compatible triangulations.
- `intersect_epilogue()` (l. ~690–795): deletes the original intersected facets, then `mesh_remove_bad_facets_no_check`
  → `detect_bad_facets` (mesh_repair.cpp l. 340–417): facets with the same vertex set (orientation normalised away)
  are merged and `operand_bit[f1] ^= operand_bit[f2]` ("^= instead of |= because there can be fins in the input ...
  and ^= discards duplicated facets in fins"). **Orientation of the duplicate is ignored**: two opposite coincident
  facets of the same operand cancel. Then `build_Weiler_model()`.
- `build_Weiler_model()` (l. 1106):
  1. duplicate every facet with reversed orientation, `sew3` the twins (α3); remember `flipped`;
  2. clear facet adjacency;
  3. `RadialBundles::initialize()` (l. 1251): lexicographic sort of halfedges by (v0,v1) → bundles of halfedges with
     the same end points; `RadialPolylines::initialize()` (l. 1371): chain non-manifold bundles into polylines
     between non-manifold vertices;
  4. bundles with 1 halfedge (open border): `sew2(h, alpha3(h))` ("hem"); with 2 halfedges: plain manifold sew;
  5. `get_surface_connected_components(mesh_, "chart")` → charts;
  6. `RadialPolylines::radial_sort()` (l. 1460): `parallel_for_slice` over polylines; per polyline, sort one bundle
     geometrically (`RadialBundles::radial_sort` in the header, l. 990: `RS.init(first); std::sort(..., RS)`), check
     that adjacent charts are distinct, then copy the order to every other bundle with the same number of halfedges
     and the same sorted list of incident charts (`get_sorted_incident_charts`); otherwise sort again; on failure
     `geo_assert_not_reached` ("you may need geogramplus");
  7. sew α2 around each sorted bundle: `sew2(h, alpha3(h_next))`, `sew2(h_prev, alpha3(h))`;
  8. connected components again → "regions" stored in the `chart` attribute.
- `RadialSort` (l. 840–1010):
  - `init(h_ref)` caches the exact normal `N_ref_` of the reference halfedge's facet.
  - `operator()(h1,h2)`: `su = h_orient(h_ref,h)`, `sv = h_refNorient(h)`; quick exit if `su1*su2 < 0`; else map
    (sv,su) through the table `su_sv_to_linear_index[3][3] = {{5,4,3},{6,-1,2},{7,0,1}}` (pseudo-angle 0..7, -1 =
    impossible); different index → order; same odd index → `h_orient(h1,h2) > 0`; same even index or `o_12 == 0`
    → `report_problem` (sets `degenerate_`).
  - `h_orient(h1,h2)`: −orient3d(p1,p2,q1,q2) evaluated against the **original input facet** of h1 or h2
    (`get_initial_facet_vertices`, which swaps two vertices if the facet is a flipped twin): if the opposite vertex w1
    is an original vertex, `orient_3d(initial facet of h2, w1)` in double-precision filtered predicates; else if w2
    is original, `-orient_3d(initial facet of h1, w2)`; else exact `orient_3d` with the exact (homogeneous) point w2.
    ("Replacing a facet of a tetrahedron with a co-planar triangle of same orientation does not change signed
    volume.")
  - `normal(h)`: exact cross product of the original facet's edges (`exact::vec3`), `h_refNorient` = sign of exact dot.
- `classify(expr)` (l. 1628–1960):
  1. components = connected sets over α2 **and** α3 links (`facet_component`); per component one vertex, preferring an
     original (input) vertex;
  2. `chart_volume[c]` = Σ dot(p1, cross(p2,p3))/6 in **double**; per component the chart with the largest |volume|
     is the external shell; its sign gives a `flipped` flag;
  3. if `nb_components > 1`: `parallel_for` over components → `compute_component_inclusion_bits(component, v)`:
     segment from v to v + (0,0,1e6) (a finite segment, not a ray), tested with `segment_triangle_intersection_SOS`
     (l. 144: two `orient_3d_SOS` for the plane, three for the edges) against **every facet of the original mesh copy**
     (`mesh_copy_`, linear scan, no BVH), skipping facets of the same component and orphaned facets;
     `bits ^= operand_bit` per hit. If v is an intersection vertex, the same with exact points
     (`compute_component_inclusion_bits_exact`). With a single component **no ray is cast at all**;
  4. propagation: seed every facet of the external chart with the component's bits, flood fill:
     across α3 `bits(f2) = bits(f1) xor operand_bit(f1)`, across facet adjacency (α2) unchanged;
  5. per facet keep iff `E(bits(alpha3(f))) && !E(bits(f))` (or swapped if `flipped`); "intersection" special-cased
     as all bits set; parallel for > 1024 facets; delete the rest.
- Other utilities: `remove_external_shell()`, `remove_internal_shells()`, `mark_external_shell()` (same largest-volume
  rule), `remove_fins()`, `simplify_coplanar_facets()`; entry points `mesh_boolean_operation(result, A, B, op)` via
  `copy_operand` (sets `operand_bit = 1 << operand_id`) and `I.classify(operation)`.

### Observations relevant to us
- Rays are cast **per connected component of the volumetric model**, not per patch; everything else is a flood fill
  across α3 with XOR. Number of rays = number of components that may float inside others (often 1–2).
- Parity semantics (XOR), at most 32 operands, no winding number ≥ 2, no orientation-sensitive operations; an operand
  that overlaps itself is even-odd.
- The outer-shell choice uses a floating-point signed volume (not exact); the vertical segment has a fixed length
  1e6 (scale dependent; would break our 2^k scale-invariance tests and very large models).
- Reusable ideas: (a) orient3d against the *input* facet instead of the sub-triangle (cheaper and exact); (b) normals
  from input facets for the Norient predicate; (c) one geometric radial sort per radial polyline with combinatorial
  copying; (d) duplicate facets merged before the radial sort, so the comparator never sees ties.

---

## 3. Zhou, Grinspun, Zorin, Jacobson 2016, "Mesh arrangements for solid geometry"

Citation: Qingnan Zhou, Eitan Grinspun, Denis Zorin, Alec Jacobson. ACM TOG 35(4), Article 39, July 2016,
DOI [10.1145/2897824.2925901](https://doi.org/10.1145/2897824.2925901). Full text read from
<https://cims.nyu.edu/gcl/papers/zhou2016mas.pdf>.

- **Input class (Sec. 3.1)**: PWN meshes, i.e. the generalized winding number is a piecewise-constant integer off
  the mesh. Self-intersections, multiple components, nested shells, |w| > 1, seams ("combinatorially open boundary
  ... permissible so long as they meet up geometrically along seams"), duplicates and coplanar facets are allowed.
  Verification: after resolving self-intersections, "the total signed incidence of every edge in the result is zero".
  Not allowed: open boundaries and non-manifold flaps (Fig. 7). 18 % of Thingi10K failed this precondition (Sec. 8).
- **Extraction function** f(w) on the winding number vector (Sec. 3.2): union `∃i: wi ≠ 0`, intersection `∀i: wi ≠ 0`,
  minus `w1 ≠ 0 ∧ w2 = 0`; single-sided inequalities (wi > 0) give orientation-sensitive operations; complements by
  seeding w(∞) = 1.
- **Cells (Sec. 5.2)**: per connected component of the resolved mesh (triangles connected by edges), patches are
  grown across manifold edges; for each patch-patch incidence, triangles are sorted cyclically around one
  representative non-manifold edge, and the above/below cells of consecutive patches are equated (union-find over
  cell nodes). One sort per incident patch pair, "rather than at every edge".
- **Cyclic sort (Sec. 5.5.3)**: divide and conquer around edge {i,j}: relative to t0, split the others into
  (1) coplanar, same side; (2) coplanar, opposite side; (3) below; (4) above; recurse into (3)/(4); groups (1)/(2)
  (exact duplicates) are ordered by simulation of simplicity using a global index ε_k, **signed by the triangle's
  incidence on the edge**, so that the order of a duplicate stack is consistent around all three edges and reversed
  for the opposite edge orientation (Fig. 13). Zero-volume cells between duplicates get labels too and are removed at
  extraction (triangles with zero total signed occurrence).
- **Ambient cell per component (Sec. 5.5.2)**: no ray. Take the vertex v with maximal |x|; q = v + (1,0,0) is in the
  ambient cell; pick an incident convex-hull edge (max |ey/ex| in the xy-projection, exact quotients), sort facets
  around it, take the facet on the ambient side. (Notes that Attene 2014's "largest normal x-component" rule fails at
  non-manifold vertices, Fig. 12.)
- **Nesting (Sec. 5.2)**: for component Ki take a point p ∈ Ki; candidates are the components Kj whose ambient cell
  does not contain p, found by **point location** (Sec. 5.5.1: exact closest point c on Kj, then insert a dummy facet
  (e, q) into the cyclic order at the edge of c, or at an edge of t / a convex-hull edge of v); the closest candidate
  is the enclosing one; equate A(Ki) with L(Kj, p), else with the universal ambient cell C0. So one point-location
  query per (component, other component) pair, culled by bounding boxes.
- **Winding numbers (Sec. 5.3)**: purely combinatorial BFS on the cell-patch graph from C0 = [0,…,0]:
  `wn = wc + s_p · e_i` with s_p = ±1 by the patch orientation, e_i the unit vector of the patch's source mesh.
- Timings: resolving intersections dominates (Fig. 18). Cherchi 2022 later measured that the propagation step "does
  not scale well on very big meshes" (see 4).
- Open input (Sec. 8): "the high-level structure of our approach would clearly extend to meshes with boundaries ...
  via the generalized winding number ... However, our low-level combinatorial and sorting based subroutines would need
  to be replaced with robust or exact evaluations of the generalized winding number ... a robust solution for
  arbitrary geometrically open models is elusive."
- Implementation: CGAL exact kernel (we did not look at it; libigl is MPL-2.0 and also not read).

---

## 4. Cherchi, Pellacini, Attene, Livesu 2022, "Interactive and robust mesh booleans" (paper only)

Citation: ACM TOG 41(6), Article 248, Nov. 2022, DOI [10.1145/3550454.3555460](https://doi.org/10.1145/3550454.3555460);
arXiv:2205.14151 (2022-05-26), <https://arxiv.org/abs/2205.14151>. Code not read (licence excluded).

- **Input assumption (Sec. 3)**: "Input meshes are always assumed to unambiguously enclose a volume, that is, they
  are manifold, watertight and with no self-intersections." No winding numbers, no open meshes.
- **Classification (Sec. 5, Algorithm 1)**: **one ray per patch**; for each input mesh M, find the **first**
  intersection of the ray with M; inside iff the signed volume of tetrahedron (t, p∞) is negative. This first-hit
  orientation test is only valid for non-self-intersecting closed inputs; it is cheaper than counting all crossings
  but does not give a winding number.
- Ray definition (5.1): axis-aligned ray, p∞ = p shifted beyond the scene bounding box. Start point: an input vertex
  inside the patch if any; else a random triangle of the patch, float-rounded barycenter pushed "beneath" the triangle
  along the ray axis, verified with an exact 2D point-in-triangle test in the projection; retry with other triangles;
  rational-number fallback. Statistics: 3.8K booleans, > 80K rays; only 2 % needed the rounding path; never reached
  the rational fallback.
- Intersection (5.2): octree (the same as for the arrangement), axis-aligned → 2D point-in-triangle tests; hits are
  implicit LPI points sorted with an exact comparator.
- Degeneracies (5.3): if the ray hits an edge/vertex, p∞ is perturbed by `nextafter` until the hit is interior
  (then 3D test with three orient3d); "In no case we performed numerical perturbation" in the benchmark, one
  perturbation always sufficed in an artificial test.
- Parallelism: the paper parallelises the arrangement (TBB, work stealing); it does not state that ray casting is
  parallel ("We considered additional parallelization opportunities in patch construction and final mesh extraction
  ... without gaining speed up").
- Timings (Sec. 6): Thingi10K, 3814 booleans on 12 cores: Zhou/libigl 28.3 min vs. 4.5 min; "Boolean part" (labeling +
  extraction) 5.8 min vs. 0.47 min (12.2x); ray-casting classification "up to 101x faster than [Zhou 2016] in the
  best case (2.38x in the worst case, 11.34x on average)"; huge meshes (1.3–14.4 M triangles): about 53x faster
  on average for the classification, which "never exceeded the cost of the arrangement part". Interactive demo: 25K–
  200K triangles at 0.02–0.14 s per frame.

---

## 5. Trettner, Nehring-Wirxel, Kobbelt 2022, EMBER (paper read in full, PDF supplied by the owner)

Citation: Philip Trettner, Julius Nehring-Wirxel, Leif Kobbelt. *EMBER: Exact Mesh Booleans via Efficient & Robust
Local Arrangements.* ACM TOG 41(4), Article 39, July 2022, DOI
[10.1145/3528223.3530181](https://doi.org/10.1145/3528223.3530181),
<https://www.graphics.rwth-aachen.de/media/papers/339/ember_exact_mesh_booleans_via_efficient_and_robust_local_arrangements.pdf>.
Paper only (no code is public; productised as Solidean by Shaped Code GmbH). Licence of the paper: standard ACM
copyright form, not open access ("© 2022 Copyright held by the owner/author(s). Publication rights licensed to ACM";
Crossref licence URL: the ACM copyright policy): personal and classroom use, no redistribution. Copyright covers the
text and figures, not the method, so implementing from the paper is allowed under our rules; the PDF must not be
committed and text or figures are not copied (this note paraphrases). The copyright notice says nothing about
patents, which are a separate question (below).

- **Representation** (Sec. 3): plane-based geometry with fixed-width homogeneous integer coordinates (Nehring-Wirxel et
  al. 2021); input quantized to 26-bit integers (lossy for float input: about 15 nm on a 1 m scene). A segment between
  two homogeneous points is in general not representable, so a classification path is built from at most three
  segments by swapping one defining plane at a time (Sec. 3.2, 4.4, Fig. 9).
- **Input** (Sec. 3.4): the inputs must induce a piecewise-constant integer generalized winding number (PWN, as Zhou
  2016): self-intersecting, non-manifold and degenerate closed inputs are fine; **open inputs are not supported**.
- **Winding number transition vectors** (Sec. 3.4): each input polygon t carries Δw_t (unit vector of its mesh); a
  segment trace from x to y gives w_y = w_x + Σ_{t∈T} sign<n_t, x−y> Δw_t; for a target y on a surface, the polygons
  containing y are removed from T and give w_F / w_B depending on the side the segment arrives from.
- **Classification by local reference points** (Sec. 4.1, 4.2.2, Fig. 4): an adaptive kd-type recursive subdivision of
  the scene box (split at the centre of gravity along the axis of largest variance, Sec. 4.5.3; leaves below about 25
  polygons). Every subproblem carries a reference point x_ref with known winding vector; when x_ref falls outside a
  child box, a new one is made by projecting the old one onto the child's box and tracing a (usually axis-aligned)
  segment from the old one, testing only the polygons of the parent subproblem. The root's reference point is outside
  all inputs (w = 0).
- **Leaf** (Sec. 4.3, 4.4, Fig. 5): per polygon a local BSP of the intersection segments (no retriangulation; overlap
  regions are disabled for all but the lowest-index polygon); every leaf polygon is classified by a path from x_ref to
  an interior point (fast path: rounded centroid plus an axis-aligned line, at most 3 segments, clipped to the box),
  testing only the leaf's polygons. Cost per classification is therefore O(leaf size), not O(sqrt(n)) like a global ray.
- **Early outs** (Sec. 4.5.1, 4.5.2, Fig. 11, 12): if a subproblem holds a single WNTV class marked self-intersection
  free and without nested components, one classification serves all its polygons; a subproblem whose reachable
  winding vectors can only map to (OUT, OUT) or (IN, IN) under the operator is discarded without subdivision. Ablation
  (Fig. 17): the two largest gains are the classify fast path and the subdivision early out.
- **Profile** (Sec. 5.2, Fig. 16): leaves take about twice the subdivision time; within leaves, classification
  (segment traces) costs about twice as much as intersection plus local BSP. Updating the reference winding vector
  "barely shows up". Timings: 1000 Thingi10K pairs (1k-100k faces, solid, manifold, self-intersection free), geometric
  mean 1.6 ms per boolean on 8 cores (i9-9900K), Mesh Arrangements 8.6 s, QuickCSG about 18 ms.
- **Relevance for us**: EMBER's classification is a property of its spatial subdivision (local reference points per
  cell), not a step that can be grafted onto a global arrangement; our analogue would be to classify each patch by a
  short segment to a nearby point of known winding instead of a ray to the far box corner. Like Lévy and Zhou it does
  not handle open inputs.
- **Patents**: the paper names none. Web searches for Trettner / Kobbelt / Nehring-Wirxel / RWTH / Shaped Code /
  Solidean with "patent" found no patent or application; Google Patents, Espacenet and DPMA were not queried directly,
  and applications publish about 18 months after filing, so this is "no indication found", not clearance. Before any
  EMBER-specific mechanism (local reference points in a subdivision) is built, the owner should have a proper patent
  search done. One unrelated hit: US 8,854,367 (CAD booleans of closed triangulated surfaces), not analysed.

---

## 6. Manifold (Apache-2.0), elalish/manifold

Read at `elalish/manifold` `master` = commit `f286a2705dcb2058113738b6dc313140fb26b737`:
<https://github.com/elalish/manifold/blob/master/src/boolean3.cpp>, `src/boolean_result.cpp`. Header: "Copyright 2021
The Manifold Authors. Licensed under the Apache License, Version 2.0".

- Assumes manifold, closed (oriented 2-manifold) inputs; floating-point with a symbolic perturbation (current code:
  vertices "moved" by a fraction of the tolerance, expand P for union, contract for difference/intersection) instead
  of exact arithmetic. Based on Julian Smith's approach (Smith & Dodgson 2007, CAD 39, "A topologically robust
  algorithm for Boolean operations on polyhedral shapes using approximate arithmetic").
- Kernels: `Shadow01` (vertex vs. edge in projection), `Kernel11` (edge-edge, in the xy projection, "shadows"),
  `Kernel02` (vertex vs. face: does the face lie above the vertex along +z, returning a signed count s02 and the z
  of the face), `Kernel12` (edge vs. face crossing, built from Kernel02 and Kernel11; its sign is the crossing
  direction x12).
- **Winding numbers** (`Winding03_`, boolean3.cpp l. 563): union-find over the vertices of P joined by every edge
  that is **not** crossed by Q (`p1q2` list); for each component's root vertex, a vertical query box collides with
  Q's BVH, and `w03[root] += s02` for every face of Q above it (signed ray crossing count along +z = winding number
  of the vertex w.r.t. Q); then flood fill to all vertices of the component. So: **one vertical ray per connected
  piece of P cut by Q**, plus the signed edge-face crossings x12 for the new vertices. Parallel (TBB) and
  deterministic (integer sums).
- Inclusion (`Boolean3::Result`, boolean_result.cpp l. ~709): `c1 = op==Intersect ? 0 : 1`, `c2 = op==Add ? 1 : 0`,
  `c3 = op==Intersect ? 1 : -1`; `i03 = c1 + c3·w03`, `i30 = c2 + c3·w30`, `i12 = c3·x12`; |i| = number of copies of a
  vertex in the output, sign = orientation. This is a winding number, not parity.
- `Manifold::Impl::PointWinding` (l. 741) reuses Kernel02 for point queries ("Points outside the bounding box have
  winding 0 for any closed manifold").

---

## 7. Open / non-watertight inputs: generalized winding number

- Jacobson, Kavan, Sorkine-Hornung 2013, *Robust inside-outside segmentation using generalized winding numbers*,
  ACM TOG 32(4), Article 33, DOI [10.1145/2461912.2461916](https://doi.org/10.1145/2461912.2461916).
  w(p) = (1/4π) Σ Ω_t(p): harmonic off the surface; integer and piecewise constant only for closed (PWN) inputs; across
  any triangle interior it jumps by exactly ±1 (the solid angle of a triangle jumps by 4π across it), independent of
  holes elsewhere. Used with a graph cut on a CDT tetrahedralization (data term from w, smoothness term), because
  thresholding alone is not robust for very open input.
- Barill, Dickson, Schmidt, Levin, Jacobson 2018, *Fast winding numbers for soups and clouds*, ACM TOG 37(4),
  Article 43, DOI [10.1145/3197517.3201337](https://doi.org/10.1145/3197517.3201337). Tree-based far-field
  (dipole/Taylor) approximation with an accuracy parameter β (β = 2 recommended), exact summation for near-field
  triangles; no rigorous error bound and no sign guarantee (from memory, paper not re-read here).
- Consequences for our per-patch GWN (my analysis, not from a paper):
  * Evaluated at a point **on** the patch, the solid angle of the patch's own triangle is 0, so w_on = (w_+ + w_−)/2,
    the average of both sides; the two sides are w_on ± 1/2 per coincident sheet. A threshold at 1/2 on w_on is
    the same as "w_+ ≥ 1 or w_− ≥ 1" only when the side values are integers; for open input they are not.
  * w w.r.t. an operand that does **not** contain the patch is continuous across the patch (except for coplanar
    overlaps), so a sample point on the patch is valid; but it varies **along** the patch for open input (harmonic,
    not constant), so one sample per patch is a choice, not exact. Near an open boundary w is a smooth function, so
    thresholding can split a patch in reality while we give it one label.
  * Within a closed-and-cut arrangement, the jumps across intersection curves are exactly ±1 per crossed sheet, so a
    combinatorial propagation (Zhou/Lévy) is exact for the "ray-count" part of w but not for the boundary part.
- Decomposition GWN = signed ray crossing count + boundary term:
  * Martens & Bessmeltsev, *One-Shot Method for Computing Generalized Winding Numbers*, Computer Graphics Forum 44,
    e70194 (2025); arXiv:2408.04466 (2024-08-08, v2 2025-09-12), <https://arxiv.org/abs/2408.04466>.
    w(p) = (1/4π) Σ_i Area(A_i) χ_i over the regions A_i into which the projection of ∂M cuts the sphere around p;
    χ (signed ray crossings) is constant on a region and changes by exactly 1 across the boundary projection
    (Lemma 5.1), so one ray plus a BFS over the regions suffices; region areas from Gauss–Bonnet on the projected
    boundary (for meshes 2π − Σ(α_i − π)). For a closed mesh ∂M = ∅ and w = χ of one ray, exactly our current
    exact-ray path.
  * Martens, Trettner, Bessmeltsev, *The Antipodal Method: Fast, Accurate, and Robust 3D Generalized Winding Numbers*,
    arXiv:2605.01536 (2026-05-02), <https://arxiv.org/abs/2605.01536>: splits the GWN into "a signed count of
    ray-surface intersections and a boundary integral over the surface's projection onto the unit sphere"; reports
    about 22x over precise CPU methods and about 3x over the fastest approximate (Barill) method, full precision.
  * Interpretation for us: for an open operand, w(p) = χ_exact(ray) + B(p), where B depends only on the boundary
    curves (open edges) of the operand and the chosen ray direction. The integer part can stay exact and be
    propagated combinatorially; only B needs floating point, and it is cheap (boundary edges only). This gives a
    principled hybrid: exact crossings, approximate boundary term, with an error that is easy to bound because B is a
    sum over boundary edges only.
- Graph-cut alternative: Diazzi & Attene 2021, *Convex polyhedral meshing for robust solid modeling*, ACM TOG 40(6),
  Article 259, DOI [10.1145/3478513.3480564](https://doi.org/10.1145/3478513.3480564), arXiv:2109.14434. Exact BSP of
  the input planes; cells labelled in/out by a min cut on the cell dual graph: data cost = area of black (input-
  covered) facets whose constraint normal disagrees with the label, smooth cost = area of white (uncovered) facets
  between differently labelled cells; for closed oriented input both costs vanish. Their Fig. 12 shows a case where
  GWN "do[es] not capture the design intent" (TetWild with ε = 0) and the area-minimising cut does. Needs a volumetric
  cell complex; with our Weiler-style cells (after a radial sort) it would apply to cells, not patches.
- Feng, Gillespie, Crane 2023, *Winding Numbers on Discrete Surfaces*, ACM TOG 42(4), DOI 10.1145/3592401: jump Laplace
  formulation for curves on surfaces; related in spirit (jump conditions), not directly applicable to 3D solids.
- I found no paper that propagates GWN combinatorially across the intersection curves of a full 3D arrangement for open
  meshes; Zhou 2016 explicitly calls it "elusive". The ray-count + boundary-term decomposition above is the closest.

---

## Summary table

| Method | Unit that gets a ray / seed | Rays | Propagation | Semantics | Open input | Self-intersecting input |
|---|---|---|---|---|---|---|
| Lévy 2025 / Geogram | connected component of the 3-map | 1 per component (0 if one component); vertical SoS segment in code, random directions in paper | flood fill, XOR operand bits across α3 | parity, ≤ 32 operands | no (hemmed into zero-thickness shell) | even-odd only |
| Zhou 2016 | connected component | none; max-x vertex + convex-hull edge sort; point location for nesting | BFS on cell-patch graph, ±1 per patch | winding vector, arbitrary f(w) | no (PWN required) | yes (PWN) |
| Cherchi 2022 | patch | 1 per (patch, input mesh), first hit | none | inside/outside | no | no |
| EMBER 2022 | reference point per kd-subdivision cell (root outside, w = 0) | short segment traces (≤ 3 segments) per child cell and per leaf polygon, against local polygons only | hierarchical | winding vector, arbitrary f(w) | no (PWN required) | yes (PWN) |
| Manifold | connected piece of P not cut by Q (vertex sets) | 1 vertical per piece, signed count | union-find flood fill | winding number | no (manifold) | — |
| Ours | patch | 1 per (patch, closed candidate) | none | winding ≥ 1 | GWN per patch (Barill), threshold 1/2 | yes |
