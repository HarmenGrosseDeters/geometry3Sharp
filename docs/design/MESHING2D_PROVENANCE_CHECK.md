# 2D meshing: provenance check (stage 6b)

Stage 6b of the [roadmap](../roadmap/meshing2d.md#stage-6b-meshing-provenance-check): a separate session (the dirty side, the only one that read the reference
implementations) compared the clean-room 2D mesher with them for copied expression before the BSL-1.0 release. This file is
the **clean part** of the report: it names only our files, routines and lines, the class of each finding and, where a rewrite
would be needed, the paper or spec item to follow. It holds no code, names, structure or line references of the reference
implementations, so a clean session may read it. The evidence on the reference side is kept outside this repository.

Algorithms from the papers are free to implement; expression (data layout, decomposition, control flow of distinctive routines,
constants, names, comments, messages) is not. Classes: **(A)** copied or closely paraphrased expression, a rewrite is needed;
**(B)** a similarity explained by the paper or the problem: same algorithm or interface, independent expression; **(C)** no finding.

## Result

**No (A) finding.** Every routine in scope is (B) or (C). No rewrite is needed. Four precision edits to the provenance log
(end of this file) are applied with this report; none was needed for the release.

## Scope, versions, method

- **Our side**: master `a988a21` (also the state at the start of the check). `comp_geom/DelaunayKernel2.cs`,
  `DelaunayKernel2.Constraints.cs`, `DelaunayMesh2.cs`, `TriangulationMesh2.cs`, `Triangulation2Result.cs`, `ConstrainedMesh2.cs`,
  `ConstrainedMesh2Types.cs`, `ConstrainedDelaunayMesh2.cs`, `QualityMesh2.cs`, `SnapRounding2.cs` (5872 lines), and the oracle
  `benchmarks/Meshing2Benchmark/Oracle/*.cs` (lower priority, token comparison and a read of its error bounds only).
- **Reference side read**: Triangle.NET, upstream `github.com/wo80/Triangle.NET` (MIT), commit
  `fa6f2fdbbbfa9312e92929bd8fbb9c21da0f7769` (2026-06-10), the library project `src/Triangle` (193 C# files).
- **Triangle 1.6 (`triangle.c`, `triangle.h`)**: not readable in the first pass (both official hosts refused by the egress
  proxy, not circumvented), so Triangle.NET, a close C# port, served as the proxy for both. Read directly in a second pass,
  stage 6b-2, below.
- **Similarity tool**: JPlag was planned (C# against Triangle.NET), but running the downloaded JPlag jar was refused by the
  session's permission policy, so a token-normalised comparison was scripted instead (Python 3, own code, reads files as data):
  comments and strings stripped, identifiers mapped to one token, numbers to one token, keywords and punctuation kept; for each
  of our files against each reference file the share of our tokens covered by a shared 12-token sequence, and the longest
  run of covered tokens (overlapping matches merge, so it is an upper bound on a single shared sequence); in addition shared
  comment word 6-grams (verbatim, lower case), shared string literals, shared numeric literals and shared identifiers of six or more letters. Calibration: the same run on unrelated upstream geometry3Sharp files
  (`Arrangement2d.cs`, `ConvexHull2.cs`, `DMesh3.cs`, `Polygon2d.cs`, written years before this work) gives the noise floor.
- **By hand**: the distinctive parts of a Delaunay refinement mesher (point location, insertion and flips, segment insertion and
  recovery, segment crossings, hole and region fill, encroachment, the bad-triangle test and queue, the small-input-angle
  exemption, the subsegment split rule, Steiner point placement) were compared with ours for structure, control flow, constants,
  names, comments and messages.

## Tool result

| Our file | tokens | best-matching reference file: share in shared 12-grams | longest run of tokens covered by shared 12-grams | shared comment 6-grams |
|---|---:|---:|---:|---:|
| `DelaunayKernel2.cs` | 4777 | 10.3 % | 34 | 0 |
| `DelaunayKernel2.Constraints.cs` | 3808 | 8.0 % | 19 (the `using` block) | 0 |
| `DelaunayMesh2.cs` | 1160 | 10.4 % | 22 | 0 |
| `TriangulationMesh2.cs` | 2317 | 6.4 % | 24 | 0 |
| `Triangulation2Result.cs` | 115 | 51.3 % (an enum and a constructor of three assignments) | 22 | 0 |
| `ConstrainedMesh2.cs` | 1310 | 10.7 % | 52 | 0 |
| `ConstrainedMesh2Types.cs` | 331 | 23.0 % (field and list declarations) | 16 | 0 |
| `ConstrainedDelaunayMesh2.cs` | 12548 | 7.7 % | 19 (the `using` block) | 0 |
| `QualityMesh2.cs` | 10270 | 5.2 % | 24 (a generic `(a*b - c*d) / d` then `new Vector(x + ...)`) | 0 |
| `SnapRounding2.cs` | 8970 | 5.7 % | 26 | 0 |
| oracle, 13 files | 76 961 | 2.0 to 19.3 % | up to 85 (a constructor of four field assignments) | 1, the phrase "inside the circumcircle of the triangle" |
| *calibration: unrelated upstream files* | 22 900 | *6.9 to 14.3 %* | *up to 83* | – |

Our mesher is at or below the noise floor of unrelated code. The longest shared runs were inspected: `using` blocks, field
declarations, constructors that assign their parameters, loop headers and a generic two-product formula; none is a distinctive
sequence. No string literal or error message is shared. Shared identifiers are .NET names and the field's vocabulary
(`vertex`, `segments`, `regions`, `marker`, `incircle`, `orient`, `apex`, `subsegment`, `encroached`), which the papers use too.

## Findings per routine

Lines are those of master `a988a21`.

### Kernel (M1)

| # | Our file : lines | Routine | Class | Category | Note |
|---|---|---|---|---|---|
| K1 | `DelaunayKernel2.cs` 17-65, 212-215 | storage: `V`, `H`, half-edge `3t + k`, `Next` by arithmetic | B | structure | A triangle array with one twin index per half-edge is the generic triangle-based representation ([CDS] ch. 3); no orientation bits, no pointers, no successor tables; constraints are a per-half-edge id (`Seg`). The `e % 3 == 2 ? e - 2 : e + 1` step is the common one of half-edge arrays and also appears in permissively licensed libraries; it is not taken from the reference. |
| K2 | `DelaunayKernel2.cs` 82-160 | `InsertionOrder`, `GridRanks`, `HilbertIndex`, `Mix` | C | – | BRIO over coordinate ranks along a Hilbert curve ([ACR], [B71]); no counterpart. |
| K3 | `DelaunayKernel2.cs` 169-210 | `Build`, `CreateFirst`: first triangle plus three ghosts | C | – | Ghost triangles, no invented coordinate ([CDS] 3.4); no counterpart in this form. |
| K4 | `DelaunayKernel2.cs` 240-290, `DelaunayKernel2.Constraints.cs` 94-145 | `Locate`, `LocatePoint`: visibility walk with a random first edge (xorshift), scan fallback | C | control flow | The stochastic visibility walk of [DPT]; the reference's walk is different. |
| K5 | `DelaunayKernel2.cs` 297-372 | `Conflict`, `GhostConflict`, `Cavity`: Bowyer-Watson cavity, k triangles become k + 2 | C | – | Bowyer-Watson cavity ([BW], [CDS] 3.3); no counterpart in this form. |
| K6 | `DelaunayKernel2.cs` 402-549 | `CanonicalizeTies`, `Fan`, `HullIsConvex` | C | – | Decision D18 and our own verification; no counterpart. |
| K7 | `TriangulationMesh2.cs` 112-152 | `TryLocate`: jump to the nearest of n^(1/3) sampled triangles, then walk | B | control flow | Jump-and-walk with about n^(1/3) samples is [MSZ]'s scheme, shared for that reason. Ours: deterministic stride sampling, the walk of K4 without randomness, and a smallest-id rule (spec R7, F-62); sample count and selection differ. |
| K8 | `TriangulationMesh2.cs` 187-245 | `IsValid` | B | control flow | Orientation, mutual neighbours, local Delaunay: the textbook embedding check ([CDS] ch. 2); ours adds the single convex boundary cycle and is exact. |

### Constrained phase (M2)

| # | Our file : lines | Routine | Class | Category | Note |
|---|---|---|---|---|---|
| C1 | `DelaunayKernel2.Constraints.cs` 154-276 | `InsertPoint`, `SplitTriangle`, `SplitEdge`, `Legalize`, `Flip` | B | control flow | 1-to-3 and 2-to-4 splits followed by Lawson flips that stop at constraints are the textbook incremental insertion ([L77], [CDS] ch. 3, [C89]). Ours keeps a stack of half-edges opposite the new vertex and lets ghosts take part; the reference's control flow differs. A split constrained edge keeps its constraint on both halves, as the problem requires. |
| C2 | `DelaunayKernel2.Constraints.cs` 281-305 | `FindEdge`, `Star` | C | – | Rotation through twins. |
| C3 | `DelaunayKernel2.Constraints.cs` 314-376 | `RecoverEdge`, `RestoreDelaunay`, `NonDelaunayEdges` | C | control flow | Sloan's recovery ([S93]): a queue of crossed edges, flip if the quadrilateral is strictly convex, else re-queue, then Lawson flips over the new edges. The reference recovers segments by a different method. |
| C4 | `ConstrainedDelaunayMesh2.cs` 491-559 | `InsertPiece`: wedge at `u` holding the direction to `w`; split at a collinear or near vertex; walk collecting crossed edges | B | control flow | Finding the triangle at the first end whose wedge holds the segment, splitting the segment at a vertex that lies on it, and stopping at a crossed constraint follow from the problem ([S93] walk; spec O10, O11). Ours scans the star once with two orientations per wedge, queues pieces on an explicit stack (F-2, no recursion) and adds the tolerance tests of D3; decomposition and control flow differ from the reference. |
| C5 | `ConstrainedDelaunayMesh2.cs` 561-665 | `Cross`, `NearVertex`, `Intersection`, `RatioToDouble` | B | control flow | Splitting both segments at their crossing is the problem (spec O10). Ours computes the point as an exact integer ratio rounded to nearest-even (D1); the reference computes it differently. |
| C6 | `ConstrainedDelaunayMesh2.cs` 669-873, `QualityMesh2.cs` 179-222 | `RingParity`, `InsideFromScratch`, `Output`, `SeedCell`, the outside fill of `ConstrainHull` | B | control flow | "Triangles reachable from outside, or from a hole seed, without crossing a constraint" is the domain definition (spec I4, I6, D5, D6); any CDT mesher floods it. Ours: a breadth-first search from the ghosts that carries the set of rings each triangle is inside (even-odd), nesting read from the sets, then per-seed cells. No shared structure or naming with the reference. |
| C7 | `ConstrainedMesh2Types.cs` 8-76 | input model: points, segments with marker, rings, hole seeds, regions (seed or ring, label, maximum area, last wins), heights | B | structure | The concepts (segment markers, hole seed points, regional attributes with an area bound, "the last one wins") are those of the de-facto input format of the field, which the spec adopts on purpose (I2, I4, I6: "as in the original C program"). A functional interface; names and layout are ours (rings, locked segments, ring regions, heights have no counterpart). |
| C8 | `ConstrainedDelaunayMesh2.cs` 949-1075 | `Covering`, `NearOtherSource`, `BuildGrid`, `Traverse` | C | – | Tolerance tube and grid ([AW87], D7); no counterpart. |

### Refinement (M3)

| # | Our file : lines | Routine | Class | Category | Note |
|---|---|---|---|---|---|
| Q1 | `QualityMesh2.cs` 110-170 | `Run`: encroached subsegments first, then bad triangles, FIFO queues, Steiner limit, final Delaunay restore | B | control flow | The order "subsegments before triangles" is Ruppert's ([R95] §3, [S02] §3). Ours uses two plain FIFO queues; the reference's queueing differs. |
| Q2 | `QualityMesh2.cs` 341-372 | `Encroached`, `DotSign`: a vertex of an adjacent triangle strictly inside the diametral circle, i.e. a negative dot product | B | control flow | The dot-product test of the angle at the apex is the definition of diametral-circle encroachment ([R95] §2). Ours is exact (filter, then integers), loops over both sides and ignores triangles outside the domain; otherwise independent. |
| Q3 | `QualityMesh2.cs` 376-470 | `Local`, `MinSin2`, `Area`, `LongestLocked2`, `Bad` | C | – | sin² of every angle from area² / (l1 l2) on a power-of-two-scaled triangle, area per region tag, size function at the centroid, the locked-edge needle exemption; computed differently from the reference. |
| Q4 | `QualityMesh2.cs` 471-482, 650-700 | constrained small angle, `SpansSmallAngle`, `SourcesAt` | B | control flow, constant | Not refining a skinny triangle whose shortest edge spans a small input angle is the rule of [S02] §4. Ours tests the input angle against the bound, that the lines meet on both segments, and distances to the apex within a factor of about 1.5 (2.24 on squares, line 685); test and tolerance differ from the reference. |
| Q5 | `QualityMesh2.cs` 723-773 | `Circumcentre`, `SplitTriangle` | C | control flow | Plain circumcentre on a scaled triangle; a walk decides whether a constraint blocks, and encroachment is found before insertion (Q6). Control flow differs from the reference. |
| Q6 | `QualityMesh2.cs` 775-823 | `Walk`, `EncroachedByPoint` | C | control flow | Cavity search for the subsegments a circumcentre would encroach ([R95] §3, [S02] §3); no counterpart in this form. |
| Q7 | `QualityMesh2.cs` 825-897, lines 836-843 | `SplitSegment`: midpoint, or the power of two nearest to half the length when exactly one end is a pre-refinement vertex, accepted in (L/3, 2L/3); computed on the input segment | B | constant, control flow | The power-of-two split with pieces in ratio at most 2:1 is concentric-shell splitting ([S02] §4.1; [CDS] ch. 6-7); the window (L/3, 2L/3) is that ratio. Ours takes the power of two from the bits of L/2, applies it only when exactly one end existed before refinement, measures on the original input segment (O12); the computation and the end condition differ from the reference. |
| Q8 | `QualityMesh2.cs` 287-332, 229-263 | `Collect`, `TagFromPiece` | C | – | Our own tag propagation. |
| Q9 | `QualityMesh2.cs` 486-648 | `BelowFloor`, `ConstraintInPixel`, `TrianglesNear`, `GridSplitPoint`, `ValidGridSplit` | C | – | Refinement on the snap grid (section 3g of MESHING2D_SNAP.md); no counterpart. |
| S1 | `SnapRounding2.cs` | snap rounding with restore | C | – | Hobby 1999, Belussi et al. 2016; the reference has no snap rounding. |

### Oracle (`benchmarks/Meshing2Benchmark/Oracle`)

| # | Our file : lines | What | Class | Category | Note |
|---|---|---|---|---|---|
| O1 | all files | token comparison | C | – | 2 to 19 % in shared 12-grams, at the noise floor; the long runs are constructors and declarations. One shared comment phrase, "inside the circumcircle of the triangle", plain English. |
| O2 | `Oracle.cs` 556-598 | static error bounds of the fast filters | B | constant, name | (3 + 16u)u and (10 + 96u)u are the bounds of [Sh97]. The comment names them by the identifiers of Shewchuk's public-domain predicates code; both are from [Sh97], not from the mesher. See E4. |

## Direct comparison with triangle.c (stage 6b-2)

A second dirty-side session (2026-11-02) compared the mesher with the C original directly; the owner had allowed the hosts for it.

- **Reference side read**: Triangle 1.6, the official archive `triangle.zip` from `netlib.org/voronoi` (sha256
  `1766327add038495fa3499e9b7cc642179229750f7201b94f8e1b7bee76f8480`, files dated 2005-07-28), chiefly `triangle.c`.
  (`www.netlib.org` is still refused by the proxy; the bare host served the same archive. Nothing was circumvented.)
- **Our side**: master `7ed6db7`; the mesher files are unchanged since `a988a21`, so the line numbers above hold.
- **Tool**: the token comparison of the first pass, adapted to C against C# (Python 3, own code, reads files as data): comments,
  strings and preprocessor lines stripped; identifiers, numbers and type keywords each mapped to one token; `->` mapped to `.`;
  share of our tokens in shared 12-grams (8-grams as a second setting), longest covered run, and separately shared comment word
  6-grams, string literals, numeric literals and identifiers of six or more letters. Calibration: the noise floor from the same
  unrelated upstream files as above, and a positive control, Triangle.NET (the known port) against `triangle.c`.
- **By hand**: the routines of the first pass's list, now against the C source, plus what only the C source can show: its memory
  layout, its messages and its comments.

### Tool result

| | share in shared 12-grams (8-grams) | longest covered run | shared comment 6-grams | shared strings |
|---|---:|---:|---:|---:|
| our mesher, 10 files (46 000 tokens) | 4.0 to 10.8 % (27 to 40 %); `Triangulation2Result.cs` 30.4 % (115 tokens, an enum and a constructor) | 18 to 53 | 0 | 0 |
| *noise floor: unrelated upstream files* | *5.8 to 13.5 % (31 to 47 %)* | *up to 38* | *1* | *0* |
| *positive control: Triangle.NET, 7 core files* | *17.9 to 53.7 %* | *27 to 928* | *103 to 1222 per file* | – |

The tool separates the known port clearly; our mesher is at the noise floor of unrelated code. The runs above the floor's 38
tokens were inspected: four consecutive calls of one shape (`DelaunayKernel2.cs` 197-201, `DelaunayKernel2.Constraints.cs` 209-215),
the barycentric height interpolation (`ConstrainedDelaunayMesh2.cs` 368-370) and the exact dot-product sign (`QualityMesh2.cs` 371);
all match repetitive arithmetic of the reference only after normalisation and are generic. No comment phrase and no message is shared.
Shared identifiers are the field's vocabulary, as above.

### Findings

| # | Our file : lines | Routine | Class | Category | Note |
|---|---|---|---|---|---|
| T1 | `DelaunayKernel2.cs` 17-65, 212-215 | storage | C | structure | The memory layout the first pass could not check: ours (K1) shares nothing with the C original's. |
| T2 | all routines K1-K8, C1-C8, Q1-Q9, S1 | as above | as above | – | Each class of the first pass holds against the C source; no routine moves to (A). In particular the small-input-angle test (Q4), the shell split (Q7), encroachment (Q2), the queue (Q1), point location (K7), segment recovery (C3, C4), crossings (C5) and the domain fill (C6) are computed and decomposed differently, with different constants. |
| T3 | `QualityMesh2.cs` 735; `DelaunayKernel2.Constraints.cs` 169 | `SplitTriangle` | B | name | The plain English name of the operation (insert a point that splits a triangle); generic, no rename needed. |
| T4 | `QualityMesh2.cs` 33 | default `MinAngle = 20` | B | constant | 20 degrees is the customary default of Delaunay refinement, just below the ~20.7° bound of [S02]. |

**No (A) finding.** The direct comparison confirms the result of the first pass.

## Edits to the provenance log (no rewrite needed)

The claims of [MESHING2D_PROVENANCE.md](MESHING2D_PROVENANCE.md) match the code; checked row by row against the routines above.
Four places were stated more precisely, applied in the same PR as this report:

- **E1** (M3, `SplitSegment` row): "exactly one end existed before refinement" (code line 838) instead of "next to a vertex that
  existed before refinement", and the acceptance window (L/3, 2L/3) with its source, [S02] §4.1 (pieces in ratio at most 2:1).
- **E2** (M3, `SpansSmallAngle` row): the tolerance as the code has it, a factor 2.24 on squared distances (about 1.5 on
  distances), and that the test also requires the input angle below the bound and the lines to meet on both segments.
- **E3** (M2, `Builder.Output` row): the domain definition of the spec (I4, I6) cited next to the decisions, since the flood fill
  from outside and from seeds is what the definition forces.
- **E4** (oracle section): the two bounds cited as [Sh97]'s, without the identifiers of Shewchuk's public-domain predicates code.
  The same identifiers in the comment at `Oracle.cs` 556-558 are public domain, not mesher expression, and stay as they are.

## Delta check of the mesher as of 2026-11-04 (stage 6b-3)

A dirty-side session (2026-11-04, the reference session of `g3-meshing-references`) repeated the method of 6b-2 on what changed
in the 2D mesher after the first two passes. As above, this section names only our files, routines and lines; the evidence on the
reference side is kept outside this repository.

- **Our side**: master `d9006aa` (after #384). Changed since the state of 6b-2 (`7ed6db7`):
  - `comp_geom/QualityMesh2.cs`: +1026 lines. Off-centres, diametral lenses with the deletion before a bisection, the
    worst-first queue, grading steps 1-2 (candidate choice, petal), the carve before refinement, the pinch fix and the
    point-location hint.
  - `comp_geom/DelaunayKernel2.Deletion.cs`: new, 134 lines, vertex deletion.
  - `comp_geom/DelaunayKernel2.cs`: the location hint and the slot bookkeeping for deletion.
  - `comp_geom/DelaunayKernel2.Constraints.cs`: one line.
  - `comp_geom/ConstrainedMesh2.cs`: hull segments from snapped chains, collapsed outer rings, dropping unused made vertices.
  - `comp_geom/ConstrainedDelaunayMesh2.cs`: hull sources, the walk counter.
  - `comp_geom/SnapRounding2.cs`: `SnapGrid` double filters, collapsed outer rings, hull chains.
  - The oracle: 8 files of `benchmarks/Meshing2Benchmark/Oracle`; token comparison only, as before.

  All mesher files were compared in full, not only the changed lines: 11 files, 57 000 tokens.
- **Reference side read**:
  - Triangle 1.6 (`triangle.zip` from `netlib.org/voronoi`, sha256 `1766327a…f8480`, the archive of 6b-2);
  - Triangle.NET upstream `wo80/Triangle.NET`, commit `fa6f2fdb` (the first pass's version), all 82 C# files of `src/Triangle`.
- **Tool**: the token comparison of 6b-2 with its settings: comments, strings and preprocessor lines stripped; identifiers, numbers
  and type keywords each mapped to one token; `->` mapped to `.`; 12-grams, 8-grams as a second setting; longest covered run;
  shared comment word 6-grams, strings, numbers and identifiers of six or more letters.
  - The script of 6b-2 was not kept, so it was written again from that description (own Python 3 code, reads files as data).
  - Recalibrated before use: noise floor from the same unrelated upstream files, and the positive control Triangle.NET against
    `triangle.c`.
- **By hand**: every routine added or changed since 6b-2, against both references, for data layout, decomposition, control flow,
  constants, names, comments and messages.

### Tool result

| | against `triangle.c`: share in shared 12-grams (8-grams) | longest run | comment 6-grams | strings |
|---|---:|---:|---:|---:|
| our mesher, 11 files | 4.3 to 11.9 % (23 to 47 %); `Triangulation2Result.cs` 33 % (115 tokens, unchanged) | 18 to 53 | 2 (`QualityMesh2.cs`, see D3) | 0 |
| `QualityMesh2.cs` (19 011 tokens) | 5.6 % (33.3 %) | 49 (line 677, the exact dot-product sign, as in 6b-2) | 2 | 0 |
| `DelaunayKernel2.Deletion.cs` (1 211 tokens) | 11.9 % (47.4 %) | 34 (lines 106-109: array moves of a slot) | 0 | 0 |
| *noise floor: unrelated upstream files* | *6.1 to 15.7 % (33 to 49 %)* | *up to 38* | *1* | *0* |
| *positive control: Triangle.NET, 7 core files* | *18.5 to 68.0 % (52 to 94 %)* | *27 to 928* | *155 to 2250 per file* | *–* |

| | against Triangle.NET (82 files): share in shared 12-grams, all files / best single file | longest run | comment 6-grams |
|---|---:|---:|---:|
| our mesher, 11 files | 24.5 to 40.7 % / 5.7 to 14.3 % (two small type files higher: 56.8 % and 88.7 %, enums and declarations) | 32 to 90 | 3 (D3; one `<summary> <param name>` phrase) |
| *noise floor: the same unrelated upstream files* | *38.4 to 53.0 % / 9.3 to 18.7 %* | *47 to 121* | *1* |

The rewritten tool reproduces 6b-2's calibration within a few points (6b-2: noise 5.8 to 13.5 %, positive control 17.9 to 53.7 %,
runs up to 928). It still separates the known port by a wide margin.

The mesher stays at or below the noise floor. That includes the 1026 new lines of `QualityMesh2.cs`, whose share went from 6.2 % to
5.6 %. The new runs at or above 30 tokens were inspected; all are generic:
- the slot moves of `ReleaseSlot`;
- the dot-product sign of 6b-2;
- call sequences and enum lists in the oracle (`Oracle.cs` 159-171, a list of check calls, 107 tokens; `Model.cs` 102-108, the
  violation enum, 93 tokens; the first pass's oracle runs reached 85).

No string literal or message of the mesher is shared.

### Findings

| # | Our file : lines (`d9006aa`) | Routine | Class | Category | Note |
|---|---|---|---|---|---|
| D1 | `QualityMesh2.cs` 192-254 | `TriangleQueue`: a binary heap keyed by sin² of the smallest angle, ties in queue order, FIFO for the rest | C | structure, control flow | Worst first by angle is our own ordering (MESHING2D_GRADING.md); the reference's queue orders by another key with another structure. |
| D2 | `QualityMesh2.cs` 646-660, 683-716 | `Encroached`, `EncroachesOn`, `InLens` | B | control flow, constant | The lens of [S02] §6.4: the apex angle of at least 120° (lens radius \|ac\|/√3), tested as a negative dot product and 4 (pa·pc)² ≥ \|pa\|²\|pc\|². Comparing an angle through a squared dot product is the generic formula. Ours has a fixed 120° lens (the one [S02] proves the 25.65° bound for) and decides it exactly (a filter, then integers). The reference's lens and its parameter differ. |
| D3 | `QualityMesh2.cs` 23 (doc comment); 114 | wording | B | comment | Two comment 6-grams are shared with `triangle.c`. "… a vertex in its diametral lens" is the definition of lens encroachment, in our sentence "a subsegment counts as encroached only by a vertex in its diametral lens". "of the convex hull of the" is plain English. A rewording of line 23 (eg "only a vertex inside the diametral lens encroaches on a subsegment") would remove even the phrase match. It is not needed, since the definition is [S02]'s; it is left to the clean side. |
| D4 | `QualityMesh2.cs` 1138-1183 | `OffCentre`, `HalfCot` | B | control flow, constant | [U04]'s off-centre: on the bisector of the shortest edge, at \|pq\| cot(θ/2) / 2 from its midpoint, used if nearer than the circumcentre. Ours computes the point along the direction to the circumcentre, with cot(θ/2) = (1 + cos θ)/sin θ from square roots and no extra factor, in a 2^k-scaled frame, and only for triangles bad by their angle. The reference's case split, its constant and its distance test differ. The circumcentre formula is the textbook one (Q5). |
| D5 | `QualityMesh2.cs` 1230-1322, 1330-1381, 1383-1437 | `BestCandidate`, `PetalCandidates`, `Score` | B | control flow, constant, name | The petal and choosing among points are [EU07]'s ideas; "petal" is the paper's word. Our candidates (seven arc points by a rational half-angle parameter, quadratically spaced; the circumcentres of up to 64 nearby triangles inside the petal), the score (bad triangles removed less those made) and the floor on the insertion radius are our own (MESHING2D_GRADING.md 4.3). The reference's new-location code computes its point by a different geometric construction, with no candidate list and no score. |
| D6 | `QualityMesh2.cs` 1622-1626, 1675-1731, 1733-1768, 1770-1778 | `DeleteFreeInCircle`, `CheckDeletion`, `InCircleAndVisible` | B | control flow | The rule (delete the free vertices inside the diametral circle that see the midpoint before a bisection) is [S02] §6.4 after Chew 1993, quoted in the comment. Ours: a local search across unconstrained edges in a scaled frame, an exact circle and visibility test against the constrained edges met, then deletion through the kernel. The reference deletes in another way, with another loop and stopping condition, and its own conditions on the subsegment's ends. |
| D7 | `DelaunayKernel2.Deletion.cs` 27-131 | `DeleteVertex`, `IsEar`, `MakeTriangle`, `ReleaseSlot` | B | control flow, name | Vertex deletion: only the star changes ([Dev99]); here by ear clipping with exact orientations ([Mei75]), then Lawson flips over the new diagonals, and the two freed slots are filled by moving the last slots. The reference retriangulates the star's polygon by another method and frees memory in another way. `DeleteVertex` and `MakeTriangle` are plain English names of the operations; also used in the references, in other roles and signatures. No rename needed. |
| D8 | `QualityMesh2.cs` 326-368 | `CarveOutside`, `OutsideOnly` | B | control flow | Removing what lies outside the domain is the domain definition (spec Q7, I4, I6; C6). Ours lifts free constraint pieces with the outside on both sides, restores the constrained Delaunay property, and deletes made vertices whose triangles all lie outside, through D7. Input points stay and the triangulation is kept. The reference removes outside content differently. |
| D9 | `DelaunayKernel2.cs` 41-57; `QualityMesh2.cs` 1513, 1635 | `LocateHint` | B | control flow | Starting a walk at a triangle near the point is the generic remedy for long walks (also [DPT], [MSZ]). Ours passes the triangle the refiner found, replacing a ghost by the real triangle across its hull edge. |
| D10 | `QualityMesh2.cs` 165-168, 1448, 1800; `SnapRounding2.cs` 610-700; `ConstrainedMesh2.cs`, `ConstrainedDelaunayMesh2.cs` (hull sources, collapsed outer rings, unused made vertices) | the pinch rule, `RoundableOnGrid`, `MarkSwept`, `SnapGrid` filters, hull chains | C | – | Snap grid and hull-on-grid code (MESHING2D_SNAP.md, MESHING2D_HULL_GRID.md); no counterpart in either reference. |
| D11 | oracle, 8 changed files | token comparison | C | – | 1.5 to 12.5 % against `triangle.c`, at or below the floor; the long runs are a list of check calls and an enum. The one shared comment phrase is the first pass's (O1). |

Sources not listed in this file's earlier sections: [U04] A. Üngör, *Off-centers: a new type of Steiner points for computing
size-optimal quality-guaranteed Delaunay triangulations*, LATIN 2004; [EU07] H. Erten, A. Üngör, *Triangulations with Locally
Optimal Steiner Points*, SGP 2007; [Dev99] O. Devillers, *On deletion in Delaunay triangulations*, 1999 (IJCGA 2002); [Mei75]
G. H. Meisters, *Polygons have ears*, 1975. All are in [SOURCES.md](../SOURCES.md).

**No (A) finding.** Everything added or changed since 6b-2 is (B) or (C), and the classes of the first two passes still hold for
the unchanged routines. No rewrite is needed. One optional comment rewording (D3) is left to the clean side.

## Stage 6b-4: delta after d9006aa

A dirty-side session (2026-11-12, owner exception of 2026-11-12, as in stage 6b) repeated the method of 6b-3 on what changed
in the 2D mesher and its oracle after `d9006aa`. As above, this section names only our files, routines and lines; the evidence on
the reference side is kept outside this repository.

- **Our side**: master `01a7e71c48d13e699b7e66af9bafdbd79c55ff1c` (after #666, #661, #663). Changed since `d9006aa`
  (`git diff d9006aa 01a7e71`):
  - `comp_geom/QualityMesh2.cs`: +314 / −56 lines. The exact corner choice (#437), the small-angle pair cache and the apex
    exemption with the 60° pairs (#394, #496; the near meeting of #664 / #666), rule C (#441, #462), `ExactlyWithin` and the
    subnormal split guard, the `Note` counters, the unsplittable-piece change of #661, the sparse tag check, pair keys (#553).
  - `comp_geom/ConstrainedDelaunayMesh2.cs`: +97 / −4. The zero-area input (`HullHasNoArea`, `Collinear`, `EmptyResult`), the
    progress guard of the tolerance splits (`Shortens`, `ScaledLen2`), `EndWithinTolerance` (#664), pair-key comparers.
  - `comp_geom/ConstrainedMesh2.cs`: +26, `ToDMesh3` (#663).
  - `comp_geom/PairKeys.cs`: new, 18 lines (#553).
  - `comp_geom/SnapRounding2.cs`: +4, doc comment only (#526).
  - The oracle: 10 files of `benchmarks/Meshing2Benchmark/Oracle` (`Oracle.cs`, `SelfCheck.cs`, `BoxPairs.cs`, `Domain.cs`,
    `Oracle.SourceCoverage.cs`, `StrtreeEdgeSearch.cs`, `StrtreePointSearch.cs`, `ConstrainedGoldenCases.cs`, `Model.cs`, the
    project file); token comparison and a read of its new error bounds, as before.
  - Not in scope: `DelaunayKernel2*.cs`, `DelaunayMesh2.cs`, `TriangulationMesh2.cs`, `Triangulation2Result.cs` and
    `ConstrainedMesh2Types.cs` are unchanged since `d9006aa`; `ConvexHull2.cs` (a comment, two messages and an input check of
    the upstream hull, not part of the mesher) was looked at and is not mesher code.

  The tool ran on the changed files in full and, separately, on their added lines only (14 files, 165 000 tokens in full).
- **Reference side read**:
  - Triangle 1.6, `triangle.zip` from `netlib.org/voronoi`, sha256 `1766327a…f8480` (the archive of 6b-2 and 6b-3, fetched again;
    the same hash);
  - Triangle.NET upstream `wo80/Triangle.NET`, commit `fa6f2fdbbbfa9312e92929bd8fbb9c21da0f7769` (2026-06-10, still the upstream
    head; the version of the earlier passes), all 82 C# files of `src/Triangle`.

  Both hosts were reachable; nothing was refused or circumvented. Nothing of either was built or run.
- **Tool**: the token comparison of 6b-2 / 6b-3 with the same settings, written again from that description (own Python 3 code,
  reads files as data): comments, strings and preprocessor lines stripped; identifiers, numbers and type keywords each mapped to one
  token; `->` mapped to `.`; share of our tokens in shared 12-grams (8-grams as a second setting), longest covered run, shared comment
  word 6-grams, strings, numbers and identifiers of six or more letters. New in this pass: the share and the run also restricted to
  the added lines. Calibration before use: the noise floor from the same unrelated upstream files (`Arrangement2d.cs`,
  `ConvexHull2.cs`, `DMesh3.cs`, `Polygon2d.cs`, at `d9006aa`) and the positive control Triangle.NET (7 core files) against
  `triangle.c`.
- **By hand**: every routine added or changed since `d9006aa`, against both references, for data layout, decomposition, control
  flow, constants, names, comments and messages.

### Tool result

| | against `triangle.c`: share in shared 12-grams (8-grams) | longest run | comment 6-grams | strings |
|---|---:|---:|---:|---:|
| `QualityMesh2.cs` (21 185 tokens) | 5.6 % (33.3 %) | 49 (line 783, the exact dot-product sign of 6b-2) | 3 (see P9) | 0 |
| its added lines (2 978 tokens) | 8.7 % (41.8 %) | 23 | – | 0 |
| `ConstrainedDelaunayMesh2.cs` (13 650 tokens) | 7.2 % (34.7 %) | 53 (line 428, the barycentric height of 6b-2) | 0 | 0 |
| its added lines (1 075 tokens) | 18.3 % (51.1 %) | 24 (lines 574-577, see below) | – | 0 |
| `ConstrainedMesh2.cs` (1 907) / added (152) | 4.2 % (29.5 %) / 0.0 % (29.6 %) | 27 / 0 | 0 | 0 |
| `PairKeys.cs` (49) | 0.0 % (16.3 %) | 0 | 0 | 0 |
| `SnapRounding2.cs` (9 757, code unchanged) | 10.3 % (34.8 %) | 31 | 0 | 0 |
| oracle, 9 changed source files (118 000 tokens) | 3.2 to 12.2 % (18 to 37 %); added lines 1.4 to 12.2 % | 129 (`Oracle.cs` 225-239, the list of check calls of 6b-3); added lines up to 43 | 0 | 1 (`"input"`) |
| *noise floor: unrelated upstream files* | *5.8 to 13.5 % (31 to 48 %)* | *up to 38* | *1* | *0* |
| *positive control: Triangle.NET, 7 core files* | *16.3 to 53.7 % (50 to 88 %)* | *27 to 928* | *103 to 1226 per file* | *–* |

| | against Triangle.NET (82 files): share in shared 12-grams, all files / best single file | longest run | comment 6-grams |
|---|---:|---:|---:|
| `QualityMesh2.cs` / added lines | 29.0 % / 5.8 %; 30.8 % / 8.4 % | 73 / 60 | 3 (P9) |
| `ConstrainedDelaunayMesh2.cs` / added lines | 32.1 % / 8.4 %; 40.9 % / 18.3 % | 90 / 32 | 0 |
| `ConstrainedMesh2.cs` / added lines | 39.6 % / 12.5 %; 53.9 % / 27.6 % (152 tokens) | 82 / 22 | 0 |
| `PairKeys.cs`, `SnapRounding2.cs` | 0 %, 26.3 % / 5.8 % | 0, 36 | 0 |
| oracle, 9 files | 16.1 to 40.0 % / 6.6 to 19.0 % | up to 165 (see below) | 0 |
| *noise floor: the same unrelated upstream files* | *37.7 to 48.8 % / 8.0 to 16.2 %* | *39 to 121* | *1* |

The rewritten tool reproduces the noise floor of 6b-2 exactly (5.8 to 13.5 %) and its positive control within two points (17.9 to
53.7 % there, 16.3 to 53.7 % here with one different core file, runs up to 928 in both); it separates the known port by a wide margin.

The mesher files stay at or below the noise floor. Two shares of added lines are above the floor of whole files, both on small samples:
the 1 075 added tokens of `ConstrainedDelaunayMesh2.cs` (18.3 %) and the 152 of `ToDMesh3` (27.6 % against one Triangle.NET file, 0 %
against `triangle.c`). Their covered runs were inspected: four `if (dist(end, other) <= t2) { end = ...; return true; }` lines of one
shape in `EndWithinTolerance`, `dx * dx + dy * dy` in `ScaledLen2`, and a `for` loop appending vertices and then triangles; all generic.
The other runs at or above 30 tokens: in `QualityMesh2.cs` the repeated scaled assignments in `PairOf` (lines 1259-1262, moved
unchanged from 6b-3's `SpansSmallAngle`), a bounding-box loop (line 1056) and the dot-product sign; in `ConstrainedDelaunayMesh2.cs` a
min/max loop (line 1137); in the oracle the result classes of `StrtreeEdgeSearch.cs` (lines 252-271, 165 tokens) and
`StrtreePointSearch.cs` (lines 171-184, 133 tokens), auto-properties and a constructor that assigns its parameters, the kind of run of
O1 (85 tokens) and of the calibration (121). The shared string is the one word `"input"`. Shared identifiers are the field's vocabulary.

### Findings

| # | Our file : lines (`01a7e71`) | Routine | Class | Category | Note |
|---|---|---|---|---|---|
| P1 | `QualityMesh2.cs` 871-925, 967-981 | `MinSin2`, `SmallestAngleCorner`, `ShortestEdgeCorner`, `BelowAngle`, `BadByAngle` | B | control flow | The smallest angle is opposite the shortest edge (elementary geometry). Ours keeps sin² from area² / (l1 l2) (Q3) and picks the corner separately and exactly: a filter on the two shortest squared lengths with a 2^-40 margin and range limits, then integer squared lengths from the exact decomposition, ties to the first. Computed and decomposed differently from the reference. |
| P2 | `QualityMesh2.cs` 1199-1325 | `SmallPairSin2`, `SmallAngleSlack`, `SmallPair`, `MeetKind`, `PairOf`, `NearSmallAngle`, `SpansSmallAngle` | B | control flow, constant | Not refining a skinny triangle whose shortest edge spans a small input angle is [S02] §4 (Q4). Ours: a per-pair cache, the input angle tested against 60° (owner decision 2026-11-09, [S02] §6.2) with a stated slack, an exact classification of how the closed segments meet (shared end, T-junction or crossing), a meeting within the tolerance (#664), distances to the apex within a factor of 1.5 on the same side. The reference's test, its cases and its constants differ; the decomposition is ours. |
| P3 | `QualityMesh2.cs` 667-773 | `Encroached` (exemption branch), `ApexExempt`, `Note`, `SmallAngleExemptionForTests`, `ApexLogForTests` | C | – | The exemption of a vertex made on one partner source from splitting a subsegment at a small input angle (MESHING2D_SMALL_ANGLES.md, owner decision 2026-11-06; the figure-eight cycle of [S02] §6.2, quoted in the comment). Neither reference has an encroachment exemption of this kind; their encroachment tests are those of Q2 / D2. |
| P4 | `QualityMesh2.cs` 690-706 | `LocallyDelaunayAt`, `IsLocallyDelaunay`, rule C's revocation | B | control flow | "d not strictly inside the circumcircle of abc" is the definition of a locally Delaunay edge ([CDS] ch. 2); ours is one exact in-circle test on the subsegment, ghosts count as Delaunay. Rule C (MESHING2D_SMALL_ANGLES_CONFORMING.md) has no counterpart. |
| P5 | `QualityMesh2.cs` 1446-1456, 1695-1800 | `CircumcentrePixel`, `SplitTriangle` (counters, the unsplittable piece of #661) | C | control flow | Our grid and give-up bookkeeping; the change of #661 (t is queued again instead of exempted) has no counterpart. Q5 holds for the rest. |
| P6 | `QualityMesh2.cs` 1805-1945 | `MinRoundedSplitExponent`, `ExactlyWithin`, `SplitSegment` (subnormal guard, counters) | B | control flow | Q7 holds. "A split point equal to an end makes no progress" is the problem; ours marks the piece unsplittable, the reference handles it differently. The subnormal guard (an exact on-segment and between-ends test below 2^-1026) has no counterpart. |
| P7 | `QualityMesh2.cs` 372-430, 549-610 | `Run` (`CheckTagsNow` at the end), `CheckTags`, `CheckTagsNow`, `TagFromPiece` | C | – | Test-only tag checks (sparse above 2000 triangles, the ghost-tag invariant) and our tag propagation (Q8); no counterpart. |
| P8 | `QualityMesh2.cs` 257, 1228; `ConstrainedDelaunayMesh2.cs` 265, 802; `PairKeys.cs` | pair keys through `PairKeys.Comparer` | C | – | A hash comparer for 64-bit pair keys (#553); the references key nothing this way. |
| P9 | `QualityMesh2.cs` 23, 116, 895 | wording | B | comment | Three comment 6-grams are shared with both references. Two are 6b-3's D3 ("a vertex in its diametral lens", "of the convex hull of the"; the optional rewording of line 23 was not applied). The new one, "the shortest edge of the triangle" (line 895, "the corner opposite the shortest edge of the triangle abc"), is plain English for the quantity. No rewording needed. |
| P10 | `ConstrainedDelaunayMesh2.cs` 53-130 | `Compute` (zero-area input), `HullHasNoArea`, `Collinear`, `EmptyResult` | C | control flow | An input without area is decided exactly on the input, before snapping (MESHING2D_HULL_GRID.md). No counterpart in this form in the reference. |
| P11 | `ConstrainedDelaunayMesh2.cs` 260-300, 520-590, 593-660 | `MergeNearPoints` (comparer), `Shortens`, `ScaledLen2`, `EndWithinTolerance`, `InsertPiece` (the guarded tolerance splits) | C | – | The tolerance splits of O11 / D3 (C4, C8) with a strict length-decrease guard for termination; no tolerance in the reference. C4's class holds for the rest of `InsertPiece`. |
| P12 | `ConstrainedMesh2.cs` 63-89 | `ToDMesh3` | C | – | Conversion to the library's `DMesh3` (#663); no counterpart. |
| P13 | `SnapRounding2.cs` 14-16 | doc comment on drift | C | – | Owner decision 2026-11-10; S1 holds. |
| P14 | oracle, 10 changed files | token comparison, new error bounds | C | – | At or below the floor against both references; the long runs are a list of check calls, an enum and result classes. The new bounds and tubes (subnormal step 2^-1074, 2^-40 / 2^-41 tolerance scales, the 8-ulp zone factor, the 2.5-pixel drift guard) are the oracle's own, in exact rationals, from MESHING2D_FAST_ORACLE.md and the decisions; neither reference has a counterpart. The STR-tree searches and the box-pair sweep have no counterpart in the references. |

**No (A) finding.** Everything changed since `d9006aa` is (B) or (C), and the classes of 6b, 6b-2 and 6b-3 still hold for the routines
these changes touch. No rewrite is needed.
