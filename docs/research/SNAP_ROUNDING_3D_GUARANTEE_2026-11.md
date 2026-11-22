# 3D rounding with a proven guarantee: what it would gain and cost (research spike, 2026-11-22)

Status: **research note, no library code.** Owner go 2026-11-22 for a spike on the plans #1278 (hard problem 1) and
#1259 (the Devillers et al. experiment). The question: can the output of the exact boolean be rounded to doubles with a
proven guarantee (no new self-intersection, bounded displacement, no lost solid) at a practical cost, and how does that
compare with today's SafeRounding. The cost of SafeRounding itself is the subject of a separate spike; this note owns
the guarantee side. The measurement is `benchmarks/SnapRoundingExperiment` (`bound`, `GuaranteeBound.cs`, `bound.py`);
its raw output is `benchmarks/SnapRoundingExperiment/trace/results/bound_*_2026-11-22.jsonl`. Measured on master
`ed93ef26`, 4 cores, the cloud container of the session. Every input is generated in code.

Not repeated here (closed, see [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md),
[SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md) sections 7-9 and
[BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) section 2): options A and B (#313), the
empty ball, clearance skins and the separating-axis prefilter, the position search (#755) and its plateau variant.

## 1. Result in short

- **The only known guarantee is the wrong guarantee for us.** Devillers, Lazard and Lenhart prove an intersection-free
  simplicial complex on a grid within 3/2 cells. They do not prove a closed manifold surface: their first step merges
  everything closer than one cell along one axis into shared sheets, and a face may end as an edge or a point. So
  "no new self-intersection" and "bounded displacement" are proven, "clean in the sense of `NotCleanReason`" and "no
  lost solid" are not, and the last cannot be proven by any scheme on a fixed grid (section 3).
- **Its gain is bounded by what SafeRounding leaves, and that is small.** A result that SafeRounding reports as
  rounding clean is already certified (verified exactly, within the proven drift bound). What is left: 19 of 534
  generated results, all of them dense clusters built for it (4 more are not clean by contacts or an inside-out tiny
  component, which no rounding repairs); on real data, by the earlier notes, 1 of 16 239 pairs and the five RC4b
  differences. On all 23 the patch around the defects holds sheets closer than a cell (a median of 37 % of its
  triangles), which the scheme would merge, not separate (section 4.4).
- **It keeps no solid that SafeRounding drops.** All 156 components that SafeRounding removed on the 534 results are
  below 0.001 cell thick (median 1e-11): slivers that the grid scheme collapses too (section 4.2).
- **Its cost in the global form rules it out.** A lower bound of its output, counted without building it: 16 to 258
  times the triangles of an ordinary result of 300 to 420 000 triangles, 0.5 to 0.9 √n (the paper expects O(n √n)),
  and every vertex moves to a grid 2^33 times coarser than the doubles' (section 4.3).
- **A local form is cheap in triangles and has no proof.** The patches are about 100 triangles and grow by a factor
  of about 5; but the seam to the untouched result must be verified and the patch grown on failure, which is the
  round-verify-repeat structure that was closed as option B, and the merged sheets remain (section 4.4).
- **Leaning: no build (option 1 of section 6).** State SafeRounding's guarantee as the per-result certificate it is.
  The decision is the owner's.

## 2. The paper, read in full

O. Devillers, S. Lazard, W. Lenhart, *3D snap rounding*, SoCG 2018, LIPIcs 99:30 (CC-BY, 14 pages, read in full; the
proofs of its lemma 2 and lemma 12 are only in the full version, which this session could not fetch). The journal
version (*Rounding meshes in 3D*, DCG 64, 2020) was not readable from here; its abstract, by a search snippet, gives
O(n^13) size and O(n^15) time in the worst case, O(n^4 √n) and O(n^5) under the assumptions, and the same expectation
of O(n √n) in practice. Earlier notes of this repository quoted the paper from abstracts; the statements below replace
them. No code was read: none is published.

### 2.1 The theorem

Input: polygonal faces in general position that do not properly intersect (their intersection, if any, is a common
face, edge or vertex of both: shared edges and vertices are allowed, so the exact result of a boolean is a valid
input). Output: a simplicial complex Q with vertices on the integer grid and a map from every input face to a set of
faces, edges or vertices of Q, with a continuous motion from each face to its image such that
- the Hausdorff distance of a face to its image never exceeds 3/2 in the maximum norm, and
- two points that meet during the motion stay together.

What the theorem does **not** say, and what matters for us:
- *A face may become an edge or a vertex*, and two faces may end on one face of Q. The output is a complex, not a
  surface: where two sheets met, Q has one face with four face neighbourhoods around it. Nothing is said about solids,
  closedness or manifoldness.
- *No bound relates the output to the input size in a useful way* (2.3), and there is no lower bound.
- *Nothing is local.* Every step works on all faces.
- *No implementation and no experiment.* The paper has no measurement; the complexity "in practice" is an expectation
  (its remark 14).

### 2.2 The algorithm, in our words

The grid is the integer grid; a voxel is a unit cube around a grid point. The three axes have different roles.

1. **Collapse along y.** The faces are put in an order. For each pair i < j, the part of face j that lies less than one
   unit from face i *measured along y* is cut out of face j and replaced by its projection onto face i (along y), and
   new "connecting faces" parallel to y close the gap between the cut and its projection. After this step, a line
   parallel to y that meets a face in its interior meets no other face within distance 1 (the paper's lemma 3). This
   step moves points by up to 1; it is the reason for the 3/2, and it is where thin features are merged.
2. **Slabs along x.** All faces are projected onto the xy-plane, the arrangement of the projected edges is computed and
   lifted back onto *all* faces (every face is cut by the vertical walls through the edges of every face above or
   below it). The edges are then projected onto the xz-plane and that arrangement is computed too. A *thin slab* is
   the unit-wide region x ∈ [c − 1/2, c + 1/2] of a grid column c that holds a vertex of either arrangement; a *thick
   slab* is what lies between two consecutive thin slabs. Every face is cut by the walls of every slab.
3. **Triangulate.** In a thin slab, the faces are projected along x onto the wall x = c; that 2D arrangement is snap
   rounded in the classical way (hot pixels, edges split at their boundaries; the 2D result used is that of Guibas and
   Marimont 1998). In a thick slab every face is a trapezoid between the two walls; trapezoids over the same floor
   trapezoid get the same diagonal, and the extra vertices from the thin slabs are fanned in.
4. **Snap.** Every vertex moves to the centre of its voxel (in a thin slab onto the wall x = c): at most 1/2.

Why nothing crosses in step 4: in a thin slab everything ends in one plane and the 2D snap-rounding theorem applies;
in a thick slab all vertices are on the two walls, the floor projections of the trapezoid edges do not cross by the 2D
theorem, and for the diagonals the paper uses the distance 1 along y from step 1 (two points that the snap would bring
onto one vertical line were a full unit apart in y, which the general position excludes at half-integer coordinates).
The general position (no face parallel to an axis, and three more conditions) is reached by four nested symbolic
perturbations: a translation in x, one in y, a scaling of the y unit, a rotation.

### 2.3 Size

With f the largest number of faces a line-cylinder of one face meets, f1 those among them within distance 1, and w_x
the largest number of faces a plane x = c meets, the output has O(n w_x² f⁷ f1⁵) elements (proposition 7), so O(n^15)
in the worst case and O(n^5) for restricted Delaunay triangulations of samplings of "nice" smooth surfaces
(proposition 13: w_x = O(√n), f = O(n^(1/4))). Remark 14 expects O(n √n) on realistic data: f and f1 bounded and
w_x = O(√n). The paper's remark 11: these sizes do not depend on the grid; a finer grid does not make the output
smaller until the grid is so coarse that the whole surface fits in a bounded number of voxels.

The O(n √n) is structural, not an artefact of the analysis: **every face is cut at every thin slab it spans**, and
there is a thin slab for every grid column that holds a vertex anywhere in the model. A triangle that spans a tenth of
the model's width is cut into about a tenth of all occupied columns. Section 4.3 counts this.

## 3. Fit to our constraints

| Constraint | Fit |
|---|---|
| The grid | The paper needs one uniform grid. Doubles are not uniform, so the grid is a power of two g with g ≥ ulp(max \|x\|): every grid point is then a double. For the drift bound and the degenerate test together, g is the grid of [BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) section 2.3, about 2^-19 of the diagonal. The inference there is confirmed against the full text in two points (the 3/2 is in the maximum norm; shared edges and vertices are allowed) and stays an inference in the third (the area lemma is ours). On all 534 generated results the grid fits the drift bound and is representable (the `utm` set included). |
| Drift | 3/2 cells in the maximum norm is up to (3/2)√3 g ≈ 2.6 g, within `maxLength` on the grid above. But **every vertex moves**, by up to 2.6 g ≈ 2^34 ulps of the diagonal, also where plain rounding is clean. Today a vertex that needs no repair is the exact point rounded to the nearest double, and an input vertex is kept where it is. |
| Clean (`NotCleanReason`) | Not implied. Step 1 turns two sheets closer than a cell into one sheet with connecting faces: a non-manifold complex. Turning that back into a closed manifold surface (drop the doubled sheet, re-pair the faces around the seam) is a further step with no statement in the paper; it is the same decision D′ takes today when it removes a thin component, moved from components to parts of components. |
| No lost solid | **Impossible for any scheme on a fixed grid, and not claimed by the paper.** A solid inside one voxel has all its vertices at one grid point; a plate thinner than a cell along y is merged in step 1. A guarantee can only read "a solid is kept where it is thicker than c cells". |
| Determinism | Fits: exact arithmetic throughout, the face order of step 1 by id; no floating-point decision. The four-level symbolic perturbation is new exact code of its own (our exact results are full of axis-parallel faces, so the degenerate branches are the common case, not the rare one). |
| CPU only, netstandard2.0 and net10.0 | Fits (2D arrangements and 2D snap rounding in exact arithmetic). |
| API and outputs | A changed default would change every boolean result (all vertices on a coarse grid, many times the triangles): an owner decision by the rules, and against D′ ("untouched triangles are the exact result, rounded"). |

Building blocks in the library (search of 2026-11-22): `comp_geom/SnapRounding2.cs` (2D snap rounding with hot
pixels, exact; it is the 2D step of the thin slabs), the exact 2D arrangement and constrained triangulation of
`ExactMeshArrangement` and the 2D mesher, `tests/.../Harness/SeparationCertificate.cs` (the exact d-separation check),
`SnapFallback.cs` and `LatticeProbe.cs` in the benchmark, `SolidBooleanPipeline.InputGrid` (a power-of-two snap of
the *inputs*). Missing: step 1 (the collapse along y with connecting faces), the slab decomposition, the symbolic
perturbation, and anything that turns the complex back into a closed surface.

## 4. Upper bounds, measured before anything is built

`python3 bound.py run <out> <set>...`, one process per case; `python3 bound.py tables <out>...` prints the tables
below. Per case: the exact boolean with plain rounding and with SafeRounding (after a warm-up run), then counts on the
results. Sets: the 516 generated cases of the earlier notes (`clusters` 198, `utm` 114, `booleans` 198, `cylinder` 6:
built to defeat the rounding) and `scale`, new here: the three booleans of two cube-spheres, the second rotated,
shifted and smaller, at six resolutions (an ordinary result with no axis-parallel structure, 336 to 423 734
triangles).

### 4.1 The gain in clean results is bounded by what SafeRounding leaves

| set | cases | failed | plain rounding clean | SafeRounding: rounding clean | clean | at the effort limit | results with removed components | removed components | of them thinner than one cell | of them one cell or thicker |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| clusters | 198 | 0 | 0 | 179 | 177 | 0 | 62 | 134 | 134 | 0 |
| utm | 114 | 0 | 28 | 114 | 112 | 0 | 10 | 20 | 20 | 0 |
| booleans | 198 | 0 | 72 | 198 | 198 | 0 | 2 | 2 | 2 | 0 |
| cylinder | 6 | 0 | 1 | 6 | 6 | 0 | 0 | 0 | 0 | 0 |
| scale | 18 | 0 | 13 | 18 | 18 | 0 | 0 | 0 | 0 | 0 |
| all | 534 | 0 | 114 | 515 | 511 | 0 | 74 | 156 | 156 | 0 |

("Rounding clean": the repair's own verdict. "Clean": also `MeshRobustRepair.NotCleanReasons` empty. The four results
that are rounding clean and not clean: two clusters and two `utm` cases with contacts at shared positions or an
inside-out tiny component.)

- A result that SafeRounding reports as rounding clean **is certified**: the exact search found no intersecting pair
  and no degenerate triangle, and the drift is within `maxLength` by D′. A guarantee in advance changes nothing for
  these results. Its whole gain is on the others.
- On real data that is 1 of 16 239 closed rotated pairs, 5 ABC differences (RC4b, a documented limit) and none of the
  294 T6 results (the earlier notes; not measured again). On the generated sets, built for this, it is 19 of
  534.
- A guaranteed scheme does not turn those into clean results either (4.4).

### 4.2 Lost solids: nothing to gain

SafeRounding removed 156 components on 74 of the 534 results. Each was measured on the plainly
rounded exact result: its thickness (the smaller of 2 volume / area and area / twice its length) in cells of the grid
g a guaranteed scheme would use.

- All 156 are thinner than one cell: the thickest is 0.0006 cell, the median 1e-11 cell, two are exactly flat. They have
  4 to 20 triangles: closed slivers between nearly coincident surfaces.
- No component thinner than a cell was kept, and no component of one cell or more was removed.
- (The tool finds the removed components from outside the library, as the components of the plain result none of
  whose triangles is in the repaired result; it lists 156, the repair counts 156.)

So on these inputs the two approaches lose the same solids: what D′ removes is far below one cell, where step 1 of the
paper merges the two sides as well. The solids a grid scheme would keep and D′ would not are those between one cell
and `maxLength` (3.5 to 7 cells) thick that the repair also had to edit; none occurred. The reverse case is real: D′
keeps a thin solid that needs no edit, at any thickness down to a few ulps (#1067), and a grid scheme merges
everything below one cell, 2^33 times coarser.

### 4.3 The cost of the global scheme: a lower bound of its output

Counted, not built: for each triangle of the result, the thick slabs of positive width it crosses with a chord of two
cells or more in the slab's middle plane. Such a piece ends as at least one triangle of the output (one of its wall
edges is two cells long, so its ends snap to two voxel centres, and the opposite wall is another column). Slabs come
from the result's own vertices only; the paper adds the vertices of both arrangements, and the cuts of step 2 by the
other faces' edges are not counted at all. The axis with the smallest count plays x. Two layers closer than a cell
that the scheme merges (in step 1, or by snapping onto the same grid triangle) are counted once each, so on results
with such layers the count can be too high by the number of layers; the `scale` family has them only in a band of
one cell along the intersection curve.

| case | result triangles n | boolean | of it rounding | thin slabs | output triangles, at least | factor | on a grid as fine as the coordinates | factor | n sqrt(n) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| scale:6:Union | 576 | 0.01 s | 0.00 s | 184 | 9601 | 17 | 9653 | 17 | 13824 |
| scale:6:Intersection | 336 | 0.01 s | 0.00 s | 157 | 5281 | 16 | 5281 | 16 | 6159 |
| scale:6:Difference | 532 | 0.02 s | 0.00 s | 165 | 8726 | 16 | 8730 | 16 | 12271 |
| scale:11:Union | 1974 | 0.03 s | 0.01 s | 589 | 53898 | 27 | 55667 | 28 | 87704 |
| scale:11:Intersection | 1090 | 0.03 s | 0.01 s | 486 | 29430 | 27 | 29659 | 27 | 35987 |
| scale:11:Difference | 1826 | 0.03 s | 0.01 s | 519 | 51032 | 28 | 52711 | 29 | 78028 |
| scale:21:Union | 7210 | 0.09 s | 0.04 s | 1979 | 334305 | 46 | 354411 | 49 | 612213 |
| scale:21:Intersection | 3726 | 0.08 s | 0.02 s | 1611 | 183826 | 49 | 187693 | 50 | 227439 |
| scale:21:Difference | 6646 | 0.11 s | 0.05 s | 1718 | 326807 | 49 | 341811 | 51 | 541802 |
| scale:41:Union | 27486 | 0.15 s | 0.04 s | 7097 | 2236316 | 81 | 2480650 | 90 | 4556877 |
| scale:41:Intersection | 13594 | 0.17 s | 0.05 s | 5728 | 1248235 | 92 | 1299034 | 96 | 1584969 |
| scale:41:Difference | 25246 | 0.20 s | 0.08 s | 6077 | 2229348 | 88 | 2425614 | 96 | 4011334 |
| scale:81:Union | 107276 | 0.80 s | 0.29 s | 25607 | 14309850 | 133 | 18630115 | 174 | 35136125 |
| scale:81:Intersection | 51688 | 0.62 s | 0.16 s | 21090 | 8674656 | 168 | 9714617 | 188 | 11751264 |
| scale:81:Difference | 98314 | 0.86 s | 0.29 s | 21990 | 14592062 | 148 | 18375643 | 187 | 30826417 |
| scale:161:Union | 423734 | 1.46 s | 0.36 s | 82620 | 63653352 | 150 | 143181882 | 338 | 275829028 |
| scale:161:Intersection | 201372 | 1.27 s | 0.27 s | 75279 | 52044992 | 258 | 74585321 | 370 | 90364661 |
| scale:161:Difference | 387890 | 1.44 s | 0.35 s | 68466 | 67734683 | 175 | 141482188 | 365 | 241581056 |

- The factor grows like √n, as the paper's remark 14 expects: on the finest grid the factor is 0.5 to 0.9 √n at every size (17 at
  576 triangles, 90 at 27 486, 338 at 423 734). On the grid g the count falls behind at the top (150 to 258 at
  resolution 161), because the triangles are then only a few cells wide and the count skips chords below two cells.
- The grid does not help (remark 11): with every distinct coordinate as its own slab the count is the last column.
- For comparison, what these results need today is the column "of it rounding": plain rounding is clean on 13 of the
  18, the other five need 1 to 6 edits, and the rest of the time is the verifying search.

| set | results | factor of the lower bound: median | smallest | largest |
|---|---:|---:|---:|---:|
| clusters | 198 | 7.9 | 5.5 | 11.8 |
| utm | 114 | 8.5 | 5.7 | 12.3 |
| booleans | 198 | 3.5 | 1.9 | 9.9 |
| cylinder | 6 | 3.8 | 0.6 | 4.8 |
| scale | 18 | 65.3 | 15.7 | 258.5 |

The small generated cases have few vertices, so few slabs; their factors are the floor of the scheme, not a sign that
it is cheap.

### 4.4 A local variant: the patch, and what the scheme does to it

The experiment proposed in BOOLEAN_RESEARCH_PRIOR_ART section 2.6 applies the scheme only around the defects. Counted
here on the results with a defect left (degenerate triangles and intersecting pairs, three rings of triangles around
them):

| set | results with a defect left | defect triangles: median, largest | patch (3 rings): median, largest | share of the result: median, largest | lower bound of the patch after the scheme, factor: median, largest | results whose patch has triangles closer than one cell | such triangles, share of the patch: median, largest |
|---|---:|---|---|---|---|---:|---|
| clusters | 21 | 6, 32 | 99, 244 | 21 %, 48 % | 5.2, 7.8 | 21 | 37 %, 74 % |
| utm | 2 | 6, 7 | 90, 90 | 19 %, 19 % | 4.3, 4.3 | 2 | 23 %, 23 % |
| all | 23 | 6, 32 | 98, 244 | 20 %, 48 % | 5.2, 7.8 | 23 | 37 %, 74 % |

("Closer than one cell": patch triangles that have a triangle with no common position within the distance g, measured
in doubles. The 23 results are the 19 that are not rounding clean and the 4 of 4.1.)


- The patches are small and the lower bound of their size after the scheme is modest (the patch has few vertices,
  hence few slabs). Cost is not what stops a local variant.
- What stops it is the two things the theorem does not give:
  1. **The seam.** The scheme snaps the vertices where the faces are cut at the patch border, by up to 3/2 cells. The
     triangles outside that use those vertices move with them, and the theorem says nothing about them: they are
     certified by nothing unless the search is run again around the seam, and if it finds a defect the patch must
     grow. That is a verify-and-grow loop, the structure of option B (closed in #313) and of Valque's local version
     (which ended on 64 % of 520 hard models within an hour, SAFE_ROUNDING.md section 7). It always ends only because
     the patch can grow to the whole result, where the cost is that of 4.3.
  2. **The sheets.** The defects that are left sit where surfaces are nearly coincident (the generated clusters by
     construction; RC4b by its diagnosis). Step 1 merges exactly those. The output around a defect is then a
     non-manifold complex, and the result is not clean in the sense of `NotCleanReason` until the merged sheets are
     removed, which no theorem covers.

## 5. Today's SafeRounding against a rounding with the paper's guarantee

| | SafeRounding (D′, D1, #755) | Devillers et al., global | the same, local with a verified seam |
|---|---|---|---|
| No new self-intersection | per result: verified exactly, reported (`RoundingClean`); not promised in advance | proven for the complex | proven inside the patch; the seam verified, not proven |
| Displacement | proven ≤ `maxLength` (1e-5 of the diagonal); 0 where no repair is needed; input vertices never move | proven ≤ 3/2 cells (max norm), about 2.6 g; every vertex moves | as global inside the patch, 0 outside |
| Ends clean (`NotCleanReason`) | 515 of 534 generated results rounding clean; all but 3 of 16 239 real pairs (earlier note; one with a defect left); reported when not | not implied: merged sheets, a non-manifold complex | not implied |
| Lost solids | whole components that are tiny, thinner than `maxLength` or of numerically zero volume, only if the repair edited them; untouched thin solids stay | everything thinner than one cell is merged, edited or not | as global inside the patch |
| Output size | the exact result's, minus collapsed slivers | at least 16 to 258 times on ordinary results of 300 to 420 000 triangles, growing like √n | the patch grows by a small factor; the rest unchanged |
| Cost | 26 to 59 % of the boolean in the median of the sets here (four cases at a time; `scale` alone: 28 %, 2.0 of 7.4 s) | exact 2D arrangements of all faces in two projections; not measured, the output alone rules it out | per defect cluster, plus the search around the seam, repeated when the patch grows |
| Code | built | none anywhere; steps 1 and 2 and the perturbation are new exact code | the same code, plus the patch logic |

## 6. Options

| # | Option | Gain | Effort | Risk |
|---|---|---|---|---|
| 1 | **Keep SafeRounding; state its guarantee as what it is** (a certificate per result: `RoundingClean` true means verified free of intersections and degenerate triangles within the proven bound; false is reported). No build | none needed on what is measured: the certificate already covers every result that a guarantee in advance would cover, at no extra cost | none (this note; a sentence in the SafeRounding guide if the owner wants it) | none |
| 2 | Local Devillers-type fallback for the defect clusters, with a verified seam and a rule for merged sheets (the experiment of #1259) | at most the results SafeRounding leaves: 5 RC4b differences and 1 of 16 239 pairs on real data, 19 of 534 generated; and only if the merged sheets can be removed cleanly, which 4.4 puts in doubt on exactly these cases | L: steps 1-4 in exact arithmetic with the symbolic perturbation, the patch and seam logic, the sheet removal; weeks, tier L; a prototype of 2-3 days could not include step 1, which is the part in question | high: no proof for the seam or the sheets, so the result is still "verified, not guaranteed"; a new exact module for a handful of cases |
| 3 | The global scheme as an option | a proven intersection-free complex on a coarse grid for every result | L, more than option 2 | not practical: 16 to 258 times the triangles and every vertex moved; not clean by our test |
| 4 | Avoid the rounding where the guarantee matters: chains of booleans stay exact and round once at the end (`ExactMeshBoolean` on exact vertices, the 4.0 design) | fewer roundings, so fewer chances for a leftover; no guarantee for the last one | exists as a design with its own decisions (EXACT_BOOLEAN_40_DESIGN.md) | its own |
| 5 | A theory effort: a local rounding lemma with a fixed, already rounded collar, or a lower bound of the output size | a publishable result if it works; for the library the right to say "always clean within the bound" | months, uncertain | research |

How others solve it: keep exact or implicit coordinates (Diazzi and Attene 2021, Cherchi et al., EMBER); round, verify
and repeat (Zhou et al. 2016; Valque and Lazard 2025, shipped in CGAL 6.1, whose documentation says the result has "no
guarantee of its validity"); Lévy 2024 names snap rounding as the missing component. Nobody we found implements
Devillers et al. or reports a measurement of it; Diazzi and Attene 2021 name it as the only algorithm with guarantees and too complex to use (by a search snippet). Terms searched
on 2026-11-22 without a newer result: "3D snap rounding 2025 2026 guarantee", "Rounding meshes in 3D lower bound
output", "snap rounding polyhedral subdivision output complexity".

**Leaning: option 1.** The upper bounds close both plans for now: the gain of a guarantee in advance is bounded by a
handful of results, the paper's scheme does not make those clean, loses the same solids, and its global form costs
16 to 258 times the triangles. Plan #1259's trigger ("a corpus where SafeRounding fails on significantly more than
the RC4b cases") stays the right trigger; if it fires, the first step is not the patch experiment but the question of
4.4, item 2: what a clean result should be where two sheets are closer than the bound. Option 5 is the only path to a
real guarantee and is research, not engineering.

For the speed spike that runs in parallel: nothing here lowers the cost of the rounding. The certificate that makes a
clean result trustworthy is the verifying search, which is the cost that spike studies; a scheme with a guarantee in
advance would replace that search only by something far more expensive.

## 7. Reproduce

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
cd benchmarks/SnapRoundingExperiment
python3 bound.py run out/bound.jsonl clusters utm booleans cylinder -j 4
python3 bound.py run out/bound_scale.jsonl scale -j 1 --timeout 1800      # alone on the machine: it is timed
python3 bound.py tables out/bound.jsonl out/bound_scale.jsonl
dotnet bin/Release/net10.0/SnapRoundingExperiment.dll bound scale:41:Union   # one case
dotnet bin/Release/net10.0/SnapRoundingExperiment.dll bound selfcheck        # the slab count on a hand-counted case
```
