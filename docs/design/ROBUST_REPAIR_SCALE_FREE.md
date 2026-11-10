# Scale-free robust repair (design note, 2026-11-10, hard problem 9)

**Question (owner 2026-11-10):** `MeshRobustRepair`'s cleanups are relative to all triangles or the global box, so they erode or
drop far parts (F1b / F1c), and the 2^k scale invariance inside the supported range keeps breaking in new places (F4, F5). Is there a
formulation of the repair stages in which no cleanup depends on the global box and the result is invariant under 2^k scaling and
translation by construction?

**Answer of this note.** Yes for 2^k scaling, and it is cheap: one exact normalisation of the working frame (option A). Yes for scale
disparity between parts that do not interact, by repairing each part in its own frame and checking the union exactly (option B): in the
probe of section 3 this keeps every part at 100 % where the whole-scene repair loses, erodes or blobs it. Translation invariance bit for bit
is only definable for translations that are exact on the input, and is reachable for those by an exact frame translation (section 5.2).
What no formulation removes: disparity inside one part (a small feature inside the box of a large one) for the resampling stages
(solidify, alpha wrap), whose resolution is one global parameter by their algorithm, and the degenerate-area criterion of
`NotCleanReason`, which is global by its documented meaning and must not change.

**Recommendation.** A now, as its own small PR. Then B in two stages behind a measured gate. D (a scale-free cleanliness criterion next
to `NotCleanReason`) is an owner decision about the status of far parts. C (local tolerances inside every stage) not now. Details in
section 6, owner questions in section 7. Nothing is implemented; no library code was changed for this note.

Read with: `RepairCampaignFindings` (F1 to F5), [QA.md](../QA.md) 2.5a (supported range), [ROBUSTNESS_SWEEP.md](../ROBUSTNESS_SWEEP.md)
(R29), [alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md), #537 (F4), #543 (F5).

## 1. Stage map: what each quantity is relative to

Legend for "relative to": **abs** an absolute constant in input units; **all** the diagonal (or largest dimension) of the bounds of all
triangles; **largest** the diagonal of the largest connected component (the far-part mode, only when a triangle of some component is
below the degenerate area by **all**, the input is closed and has at most 64 components); **unit** a working copy scaled by a power of
two to an extent in [1, 2); **local** an edge length, a cell, a component box; **none** dimensionless. "2^k" says whether the stage is
invariant under 2^k scaling by construction; "disparity" whether a small part far from (or next to) a large one is treated at its own
scale; "translation" whether the absolute coordinate magnitude enters.

The rows come from a sweep of the files by two read-only agents; the `MeshRobustRepair` rows and the rows marked (v) were checked by
hand against the code at 4069edb. Line numbers are of that commit.

| Stage | Quantity | Relative to | 2^k | Disparity | Translation |
|---|---|---|---|---|---|
| Entry (`Apply`) | range check: nonzero magnitudes in [2^-200, 2^400], extent >= 2^-40 of the largest magnitude (v) | abs / none | yes | -- | yes (by design) |
| Entry | far-part switch `global_scale_share`: a triangle below 1e-12 * diag^2 (v) | all | yes | switches the mode | no |
| `MeshAutoRepair` | `RelativeRepairTolerance` 1e-8, `RelativeMinEdgeLength` 1e-6 (crack merge 2x, boundary collapse 0.5x, final edge collapse) | all, capped by 0.01 * median edge of the whole mesh; **largest** in the far-part mode (v) | yes | no (one cap for all parts) | via the floor below |
| `MeshAutoRepair` | floor of both tolerances: 16 * eps * largest absolute coordinate | abs magnitude | yes | no | **yes** |
| `MeshAutoRepair` | fallbacks `ZeroTolerancef` (1e-6f), 1e-4 when the diagonal is not finite; `ToleranceModes.Absolute` | abs | **no** | no | no |
| `MeshAutoRepair` | occlusion offset `max(0.01 * tolerance, floor)` (`RemoveOccludedTriangles.NormalOffset`) | all | yes | no | yes (floor) |
| `MergeCoincidentEdges` | squared merge distance; hash cell `MaxDim / N`, at least the merge distance, origin at the bounds minimum | all / local | yes | no | no |
| Hole fill (`MinimalHoleFill`, smoothed fill) | target edge = mean loop edge; angle tolerances 1e-4, `ZeroTolerancef` on angles | local / none | yes (not verified inside the fillers) | yes | not verified |
| `MeshRepairOrientation` | degenerate area 1e-10 * diag^2 (written without the square); ray length 2 * diag | all | yes | no | ray origins use absolute coordinates |
| `MeshPostRepairCleanup` | degenerate area 1e-12 * diag^2 | all; `ReferenceDiagonal` (**largest**) in the far-part mode; computed on a **unit** copy outside an extent of [2^-100, 2^100] | yes outside the window, see S2 | only in the far-part mode | no |
| `MeshPostRepairCleanup` | aspect ratio 1000, fold angle 175 deg, needle 0.1, normal dots | none | yes | yes | no |
| `MeshPostRepairCleanup` | inverted components: volume (a cube of lengths) about `CachedBounds.Center`; sample offset `min(1e-4 * component diagonal, 0.25 volume / area)` | local (per component) | overflow / underflow of the cube at the range edges | yes | centred |
| `NotCleanReason` (public) | degenerate area 1e-12 * diag^2 (v), on a **unit** copy outside [2^-100, 2^100] | all | yes outside the window, see S2 | **no, by definition** | no |
| Volume check (`MeshRepairMetrics`) | IoU >= `MinVolumeIoU` 0.99 of the whole solid; `normalizing_scale` for the cube | all (one ratio for the scene) | yes | no: a part below 1 % of the volume is not seen | no |
| Lost-part check | `MinComponentDiagonal` 1e-3 of **all** (`CheckLostComponents`) or of **largest** (far-part mode); closed parts only, half of the samples (v) | all / largest | yes | parts below 1e-3 and open parts are not judged | no |
| Exact resolve (`ExactMeshBoolean.Resolve`) | exact arithmetic, no tolerance | none | yes (F4 bisect, #537) | yes | yes |
| SafeRounding / `MeshRoundingRepair` | `RoundingMaxDisplacement` 1e-5 of the diagonal of the largest component; `RoundingUlpFactor` 256 ulp of the largest coordinate; `RoundingAbsoluteTolerance` (user units, default 0) | largest / abs magnitude / abs | yes with the default 0 | partly (largest, not the part itself) | **yes** (ulp term) |
| After the resolve | `MeshPostRepairCleanup`, `collapse_slivers` (length 1e-4 * diag, area 1e-12 * diag^2; closed components below 1e-8 * diag^2 in area with a sliver are removed), on a **unit** copy (v) | all, **largest** in the retry | yes | a real part of about 1e-4 * diag is removed (documented) | no |
| After the resolve | `SeparateTouchingVertices`, `SeparateFraction` 1e-4 of the distance to the fan centroid | local | yes | yes | no |
| Solidify (`MeshWindingNumberSolidify`) | cell = diag / `SolidifyResolution` (128), octree, contour, reduction to (0.05 cell)^2, splinter removal | **unit** copy since #537 (scaled, not translated), so relative to all | yes | **no**: a part below a cell vanishes | not translated (adaptive path); falls back to the unscaled mesh for a tiny extent far from the origin |
| Alpha wrap (`MeshAlphaWrap`) | alpha = diag / 300, offset = diag / 3000 (from `MeshRobustRepair`); contact 1e-6 offset; snap 1e-9 alpha; margins | all, no normalisation | yes while no intermediate leaves the doubles (own range 2^-128 .. 2^128) | **no**: a part below alpha becomes an alpha-sized blob | yes (range check, box margin) |
| Wrap simplification (`simplify_wrap`, `Reducer`) | quadric bound (0.5 offset)^2 as a `float` priority; hash cells `max(k * mean edge, ZeroTolerance)`; slab gap = mean edge | all / local / abs | **no** above an extent of about 2^63 (F5; the T3 session fixes the `Reducer`) | no | hash origin at the bounds minimum |
| Spatial queries (`DMeshAABBTree3`) | box padding 50 * `Epsilonf`, ray window `ZeroTolerancef`, nearest-point tie window 100 * `ZeroTolerance` | abs | **no** for small scenes (F3) | -- | yes |

Read by column: the exact resolve is the only stage that is invariant in all three senses. Every other stage is relative to all
triangles, with three patches on top: the far-part mode (largest component, #420 and the first-stage change of F1), the unit copy
outside [2^-100, 2^100] in three places, and the unit copy of the solidify (#537).

## 2. Failure classes

**S: 2^k scale invariance inside the supported range.**
- S1, absolute constants: F3 (spatial queries), the fallbacks and the `Absolute` mode of `MeshAutoRepair`, `ZeroTolerance` floors of hash
  cells.
- S2, a window switch: code that runs unscaled inside an exponent window and on a unit copy outside it has two code paths with a seam
  (F4 was one). Section 3.3 measures another one.
- S3, powers of lengths: squared, cubed and fourth powers overflow or underflow far inside the coordinate range (a cube at an extent of
  2^345 is 2^1035).
- S4, `float` intermediates: F5 (the `Reducer`'s priorities), the full-grid solidify path.

**D: scale disparity between parts.**
- D1, erosion: a tolerance relative to all triangles is larger than a part's features (F1c; fixed for the far-part mode only).
- D2, loss with `true`: a part below the dust thresholds (1e-3 of the largest for the lost-part check, 1e-4 * diag in
  `collapse_slivers`, a cell, alpha) or an open part is removed and `Apply()` is true. Section 3.1 measures it.
- D3, the criterion: with parts far apart, real triangles are degenerate by `NotCleanReason`, so a perfect result is "not clean"
  (`ScaleDisparity`, `Apply()` false). This is the documented meaning of the public criterion.
- D4, the returned mesh on false: eroded (F1b).
- D5, resolution of the resampling stages: one cell size or one alpha for the scene.

**T: translation.** The floor of the tolerances and the ulp term of the rounding grow with the distance from the origin; the solidify
and the wrap give another mesh for every position (section 3.2). Unlike 2^k scaling, a translation is not exact in doubles in general.

**G: a guard gap (not a scale class, found on the way).** The wrap fallback is accepted without a volume or part check by design; with
far parts it returns `true` with one blob that spans the scene (section 3.1).

## 3. Falsification run

A temporary xunit probe (not committed) on procedural scenes, built like the campaign generators (`RepairCampaignTests.Scene`,
`DegenerateFamilies`): a sphere of radius 1 at the origin (`Sphere3Generator_NormalizedCube`, coordinates rounded to multiples of
2^-12 so that all scenes are dyadic) and a small part of half size 2^-r at distance 2^f along x. Small part: a clean box (v0), two
overlapping boxes (v1, self-intersecting), a sphere with 8 triangles removed (v2), a clean sphere (v3). r in {2, 6, .., 30}, f in
{2, 12, 22, 30}, only r + f <= 40 (the part's extent stays above 2^-40 of its magnitude). `MeshRobustRepair` with the campaign's
coarse fallback settings (`SolidifyResolution` 32, `WrapRelativeAlpha` 16, `WrapRelativeOffset` 160); the solidify (resolution 64,
adaptive) and the wrap (20 / 200) also on their own. Volumes are those of the result's triangles near a part, over the part's
expected volume. net10.0, Release, linux x64, one run (the runs are deterministic).

### 3.1 Disparity: whole scene against part by part

"Part by part" is the upper bound of option B: the two parts are repaired separately with the same settings and appended.

| Stage, small part | Whole scene | Part by part |
|---|---|---|
| repair, clean box (v0) | kept with `true`: f = 2 and r <= 14; f = 12 and r <= 6. `false`, `ScaleDisparity`, part kept: f = 12 and r = 10..18; f >= 22 and r <= 14. **Small part deleted with `true`** (path: exact resolve): r >= 18 at f = 2, r >= 22 at f = 12 | both parts 1.0000 in all 23 cases, both `true` without a fallback |
| repair, overlapping boxes (v1) | kept with `true`: f = 2 and r <= 14; f = 12 and r <= 6. `false`, `ScaleDisparity`, kept: f = 12 and r = 10, 14; f >= 22 and r <= 14. **Deleted with `true`**: every case with r >= 18 | 1.0000 in all cases; the small part by the exact resolve |
| repair, open sphere (v2) | kept with `true`: f = 2 and r <= 14; f = 12 and r = 2. **Deleted with `true`**: f = 12 and r = 6..26, f = 2 and r >= 18. `false`, `ScaleDisparity` with an eroded mesh (large sphere at 0.25 % of its volume, small part at 2 to 4 %; F1b): f = 22 and r = 2, 6; f = 30 and r = 2, 6, 10. **`true` with one wrap blob** (no triangle near either part): f = 22 and r = 10, 14, 18 | 1.0000 in all cases, hole filled by the repair stage |
| solidify alone, open sphere (v2) | f = 2: small part kept only at r = 2, gone from r = 6; f >= 12: empty result with `Compute()` true | 0.9998 in all cases |
| wrap alone, clean sphere (v3) | f = 2: r = 2 kept (1.06 / 1.20), r = 6 a 6-triangle blob of 4.8 times the volume, r >= 10 no triangle near the small part; f >= 12: `Success` with no triangle near either part (one blob at the scale of alpha) | 1.049 in all cases (the offset), as for a part alone |

- The whole-scene behaviour is not monotone in the disparity: `true` and kept, then `false` and kept, then `true` and deleted. The
  deletion is the documented dust rule (D2), but for a caller it is a silent loss of a clean closed part.
- The union of the per-part results is closed, manifold and free of self-intersections in every case. `NotCleanReason` of the union
  is "degenerate triangles" exactly where a triangle of the small part is below 1e-12 * diag^2 of the scene (15 to 19 of the 23 cases
  per row): D3 is the only thing between the per-part result and "clean".
- The small part repaired in place (at 2^f) and at the origin agree to 4 digits in volume everywhere; in place is bit-identical to
  the origin for the repair paths (section 3.2).

### 3.2 Translation

One part of size 1 moved by 2^k (1, 1, 1), k = 1 .. 39 (exact on the dyadic input, checked), the result moved back and compared
with k = 0.

| Stage | Result |
|---|---|
| repair, overlapping boxes (exact resolve) | bit-identical for all k |
| repair, open sphere (hole fill) | bit-identical for all k |
| solidify alone | a different mesh at every k (4914 to 4944 triangles for 4928), volume within 6e-6, 2e-4 at k = 39 |
| wrap alone | a different mesh at every k (same 800 triangles to k = 36, vertices differ by 1.5e-2; 790 triangles at k = 39), volume within 2e-4 |

So T is a property of the resampling stages; the vertex-preserving stages and the exact resolve passed. The run is small (two
scenes); the floor `16 eps magnitude` (5e-4 of the part at k = 39 with the 2x of the crack merge) did not bite on them.

### 3.3 2^k shifts over the supported range (new findings)

The scene "unit sphere and a small part of half size 0.25 at distance 8", scaled by 2^s, the result scaled back and compared bit for
bit with s = 0. All shifts are inside [2^-200, 2^400].

| Scene | Differs from s = 0 |
|---|---|
| two clean closed parts (v0) | s = -100 .. -70: the path changes from "accepted as is" to the exact resolve, with another vertex order (same volume). Identical at -110 and below, and from -60 up |
| open small sphere (v2) | s <= -48 and s >= 345: another hole-fill triangulation (volume within 3e-5); s = -100 .. -70: the solidify path (380 triangles instead of 600, volume +3.2 %) |
| overlapping boxes (v1), first sweep | none of 25 shifts in -190 .. 390 |
| solidify alone | none of 25 shifts (as #537 claims) |
| wrap alone | identical inside its own range; `InvalidInput` outside 2^-128 .. 2^128, as documented |

The window -100 .. -70 is the same for two scenes and coincides with the seam of the `[2^-100, 2^100]` unit-copy switch (S2); the
thresholds near -48 and 345 fit a power of a length leaving the doubles (S3). Neither was located to a line: that is a bugfix task,
and under option A it is not needed. These are in-range violations that the campaign does not reach (its shifts are -20 .. 10 and
2^24, 2^32, 2^40).

## 4. Prior art

Papers only; no code was read. "Full text" marks what was read in the paper, "abstract" what the abstract states, "from memory"
what could not be opened in this session (most paper hosts were blocked) and should be checked before it is relied on.

- **Global, box-relative parameters are the norm in the resampling family.** Hu et al. 2018 (*Tetrahedral Meshing in the Wild*, ACM
  TOG 37(4)) and Hu et al. 2020 (*Fast Tetrahedral Meshing in the Wild*, ACM TOG 39(4)), full text: envelope `eps = b / 1000` and
  target edge length `l = b / 20` of the bounding-box diagonal `b`; an oversized envelope "fails to capture the features"; the
  sizing field may vary in space, the envelope may not. Neither discusses a per-component envelope or scale disparity.
  Chu, Pan, Liu & Wang 2019 (*Repairing man-made meshes via visual driven global optimization with minimum intrusion*, ACM TOG
  38(6)), full text: the input is normalised to a unit bounding box at the origin, then vertices are merged within a threshold; very
  small patches are treated as probable noise by a bound on the zoom; their principle of minimum intrusion (keep original
  structures, big and small) is the principle of option B: a part that is clean is not touched by the defects of another.
- **Alpha wrap.** Portaneri, Rouxel-Labbé, Hemmer, Cohen-Steiner & Alliez 2022 (*Alpha wrapping with an offset*, ACM TOG 41(4)),
  abstract: the output is watertight, orientable and strictly encloses the input; cavities smaller than alpha are not entered. One
  global alpha and offset (absolute lengths, chosen as fractions of the box diagonal in the experiments: from memory). So a far small
  part cannot be lost by the wrap, only merged into an alpha-sized blob, which is what 3.1 measures. Locally adaptive variants
  exist as research: Cazals, Giesen, Pauly & Zomorodian 2005 / 2006 (*Conformal alpha shapes*, SPBG 2005; *The conformal alpha
  shape filtration*, The Visual Computer 22(8)), abstract: a per-point alpha scale that is invariant under scaling, for
  non-uniform sampling; and a 2024 CAGD paper on shrink wrapping with adaptive alpha (title only). Neither is a drop-in for the
  wrap's guarantees; this is why D5 inside one part stays open.
- **No tolerance at all, by exact arithmetic.** Zhou, Grinspun, Zorin & Jacobson 2016 (*Mesh arrangements for solid geometry*, ACM
  TOG 35(4)), abstract level: exact intersections, no general-position assumption, but the input must have a piecewise-constant
  winding number; rounding the output can reintroduce intersections (from memory). This is the library's exact resolve with
  SafeRounding, the one fully invariant row of section 1. Attene 2010 (*A lightweight approach to repairing digitized polygon
  meshes*, The Visual Computer 26(11)), abstract: a single watertight manifold without degenerate or intersecting elements, by local
  edits; from memory: degeneracy is an exact test (no epsilon), and only the largest component is kept, the extreme form of D2.
  Attene, Campen & Kobbelt 2013 (*Polygon mesh repairing: an application perspective*, ACM Computing Surveys 45(2)), from memory:
  local tolerance-based methods have no right epsilon where gap width and feature size overlap; global methods are valid but
  resample and lose what is below their resolution. That is the split between classes D1 and D5.
- **Scale-free reference lengths.** Local feature size (Ruppert 1995, J. Algorithms 18(3): the smallest disk touching two
  non-incident features; Amenta & Bern 1999, Discrete Comput. Geom. 22: the distance to the medial axis; both from memory) scales
  with the input, so a tolerance relative to it is similarity invariant, but it tends to zero exactly at cracks, duplicate faces and
  self-intersections, where a repair needs its tolerance. This is the weakness of option C. The well-separated pair criterion of
  Callahan & Kosaraju 1995 (J. ACM 42(1); definition from memory): two sets are well separated when they fit in two balls of radius
  r at distance at least s r, a pure ratio. It is the separation rule of option B, stage 2.
- **Not found** (four queries): per-component or hierarchical normalisation in mesh repair, repair of scale-disparate scenes,
  snap rounding with a local pixel size (Devillers, Lazard & Lenhart 2020, *Rounding meshes in 3D*, uses one uniform grid), or
  translation invariance of repair at large coordinates.

Conclusion: the literature normalises by the whole box or uses one global parameter, and accepts the loss of small parts; exactness
removes tolerances only for valid input. A decomposition into independent parts with an exact check of the union is not taken from
a paper; its building blocks (exact predicates, ratio criteria) are standard.

## 5. Options

### 5.1 Option A: one unit frame for the whole `Apply()`

`Apply()` scales the input by a power of two to an extent in [1, 2) (`MeshRepairMetrics.unit_scale`, exact), runs every stage in that
frame and scales the result back. This is #537's fix moved from one stage to the pipeline.

- **Guarantee.** Two inputs that differ by 2^k are the same input in the frame, so every stage, tolerance, `float` cast and power sees
  the same numbers: invariance bit for bit by construction for all of S, including causes not yet found (3.3), for every scale in the
  supported range. Inside the range the scaling is exact both ways (magnitudes in the frame are between about 2^-601 and 2^41, no subnormal
  and no overflow).
- **Does not fix** D, T or G. The absolute constants stay, as fixed fractions of the extent (the F3 limit "pieces of at least 2.4e-4"
  becomes "of at least 2.4e-4 of the extent").
- **Behaviour.** A scene whose result is scale-invariant today keeps its result bit for bit (3.3: all shifts between -44 and 340 on
  the probe scenes); only results inside today's broken windows change, to the result of the unit scale.
- **API.** None. Points to handle, all inside `MeshRobustRepair`:
  - lengths in user units are scaled into the frame: `RoundingAbsoluteTolerance`, and after `ConfigureRepair` the absolute fields of
    `MeshAutoRepair` (`RepairTolerance`, `MinEdgeLengthTol`, a finite `CleanupReferenceDiagonal`);
  - `RepairF` is a user function on user coordinates: it runs before the frame (or the frame is skipped when it is set);
  - the public `Solidify` and `AlphaWrap` objects and `ExactRoundingEdits` are in the frame after `Apply()`: scale their results back
    or document the frame; `ConfigureWrapQuality` sees frame units;
  - `NotCleanReason` (public, static) is not touched; `Apply()`'s result is checked by it after scaling back, where its own two code
    paths apply. If that check disagrees with the check in the frame, `Apply()` reports the outer one (semantics unchanged).
- **Cost.** S: one PR, a copy and two passes over the vertices. Tests: the campaign's shifts widened to the full range, the scenes of
  3.3 as fixed examples (they fail today), `Similarity.All()`, reuse.
- **Interaction with T3.** The repair then never calls the `Reducer` above an extent of 2; the `Reducer` fix stays needed for its
  other callers.

### 5.2 Option B: part frames

The scene is split into parts that cannot interact, each part is repaired by the whole existing pipeline in its own frame, and the
union is verified exactly.

1. **Parts.** Connected components are linked when they may need each other; the classes of that relation are the parts.
   Stage 1 rule, free of tolerances: two closed components are linked when their bounding boxes intersect (an exact comparison,
   which covers overlapping and nested components); every open component is linked to all others (one part, as today). Stage 2: open
   components are linked by distance relative to their own size (the well-separated criterion of section 4), which needs one
   dimensionless factor.
2. **Frame of a part.** Scale by 2^k to an extent in [1, 2) and translate per axis by the part's box minimum where that is exact for
   every vertex of the part (checked; it is exact when the part lies further from the origin than about its own extent on that axis,
   otherwise the axis is not translated and the coordinates are within a few extents of zero anyway). Original vertices map back
   exactly. New vertices (intersection points, contour and wrap vertices) are rounded to the grid of the destination, an error of
   half an ulp there, which no formulation avoids.
3. **Repair** each part with the existing stages. All quantities "relative to all" of section 1 become relative to the part.
4. **Verify the union.** Each part's result is checked in place (`NotCleanReason` criteria other than the area, exact), and pairs of
   results from different parts are tested for intersection (boxes first, then the exact triangle tests of the existing
   self-intersection check). If two results intersect, for example two wraps closer than their offsets, the two parts are merged and
   repaired together. Merging is monotone, so this ends, at worst with today's whole-scene repair.
5. **Status.** `Apply()`'s value stays "the returned mesh is clean by `NotCleanReason`". For far parts that is false with
   `ScaleDisparity` (D3) unless option D is taken. Per-part outcomes go to new output fields.

- **Guarantee.** D1, D2, D4 and D5 disappear between parts: a part's result does not depend on the size or the distance of any other
  part, by construction (3.1 is the measured upper bound: 100 %). T: for translations that are exact on the part and keep it in the
  translated branch, the part is the same input in its frame, so its result is identical up to the final rounding; S per part, as A.
  The union's cleanliness is certified by exact tests, not assumed from the separation.
- **Does not fix.** Disparity inside one part: a small closed component inside the box of a large one (a screw in a hull) and all
  open pieces in stage 1. There today's far-part mode remains. D3.
- **Behaviour.** Results of multi-part inputs change: each part gets its own resolution in the solidify and the wrap (more triangles
  for small parts, more time: one fallback per part instead of one per scene), its own dust thresholds and its own volume check
  (a part can no longer hide below 1 % of the scene's volume). Single-part inputs are untouched.
- **API.** Additive: an opt-in switch first (default off until the A/B), per-part reports. `MeshRepairMetrics` and `NotCleanReason`
  unchanged. The far-part mode (`ScaleRetry`, the 64-component rule) becomes the fallback inside a part and can shrink later.
- **Cost.** M for stage 1 (splitting, frames, the pair test, the merge loop, tests: the scenes of 3.1 as examples, a
  disparity campaign over r and f, invariance, determinism of the part order), L with stage 2.
- **Gate before building.** Count on the robust benchmark cohort how many inputs have two or more stage-1 parts, and how many of the
  183 strict misses do. If the share is negligible, B is a correctness feature for assemblies and scenes only, and is sized as that.

### 5.3 Option C: local tolerances inside every stage

Each length tolerance becomes a fraction of a local reference length (the component's diagonal, the median edge of the component, the
edges at the element), and degeneracy becomes a dimensionless shape measure. This also covers a small feature inside a large part,
for the vertex-preserving stages.

- **Guarantee.** Similarity invariance of each rewritten test by construction; no guarantee for the solidify and the wrap, whose
  resolution is global by their algorithm (a locally adaptive wrap is research, section 4).
- **API.** `MeshAutoRepair`'s relative tolerances are documented as fractions of the diagonal: a new `ToleranceModes` value and new
  opt-in fields in `MeshPostRepairCleanup`; defaults unchanged.
- **Cost.** L, invasive (every row "all" of section 1 in five classes), with a behaviour change per row and no single place to test
  the invariance. A local reference length is also zero or meaningless exactly at defects (duplicate faces, cracks), so each
  tolerance needs its own fallback.

### 5.4 Option D: a scale-free cleanliness criterion next to `NotCleanReason`

A triangle is degenerate when it is exactly collinear (exact predicate) or below a dimensionless quality (the `Reducer`'s
`4 sqrt(3) area / sum of squared edges`, or the aspect ratio `MeshPostRepairCleanup` already uses), not when its area is small
against the scene. Closedness, bowties and self-intersections are already free of scale.

- **Guarantee.** The criterion is similarity invariant and local: a part's verdict does not depend on other parts.
- **API.** A new method (or an optional parameter with today's default); `NotCleanReason` and `Apply()`'s meaning unchanged. Whether
  `Apply()` may return true by the new criterion (an opt-in property) is the owner's decision: it turns today's
  `false` / `ScaleDisparity` for far parts into `true`.
- **Cost.** S. Useful only with B (or as a diagnostic): alone it changes a status, not a mesh.

### 5.5 Comparison

| | S (2^k) | D between parts | D inside a part | T | Status of far parts (D3) | API | Cost |
|---|---|---|---|---|---|---|---|
| A unit frame | by construction, whole range | -- | -- | -- | -- | none | S |
| B part frames | by construction (per part) | by construction, union certified | far-part mode as today | exact translations, up to the final rounding | unchanged (false) | additive, opt-in | M (stage 1), L |
| C local tolerances | per rewritten test | vertex-preserving stages | vertex-preserving stages | -- | unchanged | additive modes | L |
| D criterion | -- | -- | -- | -- | true, if the owner opts in | additive | S |

## 6. Recommendation

1. **A now**, as a bugfix-class PR of its own: it closes S for the repair in one place, including the two new in-range windows of
   3.3 and whatever remains of F5 after the `Reducer` fix, and it makes the supported-range statement of QA.md 2.5a true by
   construction instead of by sweep. The campaign shifts then cover the whole range.
2. **G separately**, small: a wrap (or solidify) result accepted with `true` should at least pass the lost-part check relative to
   the largest component; today a blob spanning the scene passes (3.1). This is the open "measure first" item of the far-part
   decision of 2026-11-08, now with a procedural reproduction.
3. **B stage 1 after its gate** (the count of multi-part inputs on the cohort), opt-in, with the A/B on the robust set before the
   default changes. It replaces fixing D gap by gap: the far-part mode, `ScaleRetry` and the dust thresholds stop being the line of
   defence between parts.
4. **D with B**, if the owner wants `true` for a correctly repaired scene with far parts.
5. **C not now.** Its only gain over B is disparity inside one part, at the cost of rewriting every stage; revisit with data from B
   (how many real inputs have that shape).

What stays out of reach is stated in the class documentation: a feature below the resolution of the solidify or the wrap inside one
part, and bit-identical results under translations that are not exact on the input.

## 7. Owner questions

1. A: go ahead as its own PR? Should the public `Solidify` / `AlphaWrap` results after `Apply()` be scaled back to user
   coordinates (recommended) or documented as being in the unit frame?
2. D2: is the dust rule (a clean closed part below about 1e-3 of the largest is deleted with `true`) wanted? Under B it disappears
   between parts; until then it could be reported (`LostFarPart`) instead.
3. B: opt-in first (recommended) or default after the A/B? Stage 1 only, or also stage 2 (open pieces by relative distance)?
4. D: may `Apply()` return true by a scale-free criterion (opt-in), or does far-part success stay `false` / `ScaleDisparity` with
   per-part reports?
