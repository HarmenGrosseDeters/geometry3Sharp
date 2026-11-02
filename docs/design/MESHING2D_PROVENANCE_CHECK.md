# 2D meshing: provenance check (stage 6b)

Stage 6b of the [roadmap](../ROADMAP.md): a separate session (the dirty side, the only one that read the reference
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
