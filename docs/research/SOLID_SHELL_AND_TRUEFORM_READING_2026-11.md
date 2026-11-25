# Reading study: Solid-Shell Labeling (SIGGRAPH Asia 2025) and trueform (2025 / 2026) against our open-input problems (2026-11-25)

Status: reading study, owner approved 2026-11-25. No library code, no probe. The papers were read; no source code of either
project was read (section 1 says why for each). Numbers in sections 2 and 3 are the papers' own; everything marked
*(judgement)* is this note's reading, not a measurement.

The problems the papers are measured against:

- **Sheet or shell** ([SHEET_EXTENSION.md](../design/SHEET_EXTENSION.md), sections 2, 6, 7): an open tool of a Difference can be a
  cutting sheet or a body with a missing face. Shape does not separate them (a dome of half angle 60 degrees has a cap share of
  0.43, a shallow tray 0.42). So the extension is limited to sheets the caller states in `InputRoles`. The pipeline still routes an
  unstated open tool to the sheet role first, and after a refusal it falls back to the solid route, whose repair can cap the tool
  ([guide](../guides/booleans-and-imprint.md#the-route-of-an-open-input-reportroute)).
- **Open and non-manifold inputs of the exact Boolean**: `AllowOpenMeshes` (generalized winding number above 1/2),
  `InputRole.Sheet` with its exact cells (`mesh_ops/ExactMeshCells.cs`), `ConflictEdges`, `OpenInputsWithoutEffect`.
- **Silent failures of `MeshRobustRepair`**:
  - #1548: an unwelded soup with flipped triangles solidifies to a clean but wrong solid (-12 %, or empty).
  - #1566: a nested outward shell becomes a cavity (-51 %), with `RepairVolumeIoU` 1.
  - #1575: a hollow alpha wrap (1.5 % of the volume) is returned as a clean success after 50 s on 2k triangles.
  - By design, the repair drops interior walls ([ROBUST_REPAIR_AREA_LOSS_2026-11.md](ROBUST_REPAIR_AREA_LOSS_2026-11.md)).
- **Rounding to doubles**: `SafeRounding` / `MeshRoundingRepair`. The exact result is rounded, the edits stay within
  `RoundingMaxDisplacement` (1e-5 of the diagonal), and the result is verified exactly (`RoundingClean`, `StrictRounding`;
  [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md)). When a run fails, the pipeline retries it once on a grid of 2^12 ulp
  ([PIPELINE_GRID_STEP.md](../design/PIPELINE_GRID_STEP.md)).

## 0. Answer in short

| | Solid-Shell Labeling | trueform |
|---|---|---|
| Licence of the code | MIT (`rachael-wang/SolidShellLabeling`). Its dependencies include pymeshlab (GPL-3.0, from memory of that project's licence) and PyTorch. | Dual: PolyForm Noncommercial 1.0.0, or a paid commercial licence (`polydera/trueform`). Not MIT / Apache / BSD, so source not read; may not be ported. |
| What it is | A per-face soft label solid / shell, by optimization of a winding-number energy with user hints. Floating point, approximate, not a Boolean. | An arrangement and Boolean on an integer grid with exact predicates. Topology is kept by identities and by majority votes. The output in doubles is not guaranteed free of self-intersections. |
| (a) sheet or shell | **Not.** The paper says outright that this is semantics, not geometry (Fig. 2, section 1), and it answers with user hints. That supports our decision (stated roles), and gives nothing for auto-routing. | **Not.** Sheets are declared, as with us. An undeclared open patch separates nothing (a "fin"). It is never capped and never guessed. |
| (b) open, non-manifold in the Boolean | **Not.** Approximate, and it needs a tetrahedral mesher and an offset mesher downstream. | **Partly.** Two ideas worth knowing: the radial order around an intersection edge from the original input planes, and the "open patch = fin" policy. Neither fixes a defect we have. |
| (c) #1548 / #1566 / #1575 | **Not.** It assumes correct face orientation (section 3). The orientation variant it names as future work is the GWN Dirichlet-energy idea of Takayama et al. 2014, which our Z2 study already set aside. | **Not.** Its orientation vote is an area majority (we have `AreaMajority`). Its nesting is by ray parity, the reading that causes #1566. It has no wrap. |
| (d) rounding | n/a | **Not.** It quantizes the input to int32 / int64 and rounds constructed points in the build. It guarantees the combinatorics, not the coordinates (section 5.1). Our quality bar (exact on input doubles, verified clean output) is stronger; the owner rejected quantized kernels on 2026-11-19. |
| (e) reference for checks | Not useful (it labels; it is not a Boolean). | **The one candidate for a spike, if the licence allows it.** It has open operands (sheets), soups and non-manifold input, which Manifold lacks. Whether our use counts as noncommercial is a question for the owner. |

**Recommendation** *(judgement)*:

- No method of either paper should be built.
- One spike is worth it, only if the owner first confirms that running trueform locally falls under PolyForm Noncommercial. Its
  question: *on our procedural sheet, fin, near-coplanar and nested-shell cases, does trueform's sheet result agree with
  `ExactMeshBoolean` with `InputRole.Sheet`, case by case? And where they differ, which one is right, by our exact checks?*
- One upper-bound check, of a few hours and with no licence question: the share of `ExactMeshBoolean.radial_order` in the
  Boolean's time. It decides whether the plane-based radial order of trueform section 2.3 is worth a design.
  *Done (2026-11-25): closed.* Measured serially on 135 Thingi10K pairs, the procedural unions, and constructed worst cases (a checkerboard of cubes touching along edges, one edge with 1 024 faces): `radial_order` runs only where the kept result touches itself along an edge (never in the ordinary Boolean runs), and takes at most 1.5 % of the Boolean's time even in the worst constructed case, far below the 10 % of the stop rule. The plane-based radial order is not built.

## 1. What was read, and access

| Text | Access |
|---|---|
| S. Wang, J. Meny, I. Grguric, M. Rahimzadeh, D. Zorin, D. Panozzo, H.-T. D. Liu, *Solid-Shell Labeling for Discrete Surfaces*, SIGGRAPH Asia 2025 Conference Papers, DOI 10.1145/3757377.3763847 | Read in full: the author's PDF (rachael-wang.github.io, 9 pages). The ACM DL page was not opened. |
| Code: github.com/rachael-wang/SolidShellLabeling | Licence file and README read (MIT; Python, PyTorch, libigl, polyscope, pymeshlab, meshio). No project page found. **Source not read**: the brief needs none, and no porting is proposed. |
| Z. Sajovic, D. Knez, *trueform: fast and robust mesh CSG via topological aggregation*, arXiv 2607.15905v1, 17 Jul 2026 | Read in full (22 pages incl. appendix A). |
| Z. Sajovic, D. Knez, *trueForm: Real-Time Mesh Booleans that Commute with Mesh Idealization*, TechRxiv preprint, DOI 10.36227/techrxiv.174667714.42575478/v1, 8 May 2025 | **Full text not accessible**: TechRxiv returned a Cloudflare challenge or HTTP 403 to every request. Only the abstract was read (Crossref metadata), plus the project's research page (trueform.pages.dev/py/about/research). Statements about this paper below rest on the abstract alone. |
| Code: github.com/polydera/trueform | Read only `LICENSE`, `COMMERCIAL.md`, the head of `LICENSE.noncommercial` and the README (benchmarks, licence section). **No source read**: the licence is not MIT / Apache / BSD. |

**trueform licence, exactly.** `LICENSE`: "This project is distributed under a dual-license model maintained by XLAB.
Noncommercial use is covered by the PolyForm Noncommercial License 1.0.0 ... Commercial use requires a separate paid agreement
with XLAB." `COMMERCIAL.md`: "available for evaluation, personal, and educational work under the PolyForm Noncommercial License
1.0.0. Any revenue-generating, production, or for-profit deployment requires a commercial agreement with XLAB." GitHub reports the
licence as "Other" (NOASSERTION). The arXiv paper says the code is "freely available for academic and research use" (section 1.2).
PolyForm Noncommercial is not a copyleft licence. It is a source-available licence that permits only noncommercial purposes.
`docs/sources/README.md` allows porting only from Boost, MIT, BSD and Apache-2.0, so trueform's code may not be used or ported.
Its papers may be read and implemented from, like any paper; the licence's patent grant covers only its own permitted uses. No
patent notice was seen in the README.

## 2. Solid-Shell Labeling (Wang et al. 2025)

**Problem, input, output.**
- Artist meshes "often do not have a well defined interior". They mix solid elements (faces that bound a volume) with shell
  elements (the medial surface of a thin shell). Section 1, Fig. 1.
- Input: any triangle mesh; "no constraints on the input topology ... non-manifold elements or even a soup of triangles". But it
  assumes "that each face has the orientation intended by the artist" (section 3).
- Output: a soft label x_f in [0, 1] per face (0 shell, 1 solid), thresholded at 0.5 (Fig. 8) or 0.8 (Fig. 9).
- Downstream, outside the method: solid faces go to TetWild, shell faces to topological offsets (thickness 1 % of the longest box
  edge), and the two closed surfaces are united with the Boolean of Cherchi et al. 2022 (section 3.2).
- Big triangles that slice through several regions are first split by a mesh arrangement (Campen and Kobbelt 2010, or Guo and Fu
  2024; section 3.2).

**Method.** Three terms are minimized over x (eq. 1, alpha = 10^3, beta = 10):
- A *binary winding number* term: E_bwn = mean over samples p of w(p, x)^2 (1 - w(p, x))^2. Here w(p, x) = (1 / 4 pi) times
  the sum over faces of x_f Omega_f(p), a winding number weighted by the labels. Its minima are fields that are 0 or 1 everywhere
  (eqs. 2, 3; after Du et al. 2021).
- A *user* term on a sparse set of points q with a target winding number in {0, 1}: the mean of (w(q, x) - target)^k, with k = 4
  (eq. 4; k = 2 does not converge, Fig. 15).
- An area-weighted *smoothness* term over faces that share an edge (eq. 5).

Implementation details (section 3.2):
- PyTorch with Adam.
- 64^3 samples within 10 % of the longest box edge of the surface.
- The solid angle matrix is cached.
- 500 to 5000 iterations; initialization x = 0.5.

The authors state the limits of what geometry can decide:
- "whether a shape is a volumetric thin shell or a solid depends on the semantics ... instead of purely relying on geometric
  information" (section 1, Fig. 2: an eggshell and a marble from the same geometry).
- With every hint outside, the unique minimum is all shell (section 4).
- All shell is also a minimum without hints: initialized at 0, x stays there (section 4.3, "Initialization").
- Different initializations "may converge to different results" (Fig. 16).
- Fig. 7: a bunny with 10 % of its faces removed needs one inside hint to be labelled fully solid.

**Guarantees.** None stated. It is a non-convex continuous optimization in floating point on a GPU. There is no exactness, no
statement on determinism, and convergence is "determined by the gradient norm". Failure modes it admits: dependence on the
initialization (Fig. 16); slow convergence when beta is too large (Fig. 14, runs capped at 2 minutes); a wrong labelling when the
orientation is wrong (section 5).

**Cost** (Table 1, Spot mesh, Apple M4 Pro GPU):

| faces | init | per iteration | to convergence |
|---|---|---|---|
| 5K | 1.7 s | 8.3 ms | 14.7 s |
| 10K | 3.0 s | 12.7 ms | 21.8 s |
| 20K | 8.5 s | 32.7 ms | 48.2 s |
| 40K | 32.7 s | 91.6 ms | 122.1 s |

User effort (Table 2): 1 to 12 hint points per benchmark model (cartoon car 3, display cloche 1, grandfather clock 4,
helicopter 4, pirate ship 8, piano 12, tank 7).

**Mapping onto our problems.**

- **(a) Sheet or shell. Not.** The paper reaches our conclusion of SHEET_EXTENSION.md section 7 independently: the label is intent.
  Its tool is a set of hint points, a finer form of the same information `InputRoles` carries.
  - *(judgement)* Without hints the energy cannot separate our two cases either. A dome sheet and a tray with a missing face both
    give a fractional winding field near their opening. E_bwn pushes both towards "shell" or towards a closure the energy finds
    cheaper, and that is the same ambiguity as the cap share of 0.43 against 0.42.
  - A hint point ("this point is inside the tool") would be an API alternative to `InputRoles` for a tool that is partly sheet and
    partly solid. We have no such input today. Nothing in our issues asks for it.
- **(b) Open inputs of the exact Boolean. Not.** The method is approximate end to end: the labels, then TetWild (an envelope
  approximation), then an offset that invents a shell of chosen thickness. That is the "invented surface" our fallback is
  criticized for, made systematic. As a pre-pass of `AllowOpenMeshes`, labels in [0, 1] would replace our exact rule "winding
  number above 1/2" with an optimized weighting. The weighting is not reproducible bit for bit and has no invariance under
  `Similarity.All()` beyond what Adam in doubles happens to give.
- **(c) `MeshRobustRepair`. Not.**
  - #1548 (flips): the method assumes correct orientation. The extension it names (labels in [-1, 1], section 5) is in substance
    the orientation by a winding-number energy of K. Takayama, A. Jacobson, L. Kavan, O. Sorkine-Hornung (arXiv 1406.5431,
    2014). [HIGHER_MATH_STUDY_2026-11.md](HIGHER_MATH_STUDY_2026-11.md) section 3 lists that work and its own caveat. That study
    also measured that on #1548 a weld by position plus `MeshRepairOrientation` is already exact (volume share 1.000). So #1548
    is the repair's choice of route and reference, not a missing orientation method.
  - #1566 (nesting) *(judgement)*: E_bwn is 0 both for "inner shell ignored" (x = 0 on it, the union) and for "inner shell
    reversed" (the cavity, with signed labels). The ambiguity of the issue stays.
  - #1575 (hollow wrap): the wrap goes hollow because of holes plus flips. The labels do not fix either cause.
  - The interior walls that the repair drops by design are exactly what the paper's default keeps ("preserves nested interior
    structures", section 3.1). That is the other semantics, for simulation assets, and not a defect of ours.
- **(d)** Not applicable.
- **(e) Reference. Not useful.** It produces labels, not a Boolean. Its pipeline (TetWild, offsets, a Boolean, pymeshlab) is a
  stack of approximate tools.

**Licence and porting.** The code is MIT, so it could be ported; the equations of section 3.1 can be implemented from the paper.
Nothing is proposed.

**Verdict.** (a) not, (b) not, (c) not, (d) n/a, (e) not. Worth citing as prior art for the decision that sheet or shell is intent
(SHEET_EXTENSION.md section 7). Open question, no spike: would a caller-facing hint point ever be wanted instead of a role per
input? That is an owner question, not a research one.

## 3. trueform (Sajovic, Knez 2025 / 2026)

### 3.1 The 2025 TechRxiv preprint (abstract only)

The abstract makes three claims:
- *closure equivalence*: "closable meshes - open with a well-defined closure - can be operated on without having to perform the
  closure";
- *commutative correctness*: "non-idealized mesh properties - such as geometric inconsistency and non-closable non-manifold
  flaps - are treated as stochastic noise over the underlying closable meshes";
- a rate of 6.5e7 polygons per second on 12 threads, evaluated on Thingi10K and the authors' production workflows.

The project's research page adds: "Chain operations freely on non-ideal meshes and clean up once at the end".

It cannot be checked from the abstract what "a well-defined closure" means. It would be the part most relevant to our sheet or
shell question. *(judgement)* The 2026 arXiv paper, which describes the current pipeline in full, does not use the term. It says
instead that an open component that is not declared a sheet "does not separate two volumes ... we always fuse them into one
domain" (section 2.3). If that is the current behaviour, a body with a missing face is not closed by any rule there either.
Open question for a spike (section 4): what does the library do today with a closed box with one face removed?

### 3.2 The 2026 arXiv paper

**Problem.** Mesh CSG output is consumed in floating point, and materialisation (rounding to a representable position) can create
crossings and orderings the exact result never had. "Only index-based topology survives materialisation". So the method keeps the
topology exact and treats every geometric reading as an observation of an intended geometry (abstract, section 1).

**Input.**
- Convex polygons ("non-convex inputs ... are triangulated upstream", section 5.1).
- Several meshes or one polygon soup (self-arrangement).
- Non-manifold flaps, T-junctions and isolated edges are "first-class" (section 1).
- Open surfaces either as declared *sheets* or as undeclared open patches (sections 2.3, 2.6).
- Coordinates beyond the int64 grid's range "must be rescaled" (section 5.1).

**Output.**
- A domain partition with one operand-inclusion bit vector per domain. Any Boolean expression of any arity is a bit test per
  domain on one build (section 2.7, Table 4).
- Volumetric regions (section 2.7).
- A mesh materialised to double (default for double input) or to the integer grid coordinates (section 3, Table 2).

**Method** (enough to judge the effort):

1. *Kernel* (section 2.1). Input floats are mapped onto an integer grid: int32 for float input, with predicates in 128 bits; int64
   for double input, in 256 bits. Every predicate is a polynomial of total degree at most 3, and there is no filter and no
   fallback kernel. The paper does not say how the grid scale is chosen.
   - Pairwise contacts are classified exactly into five types (VV, VE, VF, EE, EF), from the zero pattern of three orient3d signs
     per fan triangle (Table 1).
   - No Simulation of Simplicity in the arrangement, so a contact the user modelled survives as a contact (Fig. 3).
   - An optional tolerance band changes only the classifier.
2. *Construction* (section 2.2). Only EE and EF make a new point, by `div_round` to the nearest grid cell: "exact as a grid
   coordinate, inexact as a point". Each point is named by the primitives that made it (a topological identity), plus a merge
   of distinct identities at one coordinate.
   - Each cut face is arranged in its own 2D plane. Crossings are named by face triples, refined by canonical edge pairs where
     there is coplanar contact (Fig. 5).
   - Regions are found by a path classification, or by an exact planar walk.
3. *Graph* (section 2.3). The nodes are components of the manifold-edge link (MEL components); the edges are non-manifold edges.
   - The radial order around an intersection edge is decided "by exact sign tests on the original input planes alone": signs of
     degree-2 products of plane coefficients, no constructed coordinate. Two wedges tie exactly when their planes coincide.
   - Non-manifold edges are grouped into polylines and *relations*, one per pair of polyline and incident-component set.
4. *Domain partition* (section 2.4). Each relation votes its canonical radial permutation. The weight of an observation is the
   least area of the faces at its edge (Algorithm 1). The vote emits merge pairs of component sides, and a dense equivalence map
   gives the domains.
   - Within the build the observations can disagree only through a mirrored direction sign or "input sheets inconsistently
     wound". The vote matters when an arrangement is read back from materialised coordinates.
   - An open component's two sides are always fused (d0 = d1). Mode 2 (the default) keeps it as a fin of the domain's boundary;
     Mode 1 (`ignore_open_fragments`) drops it (Fig. 7).
5. *Classification* (section 2.5). Crossing component c toggles the operand bits B(c), so membership is an XOR potential on the
   domain graph.
   - Seeds come from the voted wedges.
   - For components that do not touch: the outer environment of each is its most negative signed-volume domain. The outermost one
     anchors at b = 0, and each other is fixed by an SoS-perturbed segment to a far point (crossing parity), then an XOR BFS.
   - Undeclared open patches are transparent to the cast.
6. *Sheets* (section 2.6). A declared sheet's bit means "behind its normal". Its sides are not fused, so intersection keeps what
   lies behind it and difference keeps what lies in front, and the result is capped by the sheet faces it crosses (Fig. 10).
   - A component the sheet never touches takes its side from the sign of the sheet's generalized winding number at that component.
   - A clean closed mesh declared a sheet behaves as the volume it bounds.
   - On the 1000 corpus pairs with one operand declared a sheet, the outputs are identical and the build times agree within 2 %
     ("untabulated").

**Guarantees, and what it does not guarantee** (section 5.1, appendix A):

- The partition is a combinatorial object. "Materialising the output can introduce self-intersections the exact arrangement did
  not have ... the coordinates carrying it are not guaranteed crossing-free ... We make the topology robust to the crossing; we do
  not remove the crossing itself."
- The vote's guarantee "is statistical, not worst-case". "A configuration can be built to defeat it". "The method is robust
  against the error a real pipeline injects, not against an adversary who controls the geometry". The appendix calls the
  Dirichlet model "a subjective prior". The operative guarantee is definitional: the vote is right exactly when a majority of a
  relation's observations is.
- The "validity" of the comparisons is: closed and consistently oriented, with signed volume and area equal to the reference
  values to floating-point tolerance (section 4.3). Freedom from self-intersections is not part of it.
- Determinism: not claimed as a property. The timings are "the minimum over repeated runs of each deterministic case"
  (section 4.2).
- Not stated: what happens when a declared sheet's free rim lies inside a volume. *(judgement)* There the sheet's two sides are
  kept apart, while the space around the rim is one region. The paper neither refuses this case nor discusses it. Our sheet
  role refuses it as `RimInsideWorkpiece`.

**Cost** (Apple M4 Max, 16 threads, TBB):

- Pairwise Booleans, 1000 random Thingi10K pairs of 100k to 1M triangles per operand (Table 5):

  | library | median | geometric-mean slowdown |
  |---|---|---|
  | trueform | 15.7 ms | 1.0x |
  | MeshLib | | 5.5x |
  | Manifold | 118.0 ms | 7.6x |
  | EMBER | | 10.4x |
  | Cherchi | | 28.7x |
  | CGAL | | 32.1x |
  | Geogram | | 100.4x |

  CGAL timed out (more than 5 h) on one pair; trueform did that pair in 20 ms.
- N-ary unions, medians at N = 4 / 16 / 64 operands: 6 / 25 / 103 ms (Table 6). Bunny swarm: a build of 1.68 s on 56 M
  triangles; the extractions cost 45 to 132 ms each (Table 4).
- Browser (WebAssembly): median 21.9 ms (Table 7).

**Robustness numbers of the vote** (section 4.1), all on reading a *materialised* arrangement:

- Two-sphere sweep: per-edge reading fails at 54 of 191 offsets; the vote fails at 0. Reading from the original planes fails at
  0 (Fig. 12).
- Geological arrangement: 4 of 220 relations split; Thingi10K: 178 of 704 relations in 25 of 59 models. The vote recovers all
  (Table 3).
- Under added error: per-edge reading collapses by an added error of 1e-3 of the box; the vote stays correct on 78 to 100 %
  of the relations there (Fig. 13).

**Mapping onto our problems.**

- **(a) Sheet or shell. Not.**
  - Sheets are declared (`tf` "declared sheets"), as with `InputRoles`. An undeclared open patch is a non-separating fin. It is
    neither tried as a sheet nor capped.
  - That is a third policy beside ours (try as a sheet, then fall back to the repair). *(judgement)* It is closest to our
    `OpenInputsWithoutEffect` reading, where a shallow open tool encloses nothing. It does not decide between sheet and shell; it
    defines the undeclared case away.
  - It confirms the prior-art finding of [OPEN_CUTTING_SURFACE_2026-11.md](OPEN_CUTTING_SURFACE_2026-11.md) section 3: every
    system takes the side from a stated orientation.
  - Their floater rule (the sign of the sheet's generalized winding number) is the opposite of our deliberate
    `FloatingComponent` / `UncutComponents` refusal. *(judgement)* For a curved sheet that sign is not the side of the sheet's
    extension in general. Our refusal (with `UncutSide` as the stated way out) is the safer default; nothing to take over.
- **(b) Open and non-manifold handling of the exact Boolean. Partly.**
  - Our radial order (`ExactMeshBoolean.radial_order`) works on the exact constructed points, in BigInteger dot and cross
    products. trueform's (section 2.3) uses only the input planes: every piece lies in its input triangle's plane, so the wedge
    directions follow from plane coefficients, of degree 2. *(judgement)* That is exact too, and it could be cheaper.
    - It applies to pieces of input triangles, so to every piece of our arrangement.
    - Coplanar ties have to be classified as before.
    - It is a candidate only if `radial_order` is a measurable share of the Boolean's time, and none of our hot-spot studies
      lists it (EXACT_HOTSPOTS_STUDY, EXACT_BOOLEAN_SPEED_LEVERS). Upper-bound check first, a few hours; a design after that,
      about one to two weeks *(estimate)*.
  - Their fin policy (Mode 2) and their vote over "inconsistently wound input sheets" are policies. Our `ConflictEdges` leaves
    such pieces unjoined, exactly. A majority would replace an exact refusal with a statistical guess, against our rule that
    nothing is decided by a threshold.
  - The fallback that caps open tools: trueform has none, so there is nothing to learn about it.
- **(c) `MeshRobustRepair`. Not.**
  - #1548: their orientation vote is the area majority of a component (section 2.3), which we have as
    `Orientation = AreaMajority`. An unwelded soup only gets components after the VV merge of coincident points. The welded case
    is already exact with `MeshRepairOrientation` (HIGHER_MATH_STUDY section 3).
  - #1566 *(judgement, from section 2.5)*: membership is an XOR of the crossings, so an outward shell nested inside another
    outward shell of the same operand ends as a cavity. That is the parity reading that makes #1566 silent, not the
    winding-number-above-0 reading of our `Resolve`.
  - #1575: no wrap. An input with holes is an open component and fused (transparent), so it encloses nothing. That is another
    silent outcome, not a solution.
- **(d) Rounding to doubles. Not.**
  - trueform materialises in two places. The input is snapped to the int32 / int64 grid, so a double input is in general not
    represented exactly: a uniform grid cannot hold doubles near the origin next to large coordinates *(judgement)*. And the EE /
    EF points are rounded to the grid inside the build.
  - After the output is written in doubles it guarantees the partition, not the geometry (section 5.1).
  - Against that, `SafeRounding`:
    - the exact result on the input doubles;
    - untouched triangles are the exact rounded output;
    - edits within `RoundingMaxDisplacement` times the diagonal;
    - an exact check for self-intersections, degenerate triangles and false contacts, with `StrictRounding` as a hard refusal.
  - The grid retry of the pipeline is the nearest thing we have to their kernel (snapped inputs). It runs only after a failure,
    it is reported in `Report.Route`, and its risk (a capture of about one cell) is documented.
  - The paper itself names a rounding "that introduces no new intersections" as the open problem it does not solve. Our
    rounding repair is our answer to that, within its bound.
  - The 128 / 256-bit widths match the quantized kernels that
    [PLANE_BASED_KERNEL_STUDY_2026-11.md](PLANE_BASED_KERNEL_STUDY_2026-11.md) priced (no-go, owner 2026-11-19: exact on input
    doubles needs about 510 to 540 bits there).
- **(e) Reference implementation. The one real candidate, behind a licence question.**
  - Manifold, used locally today, takes only closed manifold solids. trueform takes declared sheets, soups, open patches and
    non-manifold input, so it is the first independent implementation of a sheet cut we know of with a Python package.
  - A black-box comparison never touches its source. Like the CGAL driver of `benchmarks/BooleanComparison`, nothing of it would
    be distributed.
  - The licence permits only noncommercial purposes. Whether this repository's development counts is a question for the owner,
    not for this note.
  - Two caveats for its use as an oracle. Its outputs are not free of self-intersections by design, so a difference must be
    judged by our exact checks, not taken as a verdict. And it quantizes, so near-degenerate cases at 1-ulp offsets can come out
    differently for reasons of input precision alone.

**Licence and porting.** No code may be used or ported (PolyForm Noncommercial; source not read). Ideas from the paper may be
implemented from its text and cited: the five contact types, the radial order from the input planes, the build / query split with
bit vectors (which goes back to Zhou et al. 2016 and Lévy 2025, already our sources), and the vote. *(judgement)* Of these, only
the radial order from planes is a candidate. The bit-vector build / query split is what `ExactMesh` (lazy expressions) already
gives in one exact pass.

**Verdict.** (a) not; (b) partly, the radial order from the input planes, after an upper-bound check; (c) not; (d) not;
(e) a candidate, subject to the owner's licence decision.

## 4. Spike, if the owner wants one

Question: *on procedural cases, does trueform's sheet cut agree with `ExactMeshBoolean` with `InputRole.Sheet`, and where it
does not, which result passes our exact checks?*

- **Precondition:** the owner confirms that a local, undistributed run of trueform under PolyForm Noncommercial is permitted for
  this work.
- **Cases** (procedural, no meshes committed): the sheet-extension and open-cutting-surface fixtures (slot, fin, chord, rectangle
  short on four sides, dome, tray); rim exactly on a face and a few ulps inside; near-coplanar sheets; a floater under a curved
  sheet; nested outward shells (#1566); a box with one face removed, as a sheet and undeclared.
- **What to record:** the outcome per case (cut, refused or fin), the volume removed, `NotCleanReason` of each output, and the
  agreement of the kept volume (exact on ours, double on theirs).
- **Stop rule** *(judgement)*: if the two agree on every case where ours succeeds, and every case where they differ is one where
  ours refuses and theirs cuts, the result is documentation of the policy difference, not a defect. A case where ours succeeds and
  theirs is closer to the obvious intent is a finding for our sheet role.
- **Effort** *(estimate)*: one to two days. A Python driver in `benchmarks/BooleanComparison` style, run locally only.

The second check needs no licence decision: an upper bound of `radial_order`'s share in the Boolean time on the existing
benchmark sets (a stopwatch around the call, a few hours). If it is under a few percent, the plane-based order of trueform
section 2.3 is closed.

## Sources

- S. Wang, J. Meny, I. Grguric, M. Rahimzadeh, D. Zorin, D. Panozzo, H.-T. D. Liu, *Solid-Shell Labeling for Discrete
  Surfaces*, SIGGRAPH Asia 2025 Conference Papers, DOI 10.1145/3757377.3763847 (read in full; code MIT, not read).
- Z. Sajovic, D. Knez, *trueform: fast and robust mesh CSG via topological aggregation*, arXiv 2607.15905v1, 2026 (read in
  full).
- Z. Sajovic, D. Knez, *trueForm: Real-Time Mesh Booleans that Commute with Mesh Idealization*, TechRxiv, 2025, DOI
  10.36227/techrxiv.174667714.42575478/v1 (abstract only; the full text was not reachable).
- trueform repository `LICENSE`, `COMMERCIAL.md`, `LICENSE.noncommercial`, README (read 2026-11-25; no source read).
- Cited through the papers or our earlier notes, not re-read: K. Takayama et al., arXiv 1406.5431, 2014; X. Du et al., *Optimizing
  global injectivity for constrained parameterization*, ACM TOG 40(6), 2021; B. Lévy, arXiv 2405.12949; S. Böhm, E. Runge,
  CAD 180, 2025.
