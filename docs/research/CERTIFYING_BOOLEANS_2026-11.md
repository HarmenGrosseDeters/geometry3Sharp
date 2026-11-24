# Certifying mesh operations, stage 1: `ExactMeshBoolean` (2026-11-24)

Research only, owner approval 2026-11-24. The goal of the topic: a user trusts a small, independent checker instead of the large
kernel. A certifying algorithm (McConnell, Mehlhorn, Näher, Schweitzer 2011) returns a certificate with its result, and a much
simpler checker verifies the result exactly from that certificate. This note asks five things. Is a certifying 3D mesh Boolean new?
What does the library already have? What would the certificate and the checker be? What do they cost? Should we go on? **No
library code was changed.** The prototype is the mode `certify size` of `benchmarks/BooleanComparison` (`CertifySize.cs`), and its
raw output is `benchmarks/BooleanComparison/perf/certify_size.txt`.

The topic is not new to this repository. Three earlier notes did most of the groundwork, and this note builds on them rather than
repeating them:
- [BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) section 3: the prior art up to 2026-11-17 and
  the certificate parts C1-C5.
- [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md): the cost of stage 3 (C2, the embedding), a filtered C2
  prototype, a certified user mode (option C, parked), and the owner's decision on removed parts.
- The test-only checker `tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs` (C0-C5, with C2 since c906101). The fuzz
  campaign ([EXACT_BOOLEAN_FUZZ_2026-11.md](EXACT_BOOLEAN_FUZZ_2026-11.md)) uses it as its oracle.

## 1. Result in short

| Question | Answer |
|---|---|
| Novelty | No published certifying 3D mesh Boolean, certified mesh arrangement or certified mesh repair found (searches in section 2). The only hit for the exact topic is this repository's own PR #1061. The building blocks are known: local-to-global checkers (LEDA), winding-number jumps, coverage by convexity, verified checkers (Isabelle, Coq, Lean). Combining them for an exact 3D Boolean with rounding is new engineering. A formally verified checker for it would be a research result. |
| What exists | A complete test-only certifying checker (C0-C5, 1598 lines). It is fed by an internal recording (`RecordCertificate`, `ComputeExactVertices`), decides every sign with BigInteger, and certifies 100 of the 105 runs measured here. The other 5 are inconclusive by its documented limits, and none fails. |
| Certificate size (measured) | Format R, the rational corners the checker reads today: 0.6-4.0 times the bytes of the input meshes, typically 0.7-1.2. Format S, each corner as its construction from input ids: 0.5-3.4 times, at most 25 % smaller than R. The winding labels are 0.07-0.4 times the input. The coordinates are short (median 6 bits, at most 351 bits per homogeneous coordinate), so size is not the problem. |
| Checker time (measured, as built) | 1.1-118 times the boolean on 84 fuzz runs (median 9.6), 2-2350 times on 21 procedural cases. Four inputs exceed 20 s: C2 takes 27-44 s on three coplanar or touching inputs, and C5 takes 17 s and 52 s on two torus cases. The earlier filtered C2 prototype ran at 0.4-8.5 times the boolean, and the earlier estimate for a filtered whole checker was 3-16 times. |
| "Cheaper than recomputing" | Not on time. The boolean is near-linear and parallel, and the checker must test every piece with predicates of the same degree, so its floor is about 1-3 times the boolean. The gain is trust, not speed. The checker relies on a different and smaller argument than the kernel, and what it would replace is a second, independent Boolean. |
| Doubles with filters only, no implicit points? | **Partly.** The claims about the rounded result (closed, manifold, no self-intersection) need only doubles: `NotCleanReason` is exact on doubles. The membership claims (the right faces, the right labels, nothing missing) live on the exact result, whose corners are not doubles. Implicit points are unavoidable there. They can be rationals (format R, BigInteger plus a filter) or constructions from input ids evaluated by indirect predicates on the input doubles (format S, no BigInteger points). Format S shares a predicate family with the arrangement and so weakens independence. |
| Local vs global | Local and search-free: C1 (pieces in and tiling their sources), the jump and wedge rules of C3, C4 (selection), and the closedness of both results. Global: C2 (no two pieces cross: an all-pairs claim with no witness of linear size), one anchor ray per connected set of pieces, C0 (inputs and culling), and the coverage search of C5. **Completeness ("nothing missing") is not a property of the output.** It follows from C1, C2, C3 and C4 together with the anchors (section 3.2). |
| Recommendation | **Go for two narrow steps, no-go for a library mode now.** Step A (test code): remove the two outliers above 20 s, which the time rule requires anyway. Step B: the certificate as data plus a standalone checker that reads only the inputs and a file, as the first artifact a user could run. A library mode (option C of the previous note) and a formally verified checker stay parked behind named triggers (section 6). |

## 2. Prior-art check

The earlier table (prior-art note 3.2) stands: McConnell et al. 2011; Mehlhorn et al. 1996/1999 (checkers for convex polytopes and
Delaunay triangulations in LEDA, the local-to-global argument); Devillers, Liotta, Preparata, Tamassia 1998 (the arithmetic degree of a checker); Palios (verification of
partitions); Alkassar, Böhme, Mehlhorn, Rizkallah 2014 (verified checkers); Fouilhé, Monniaux, Périn 2013 (an untrusted polyhedra
library with a Coq-verified checker); Zhou et al. 2016 and Jacobson et al. 2013 (winding jumps); DRAT-trim and cake_lpr (verified
proof checking in SAT solving); Chazelle 1984 (the Ω(n²) bound on convex-cell witnesses). New in this check, 2026-11-24:

| Work | What it adds |
|---|---|
| L. Noschinski, C. Rizkallah, K. Mehlhorn, *Verification of certifying computations through AutoCorres and Simpl*, NFM 2014; AFP entry *Planarity_Certificates* (Noschinski; version of 2025-03-17) [S] | The LEDA planarity checker is verified in Isabelle: planar embeddings (combinatorial maps) and Kuratowski subgraphs. It is the closest model for a verified checker of a combinatorial-geometric output. The planar map in our C1 tiling check is the same kind of object. |
| K. Mehlhorn et al., *Trustworthy graph algorithms*, arXiv 1907.04065 (MFCS 2019) [S] | A survey of the certifying-plus-verified-checker programme for LEDA. It names the parts that had to be formalized first, a graph library and then the checkers. |
| X. Allamigeon, Q. Canu, P.-Y. Strub, *A formal disproof of the Hirsch conjecture*, CPP 2023 [S; content M] | In Coq, it checks certificates that an untrusted informal tool produced for a polytope's vertex-edge graph. It is the same untrusted-producer, verified-checker architecture applied to polyhedra. |
| *Formally certifying the vertex set of a polyhedron faster than informal enumeration*, arXiv 2610.11913 (2026) [S; abstract and introduction only] | It reports that no formally proven method existed for certifying the vertex sets of polyhedra. Its checker is fast and has two layers: a Boolean layer proven equivalent to the mathematical conditions, and a refinement layer. It is evidence that a verified geometric checker can also be fast. |
| D. Pichardie, Y. Bertot, *Formalizing convex hull algorithms*, TPHOLs 2001; J.-F. Dufourd, Y. Bertot, *Formal study of plane Delaunay triangulation*, ITP 2010; AFP *Andrew_Monotone_Chain* [S] | Verified algorithms, not checkers. Formal 2D geometry is mature for hulls and Delaunay triangulations. Nothing found for 3D arrangements. |
| Martens, Trettner, Bessmeltsev, *The antipodal method*, TOG 2026 (arXiv 2605.01536); EMBER (Trettner et al., TOG 2022); Cherchi et al. 2022 (arXiv 2205.14151); *trueform* (arXiv 2607.15905, 2026) [S; titles and abstracts only] | Recent exact or robust Booleans and winding numbers. None ships a certificate or a checker. Cherchi et al. state that cascaded Booleans carry no guarantee after snapping to doubles. |
| Industrial "translation validation" (Verisurf, Kubotek; ACIS `validate`) [S] | Comparison against a reference model within a tolerance, and kernel self-checks. Neither is a certificate against the inputs. |

Terms searched without a result for the topic itself: "certifying algorithm mesh boolean certificate checker verified polyhedral
arrangement" (extended search), "mesh boolean output guaranteed correct independent checker winding number verification 2026"
(extended), "translation validation computational geometry kernel / proof-carrying mesh geometry", and "formally verified
geometric kernel checker Isabelle Coq Delaunay convex hull certificate". Not reached: the full texts of arXiv 2610.11913 and the
CPP 2023 paper (only search snippets), and no paywalled venue was opened. Marks: [S] means confirmed by a search result in this
session; [M] means from memory and not re-checked. The repair side (certified mesh repair, ε-envelope certificates) also returned
nothing beyond the envelope papers already in [ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md](ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md).

**Verdict.** No published certifying 3D mesh Boolean, arrangement or repair was found. The component ideas are known. Their
combination for an exact 3D Boolean with rounding to doubles is new engineering, and this repository has had it as test code since
#1061. A formally verified checker for the local parts would be a research contribution: the 3D counterpart of the verified LEDA
checkers.

## 3. Certificate design

Notation: the inputs A and B (closed, doubles); the exact result E (pieces of the input triangles with rational corners); the
rounded result R (doubles). Parts C0-C5 are as in the prior-art note 3.3 and in the checker's class comment.

### 3.1 What is certified, and by which part

| Claim | Part | Local? | Predicates |
|---|---|---|---|
| The arrangement's meshes are the caller's, and every culled triangle lies outside the culling mesh | C0 | global: triangle against mesh, one ray per culled set | exact triangle intersection on input doubles, ray crossings |
| Every piece lies in its source triangle, and the pieces of a source tile it | C1 | **local** (per source: edge pairing plus an exact area sum) | orient2d / orient3d on corners |
| No two pieces of different sources meet except at shared corners or along shared edges | C2 | **global** (an all-pairs claim) | orient3d on corners |
| Each piece's side labels (w_A, w_B) are right | C3 | **local** jumps (±1 across a piece of its own mesh, 0 otherwise) and wedges (radial order around every edge), plus **global** anchors (one ray per connected set) | orient3d on corners; the ray against all input triangles |
| A piece is in E iff the operation's predicate differs across it, oriented to the "in" side | C4 | **local** | none (integers) |
| E is the kept pieces; E and R are closed and oriented; R and E lie within δ of each other | C5 | closedness **local**; coverage local given a witness, global (a search) without one | exact squared distances |
| R has no self-intersection | on doubles | global (a pair search) | orient3d on doubles (`NotCleanReason`) |

### 3.2 Completeness ("nothing of the true result is missing")

No check on the output alone gives completeness. If a whole shell of B is missing from a union, E stays closed, manifold and
embedded. Completeness is a statement about the input surfaces:
- **C1** says the pieces cover the input surfaces, so nothing of A ∪ B is lost before classification.
- **C2** says no piece is crossed by another, so the winding numbers are constant on each side of each piece.
- **C3** says the labels jump by the orientation across each piece and agree around each edge. One anchor per connected set then
  fixes them all. Let f be the label minus the true winding number. f has no jump anywhere, so it is constant on every connected
  set, and the anchor makes that constant zero.
- **C4** makes wind(E) − [pred(w_A, w_B)] jump-free across every piece, kept or not. That function is zero far away, so it is zero
  everywhere off the surfaces. So E bounds exactly the solid pred(A, B).

The non-local steps are C2 and the anchors. The anchors cost one ray per connected set of pieces (1-2 in every run here) and are
cheap. C2 is the hard part, as everywhere in this field. It has no witness of near-linear size (Chazelle's bound rules out
convex-cell witnesses), so the checker re-derives it with its own box tree. That is the "recheck" case of McConnell et al.,
acceptable because a pair search is simpler than an arrangement.

### 3.3 The certificate format

The recording and the checker already exchange these data in memory. As a file, the certificate would hold:

| Block | Content | Size measured (`certify size`) |
|---|---|---|
| Header | input hashes (C0 compares them with the inputs given to the checker), operation or predicate, grid scale and centre, rounding bound and settings | constant |
| Pieces | per piece: three corner ids and its source triangle | 16 B per piece |
| Corners, **R** | per corner that is not an input vertex: homogeneous X, Y, Z, W | median 6 bits per coordinate on the fuzz runs, at most 351 bits (3 spheres) |
| Corners, **S** | per such corner: its construction (an input edge crossing an input plane, two coplanar input edges with an axis, or three input planes), as `ExactMeshArrangement`'s point key | 13-18 B |
| Labels | per classified piece: one front winding number per mesh; per piece: keep and flip bits | 0.07-0.4 times the input |
| Hints (optional) | per piece its coplanar stack; per triangle of R the triangles of E that cover it | not measured; they replace the two search parts of C3 and C5 by checks of a list |
| Removed parts | per component the rounding removed: its triangles, area and box (owner decision 2026-11-22: listed, never silently exempt) | small |

Format S is at most 25 % smaller than R on these inputs, because the rationals are short. Its value lies elsewhere, in the checker
(section 3.4). Both formats are within a few times the input mesh: a file of the same order as the result itself.

### 3.4 The checker: doubles and filters, implicit points

- **On R alone (doubles):** closed, oriented, manifold, no self-intersection. This is exact on doubles with adaptive predicates
  (`NotCleanReason`: 0.1-0.6 times the boolean, previous note). It says nothing about the point set.
- **On E (membership):** E's corners are intersections of input edges and planes. Every predicate of C1-C3 is a sign on such
  points. Two implementations are possible:
  1. **Rationals plus a filter (format R).** Approximate the corners in floating point with an error bound, and fall back to
     BigInteger where the bound does not decide. The C2 prototype of the previous note did this: a factor of 4-13 over pure
     BigInteger. Exact zeros are frequent (a corner on another piece's edge line), and the filter never decides them. They need a
     cheap exact stage or an incidence hint ("this corner lies on input edge e"), which C1-style checks can verify.
  2. **Constructions (format S) evaluated by indirect predicates on the input doubles** (Attene 2020), with an expansion fallback.
     This removes BigInteger points and their allocations. The price: the checker then implements the same predicate family as the
     arrangement. Independence would come only from generating those predicates from a separate specification
     (`tools/PredicateCompiler`), with its own error bounds to review.
- **Without implicit points at all:** not possible for membership. A checker that reads only A, B and R would have to rebuild E's
  intersection curves to know where R must be, and that is recomputation. Coverage of R by the input surfaces (an ε-envelope) is
  checkable on doubles, but it certifies "R lies near A ∪ B", not "R bounds A op B".

**Size of the trusted base.** Today the checker is 1598 lines plus `ExactPoint3` (267) and the exact predicates of C0. By part (by
line ranges): data and recording about 380, C0 about 175, C1 about 90, C2 about 355, C3 and C4 about 215, C5 about 385. The local,
search-free core (C1, the C3 rules, C4, closedness) is about 400 lines and needs only orientation signs and integer bookkeeping.
That core is the realistic candidate for a formally verified checker. C2, the anchors and the C5 search are the global parts, about
800 lines, whose correctness rests on arguing that a search is conservative.

## 4. Measurements

`dotnet run -c Release --project benchmarks/BooleanComparison -- certify size 3 4 > perf/certify_size.txt`. Machine: a cloud
container with 4 logical cores, .NET 10.0.15, Release. Boolean times are medians of 3 warm runs with the default threads. The
checker parts run serially, once, after a warm-up. The container is shared, so times vary by some 10-30 %: read the ratios as
orders of magnitude. Inputs: the 7 closed families of the fuzzer (`BooleanFuzzCases`: coplanar, shared, ulp, slab, shells, generic,
rotated), seeds 0-3, three operations each (84 runs), plus the 17 procedural cases of `certify cost` and 4 of its size series.

### 4.1 Verdicts

All 105 runs end as follows: 100 certified. 4 are inconclusive because C5 left sliver triangles uncovered within its split depth
(ulp #3 U and D, rotated #0 U, rotated #3 U, with 4-9 triangles each). The fuzzer allows this in its sliver families. 1 reports
removed parts (slab #0 D, by the owner's rule). No run fails.

### 4.2 Fuzz runs (84; small inputs, 8-120 triangles)

| Quantity | Median | Range |
|---|---|---|
| Certificate R / input bytes | 1.50 | up to 6.4 |
| Certificate S / input bytes | 1.46 | up to 3.5 |
| Bits per homogeneous coordinate (largest) | 6 | up to 225 |
| Boolean (ms) | 1.49 | |
| Checker C0-C5 (ms) | 15.8 | |
| Checker / boolean | 9.6 | 1.1-118 |
| Share of C2 in the checker | 42 % | |

On inputs this small, fixed costs dominate both sides: the box trees, the hash maps and the anchor ray.

### 4.3 Procedural cases (ms; in brackets: times the boolean)

| Case | Pieces | R / input | S / input | Boolean | C1 | C2 | C3+C4 | C5 | Checker sum |
|---|---|---|---|---|---|---|---|---|---|
| box+box overlap U | 84 | 2.68 | 2.27 | 0.83 | 1.44 | 11.0 | 0.74 | 1.90 | 15.2 (18) |
| sphere+sphere overlap U | 14 228 | 0.87 | 0.76 | 37.9 | 518 | 774 | 181 | 1444 | 3092 (82) |
| sphere-sphere D | 10 098 | 0.64 | 0.54 | 29.9 | 413 | 583 | 180 | 1125 | 2901 (97) |
| box-cylinder D | 892 | 3.95 | 3.36 | 4.23 | 14.1 | 212 | 7.52 | 18.5 | 254 (60) |
| torus+torus linked U | 7 984 | 1.20 | 0.91 | 35.7 | 351 | 803 | 106 | **17 128** | 18 447 (516) |
| fine box edge-touch U | 5 400 | 0.67 | 0.67 | 19.2 | 160 | **43 700** | 45.4 | 1114 | 45 045 (2351) |
| fine box vertex-touch U | 5 400 | 0.67 | 0.67 | 19.8 | 160 | **35 013** | 63.0 | 1051 | 36 314 (1830) |
| cylinder on box, partial face-touch U | 5 462 | 1.73 | 1.37 | 40.9 | 175 | **27 337** | 44.6 | 416 | 27 989 (684) |
| 3 spheres U | 16 736 | 1.15 | 0.90 | 38.4 | 620 | 1502 | 227 | 1396 | 3940 (103) |
| sphere+sphere U n=64 | 99 428 | 0.74 | 0.70 | 187 | 3194 | 3783 | 1110 | 11 333 | 20 984 (112) |
| torus-torus D nu=128 | 24 079 | 0.64 | 0.51 | 114 | 888 | 2297 | 526 | **52 449** | 60 590 (530) |
| fine box face-on-face U n=16 | 6 368 | 0.76 | 0.75 | 21.8 | 198 | **38 096** | 58.6 | 407 | 38 789 (1780) |

All 21 rows are in `perf/certify_size.txt`. Reading them:
- **The local core is linear but not cheap with BigInteger.** C1 + C3 + C4 cost 0.4-30 times the boolean, typically 12-23 on
  curved inputs. The previous note's filter factor of 4-13 would bring that to about 1-5 times. Filtering is a prerequisite for any
  user-facing use.
- **Two global parts are quadratic on some inputs, and both exceed 20 s.**
  1. In C2, the in-plane "straddle" loop: for every classified piece, every coplanar input triangle of its plane gets a BigInteger
     test, with no box filter (`check_embedding`, the last loop). On meshed box faces that is pieces times triangles per plane.
     This is a diagnosis from reading the code. The loop was not timed separately. The pair search of C2 itself is near-linear:
     it stays under 3.8 s even at 99 428 pieces.
  2. In C5, on tori: 17 s (7 984 pieces) and 52 s (24 079 pieces). The previous note measured the first and did not diagnose it.
     Growth from 7 984 to 24 079 pieces is faster than linear. Not diagnosed here either.

  Both are test-harness code, not library code. By CLAUDE.md's time rule they are potential defects and need an issue with a
  diagnosis. That is reported to the coordinator, and no issue was opened by this research session.
- **Size scales with the pieces, not with the coordinates.** The R/S gap is largest where many corners are constructed (box-cylinder,
  partial face-touch) and vanishes where the result has only input vertices (the touch cases).

## 5. Open questions

- **Repair (`MeshRobustRepair`).** "Clean" is checkable on doubles (`NotCleanReason`). The faithfulness claim is harder: the input is
  broken, so there is no exact winding number to anchor membership. For open inputs the generalized winding number is not an
  integer, so C3's integer jumps do not apply (open operands are outside the Boolean checker's class too). What could be certified
  is a two-sided ε-envelope: every output triangle within ε of the input, and every kept input part within ε of the output. Each
  direction is checkable with the coverage idea of C5 (corners within ε of one convex triangle; split otherwise) and a witness list
  per triangle. Whether that is the contract users want from repair is a semantic question, not a checking one.
- **The rounding.** C5 certifies Hausdorff distance at most δ, plus R's embedding on doubles. It does not certify that R and E have
  the same topology (isotopy). The prior-art note 2.5 names that as open research.
- **Independence versus speed.** Every shared building block makes the checker faster and its verdict less independent. Format S
  with generated indirect predicates is the cheapest exact route. Whether that is still "independent enough" depends on who audits
  the predicate generator, and is an owner question if a user mode is ever built.
- **C2 without search.** A certificate of the embedding that is near-linear in the worst case is open. Chazelle's bound rules out
  convex-cell witnesses.

## 6. Recommendation, staged plan and stop rules

**Go, narrowly.** The certifying checker exists, has found no defect in the Boolean, and is the fuzzer's oracle. What is missing
for the topic's goal (a user runs a small checker) is not a new algorithm but (1) a checker without quadratic outliers and (2) a
certificate that leaves the process.

| Step | Content | Tier | Stop rule |
|---|---|---|---|
| **A** | Test code only: a box filter for C2's straddle loop; a diagnosis and fix of C5 on tori; the procedural cases of `certify size` as the acceptance set | M | Done when every case of `certify size` has its checker under 20 s and under 50 times the boolean. If a part stays quadratic after the box filter: stop, and record the diagnosis in an issue. |
| **B** | Certificate as data: an internal serializer of the recorded witness (format R plus the removed parts) and a standalone checker (a tool, not the library) that reads only the input meshes and the file. Its dependency list must show no reference to `ExactMeshArrangement` or the classification. | M | Stop if the standalone checker needs any arrangement code, or if the file exceeds 5 times the input on `certify size` (measured: at most 4.0). |
| C | A library opt-in mode with filters (previous note, section 5: 3-16 times the boolean, estimated; exact-code review, a mutation shard, at least one new error bound) | L | Parked. Trigger: a caller who must show a certificate. Then first filter C1, C3 and C5 and measure as in section 4. |
| D | A formally verified local core (C1, the C3 rules, C4; about 400 lines) in Isabelle or Lean, following the Rizkallah and Noschinski checkers | research | Parked. Trigger: an owner decision to publish, or a partner. Stop if the planar-map part of C1 cannot reuse an existing formal library. |
| E | Repair and ε-envelope certificates | study | Only after B, and only with a contract for repair faithfulness agreed with the owner. |

**Owner decision (2026-11-24, relayed by the coordinator):** step A now, built on this branch
(`research/certifying-booleans-step-a`); step B after A meets its stop rule. The library mode (C) and the verified checker (D)
stay parked behind their triggers; E stays behind B and a repair contract.

How others solve it: section 2. No library ships a certificate of a mesh Boolean against its inputs. Users check results with a
separate self-intersection test, and the closest working practice is verified proof checking in SAT solving and verified checkers in
LEDA and Coq polyhedra.

## 7. Limits of this study

- Procedural inputs only (the fuzzer's families and the benchmark's cases). No Thingi10K run.
- Sizes are counted, not serialized: ids are 4 bytes, BigIntegers their byte length plus 1. A real format with varints would be
  smaller.
- Checker parts were timed once after a warm-up, on a shared 4-core container. They are ratios, not regression baselines.
- The two outliers above 20 s are diagnosed only by reading (C2) or not at all (C5).
- The prior-art check reached abstracts and search snippets, not the full texts of the 2026 arXiv paper or the CPP 2023 paper.

## 8. Step A: the quadratic outliers removed (2026-11-24)

Test and benchmark code only (`BooleanCertificate.cs`, its tests, the benchmark's name filter). Raw output: `perf/certify_size_step_a.txt`
(same command and machine class as section 4, the checker serial; the earlier numbers are `perf/certify_size.txt`).

**Causes (profiled, not only read).**
1. C2 on touching coplanar inputs: two loops of pieces times all coplanar input triangles of the plane, each a BigInteger test with
   no box filter: the straddle loop (29 s on fine box edge-touch) and the stacks of C3, prepared inside the first C2 call (4 s). The pair
   search itself took 0.5 s.
2. C5 on tori: not the number of exact tests (162 000) but their price: each `Near` is a BigInteger rational distance (with gcd
   reductions), about 50 microseconds, and a triangle needed about 27 of them. The grid gave 45 candidates per triangle. Distances were
   tiny (Delta 4e-5 against triangles of 0.02), so almost all tests were decidable in doubles.

**Fixes (verdicts unchanged, soundness kept).**
- C2: a box filter in front of both loops (`CoplanarNear`): per plane with more than 8 input triangles a box tree (the existing
  `EmbeddingBoxTree`, now with `Query`), otherwise a linear box scan. Boxes come from the exact corners' approximations widened by a
  relative 2^-49 (as C2's pair boxes). A pair whose boxes are disjoint is disjoint, so the exact test (`StraddlesInPlane`,
  `InsideInPlane`) is only skipped where it must answer "no". The source triangle is always kept; candidates stay in group order.
- C5: two one-sided double filters before the exact `Near`: reject when the point is farther than Delta + 3 eps from the box of the
  triangle's approximated corners, accept when a point inside the triangle (a clamped convex combination, so even a wrong region of the
  closest-point routine only gives a larger distance) is within Delta - 3 eps. eps = 2^-40 times the largest coordinate plus extent.
  Everything between, and every point without a finite approximation, goes to the exact test.

**Soundness evidence.** Tests: `CoplanarBoxFilter_LosesNoPair_OnFineFaceContacts` (audit against the exact tests, planes of 60-70
triangles, all operations), `InjectedMissedSplit_OnFineFaces_IsCaughtThroughTheBoxTree` (a wrong result that must not certify; the
filtered checker agrees with the unfiltered exact brute force on every case), `CoverageFilters_AgreeWithTheExactDistanceTest` (8 000
random lattice cases, scales 1 and 2^20, plus distances delta +- 2^-30 .. 2^-60). Hand mutations, each turning at least one test red:
query node test flipped (3 tests), linear scan comparison flipped (16), straddle loop with no neighbours (2), stacks with no neighbours
(18), box slack removed (18), C5 reject tolerance flipped (48), C5 accept margin flipped (1: only the borderline cases catch it),
accept with a loose margin (2).

**Verdicts.** All 105 runs (84 fuzz, 21 procedural) end as before: 100 certified, 4 with uncovered slivers, 1 with removed parts, 0
fail; the verdict text of every run is identical.

**Times** (ms, after / before; in brackets times the boolean):

| Case | Boolean | C1 | C2 | C5 | Checker before (x) | Checker after (x) |
|---|---|---|---|---|---|---|
| box+box overlap U | 0.8 | 1 → 1 | 11 → 10 | 2 → 1 | 15 (18) | 14 (17) |
| box+box face-touch U | 0.8 | 0 → 0 | 0 → 0 | 0 → 0 | 1 (3) | 1 (1) |
| box+box edge-touch U | 0.6 | 0 → 0 | 0 → 0 | 0 → 0 | 1 (2) | 1 (2) |
| sphere+sphere overlap U | 45.7 | 518 → 617 | 774 → 673 | 1444 → 999 | 3092 (82) | 2547 (56) |
| sphere&sphere overlap I | 23.1 | 511 → 510 | 748 → 740 | 534 → 386 | 2119 (93) | 2050 (89) |
| sphere-sphere D | 26.8 | 413 → 494 | 583 → 571 | 1125 → 830 | 2901 (97) | 2717 (101) |
| sphere+sphere tangent-ish U | 11.1 | 198 → 237 | 236 → 197 | 665 → 417 | 1210 (87) | 938 (84) |
| box-cylinder D | 4.9 | 14 → 13 | 212 → 227 | 18 → 15 | 254 (60) | 265 (54) |
| torus+cylinder U | 16.0 | 180 → 162 | 824 → 529 | 487 → 361 | 1584 (100) | 1221 (76) |
| torus+torus linked U | 37.9 | 351 → 418 | 803 → 762 | 17128 → 982 | 18447 (517) | 2318 (61) |
| fine box edge-touch U | 18.6 | 160 → 159 | 43700 → 1357 | 1114 → 1061 | 45045 (2346) | 2638 (142) |
| fine box vertex-touch U | 14.6 | 160 → 154 | 35013 → 1440 | 1051 → 775 | 36313 (1834) | 2440 (167) |
| sphere on box face (pole touch) U | 18.2 | 396 → 403 | 14380 → 970 | 1351 → 984 | 16384 (645) | 2576 (142) |
| two cylinders line-touch U | 3.5 | 12 → 8 | 643 → 304 | 38 → 22 | 703 (241) | 341 (99) |
| cylinder on box, partial face-touch U | 36.6 | 175 → 168 | 27337 → 1732 | 416 → 395 | 27988 (684) | 2348 (64) |
| 3 spheres U | 39.2 | 620 → 614 | 1502 → 1292 | 1396 → 844 | 3939 (103) | 3095 (79) |
| box+sphere+cyl U | 21.6 | 401 → 514 | 771 → 799 | 759 → 612 | 2074 (91) | 2051 (95) |
| sphere+sphere U n=16 | 17.8 | 260 → 295 | 368 → 367 | 635 → 410 | 1377 (63) | 1158 (65) |
| sphere+sphere U n=64 | 190.0 | 3194 → 2739 | 3783 → 3469 | 11333 → 9035 | 20984 (112) | 17663 (93) |
| torus-torus D nu=128 | 92.3 | 888 → 794 | 2297 → 1815 | 52449 → 2924 | 60590 (531) | 11154 (121) |
| fine box face-on-face U n=16 | 22.5 | 198 → 166 | 38096 → 1771 | 407 → 353 | 38790 (1779) | 2359 (105) |
Fuzz runs: checker / boolean median 8.3 (before 9.6), maximum 49 (before 118).

**Stop rule.** Under 20 s: met by every case (largest: sphere+sphere n=64, 17.7 s; torus-torus nu=128 went from 60.6 s to 11.2 s).
Under 50 times the boolean: **met only by the fuzz runs and the box cases; not met** by the curved and the fine-box cases (54-167 times).
That is not a quadratic part any more: it is the linear BigInteger floor, measured per part: C1 0.1-3 s, the same-chain test of C5
(string keys, gcds per edge: 7.4 s of the 9 s of C5 on n=64), C2's pair search (3 s on n=64), C0 (6 s on torus-torus nu=128). The
earlier estimate (section 4: filtered C1, C3, C5 would bring the whole checker to 3-16 times) still stands; reaching 50 times needs a
floating-point filter for C1, the pair search and the chains, a larger change than step A (a candidate for step C, where the filters
are part of the design). Not done here.

## Sources

Papers and public documentation only. No code was read or ported, apart from this repository's own.
- R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011.
- K. Mehlhorn, S. Näher, M. Seel, R. Seidel, T. Schilz, S. Schirra, C. Uhrig, *Checking geometric programs or verification of
  geometric structures*, CGTA 12, 1999.
- O. Devillers, G. Liotta, F. P. Preparata, R. Tamassia, *Checking the convexity of polytopes and the planarity of subdivisions*,
  CGTA 11, 1998.
- E. Alkassar, S. Böhme, K. Mehlhorn, C. Rizkallah, *A framework for the verification of certifying computations*, JAR 52(3), 2014
  (arXiv 1301.7462).
- L. Noschinski, C. Rizkallah, K. Mehlhorn, *Verification of certifying computations through AutoCorres and Simpl*, NFM 2014;
  L. Noschinski, *Planarity_Certificates*, Archive of Formal Proofs (isa-afp.org).
- K. Mehlhorn et al., *Trustworthy graph algorithms*, arXiv 1907.04065.
- X. Allamigeon, Q. Canu, P.-Y. Strub, *A formal disproof of the Hirsch conjecture*, CPP 2023 (arXiv 2301.04060).
- *Formally certifying the vertex set of a polyhedron faster than informal enumeration*, arXiv 2610.11913, 2026 (introduction only).
- D. Pichardie, Y. Bertot, *Formalizing convex hull algorithms*, TPHOLs 2001; J.-F. Dufourd, Y. Bertot, *Formal study of plane
  Delaunay triangulation*, ITP 2010 (arXiv 1007.3350).
- A. Fouilhé, D. Monniaux, M. Périn, *Efficient generation of correctness certificates for the abstract domain of polyhedra*, SAS 2013.
- M. Attene, *Indirect predicates for geometric constructions*, Computer-Aided Design, 2020.
- B. Chazelle, *Convex partitions of polyhedra: a lower bound and worst-case optimal algorithm*, SIAM J. Comput. 13(3), 1984.
- Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh arrangements for solid geometry*, SIGGRAPH 2016; G. Cherchi et al.,
  *Interactive and robust mesh Booleans*, SIGGRAPH Asia 2022 (arXiv 2205.14151); P. Trettner et al., *EMBER*, TOG 41(4), 2022;
  Martens, Trettner, Bessmeltsev, *The antipodal method*, TOG 2026 (arXiv 2605.01536); *trueform*, arXiv 2607.15905 (abstracts).
