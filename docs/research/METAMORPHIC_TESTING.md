# Metamorphic testing and fuzzing of geometry kernels: method, falsification run, recommendation

Status: research note with a falsification run (tier S: docs and one benchmark harness, no library change), 2026-11-16. A1 and P1 built 2026-11-16 (PR `claude/metamorphic-a1-p1`): `MetamorphicRelationTests`, validated on the fix parents (A1 fires on cbf8b78, P1 on c677da7, both quiet on master, `benchmarks/MetamorphicFalsification/results/relations.txt`). The first sweeps found two defects on master, #985 (SafeRounding, ExactMeshBoolean) and #986 (QualityMesh2 Empty); the mesher's cost follows the aspect ratio (min-angle size law), so its bound is scaled by 2^|e| (section 10).
Builds on the catalogue [design/METAMORPHIC_RELATIONS.md](../design/METAMORPHIC_RELATIONS.md) (2026-11-13; its R1 symmetry and R2
cost relations are in the tree as `SymmetryInvarianceTests` and `CostInvarianceTests`, R3-R5 are not). That note ranked relations
from the fault classes of the findings documents. This note tests the ranking against the repository's bug issues.

**Question.** Can metamorphic relations (relations between the outputs of related inputs, no reference solution) find real bugs
in mesh kernels systematically, and which relations find which fault classes?

## 1. Result in six lines

1. Of 83 issues (74 closed, 9 open), **51 are defects of an operation** (including slow units under the 20 s rule); the rest are
   enhancements, local tasks, CI flakes, fuzz-tool and audit items (section 4).
2. A metamorphic relation **was or would have been the finding mechanism for 15 of the 51 (29 %)**: similarity or 2^k scale 8, a
   cost or shape relation (anisotropic scale, far part) 4, tiny perturbation 1, refinement 1, parallel = serial 1. The other **36
   (71 %) need no relation or none helps**: 25 are found by the time rule, a crash or a contract / guarantee check, 11 need an
   oracle or a differential (the 2D mesher's oracle disagreements 7, a port error 1, platform differences 3).
3. Executed on the fixing commit's parent (six repros, section 3): the **2^k scale caught 2 of 6** by the rule of section 2 (#745, #644), plus #856, where it fires on master too because the fix is an opt-in refusal, and #710, which throws at every scale, a **cost relation under
   anisotropic scaling** caught the one the 2^k scale missed (#712), a **tiny perturbation** caught the one that scale and
   symmetry missed (#719), and **symmetry and permutation caught none that the fixed code does not also show** (they fire on
   the control as well for #856, #712 and #710; on #719 a first run fired and was a signed-zero artifact of the harness, found by the review).
4. The harness already uses the cheap relations (scale, permutation, similarity), so what is left is the part relations cannot
   reach: the bugs are mostly **absolute constants, range edges and cost cliffs**, found by scale and fuzz, and **oracle
   disagreements** of the 2D mesher (7 issues).
5. Cost per relation is small (section 6). **Recommendation: build two more, park the rest.** Build (a) the anisotropic cost
   relation and (b) the span / tiny-coordinate perturbation, as a few parametrised tests each; park Boolean algebra beyond pairs,
   refinement invariance and idempotence (one issue each at most, none for algebra and idempotence; keep as cheap regression nets only when an area is
   reopened); drop black-box comparison runs against other kernels as a bug-finding method (no relation fires without a
   reference, and the differential tier exists). Stop rules in section 7.
6. A failure to find new bugs is also a result: the falsification run found **no new defect**; the only open finding on master
   is the documented one (MeshMeshCut without `RefuseContact` still depends on the scale, #856).

## 2. Method

**Relation.** A transformation `T` of the input with a predicted effect on the output: `op(T x) ~ E(op(x))`. Tiers of strength:

| Tier | Prediction | Example | Check |
|---|---|---|---|
| exact | same result, mapped back, bit for bit | 2^k scale, the 48 axis symmetries, relabelling | `Invariance.Difference`, `SymmetryInvarianceTests` |
| set / geometric | same solid, not the same triangles | conforming subdivision, triangle order of ties | `BooleanOracle.SameSolid`, `GeometricDifference` |
| identity | an algebraic law between several results | vol(AuB)+vol(AnB)=vol(A)+vol(B), A-B = A n comp(B), A u A = A | `ExactBooleanMetamorphicTests` |
| bound | a size or cost relation | cost under 2^k and symmetry, flattened = unflattened cost | `CostInvarianceTests`, the extra of this run |
| predictable shift | the result moves only as far as the input did | one coordinate 0 -> 2^-1000, ulp shifts | this run (`tiny coordinate`) |

**Control.** A relation that fires on a legitimate difference (a triangulation tie, an out-of-range result, a documented refusal)
is not a finding. Each executed repro is therefore run twice: on the parent of the fixing commit and on master. A relation
**caught** the bug if it fires on the parent and not on the control. This is the one thing the earlier note lacked (it
had no .NET SDK, section 8.9 there) and it changed several verdicts.

**Related methods.** Metamorphic testing (Chen, Cheung, Yiu 1998; surveys Segura et al. 2016, Chen et al. 2018) is the
umbrella. Compiler fuzzing is its closest practice. Csmith (Yang et al. 2011) generates programs free of undefined behaviour
and compares compilers differentially; EMI (Le, Afshari, Su 2014) derives variants of a program that must behave equal on the
given inputs by changing code the run does not reach; GraphicsFuzz (Donaldson et al. 2017) applies semantics-preserving
transformations to shaders and compares rendered images. Transfer to geometry: Csmith is the generator tier
(`DegenerateFamilies`, CsCheck campaigns, MeshFuzzPilot); EMI's "change what the run does not look at" is far-part, interior
refinement and the span change of #719; GraphicsFuzz's transformation list is the relation list. The geometry-specific part has
no published precedent that the search found (no paper on metamorphic testing of mesh Booleans or offsetting; the earlier
note and this one searched "metamorphic testing" with mesh / Boolean / computational geometry). Mesh-Boolean papers evaluate
by comparison against other kernels and by robustness counts on large corpora: EMBER (Trettner, Nehring-Wirxel, Kobbelt, ACM
TOG 41(4), 2022) and Cherchi et al. (ACM TOG 39(6), 2020). The details of their evaluation protocol were not read for this note;
the repository's own `benchmarks/BooleanComparison` is the reference for what is done here.

## 3. Falsification run

`benchmarks/MetamorphicFalsification` (README there). Six procedural repros, built against the parent of each fix; raw output in
`results/parents.txt` and `results/control_master.txt`. Linux x64, net10.0. "Caught" = fires on the parent and not on master.

| Issue (class) | Parent | 2^k scale | Symmetry | Permutation | Cost spread (inside the scale group) | Extra | Verdict |
|---|---|---|---|---|---|---|---|
| #856 MeshMeshCut, cut mesh touches target (absolute tolerances) | 51270ea | fires (5 of 12 scales differ, 1 throws; scales 2^-7 .. 2^5 of the issue's case) **and on master too**: the fix is the opt-in `RefuseContact`, which the harness does not set | fires on both | not caught | n/a | | exception to the rule: the bug is still reproducible on master by design |
| #745 `PointAABBTree3.FastWindingNumber` (absolute padding) | 2c2e300 | **caught** (3 of 3 differ; 185 ms -> 3.4 s, the same run shows the cost spread) | n/a | n/a | (same run) | | caught by scale |
| #644 `ConvexHull2` extent <= 2^-52 (absolute threshold) | e1ff201 | **caught** (4 of 5 differ) | n/a | n/a | n/a | | caught by scale |
| #712 `MeshRobustRepair`, tetrahedron flattened to 2^-46 (cost, 3.4-6 s vs 3 ms) | cbf8b78 | not caught (cost is the same at every scale: the input stays flat) | fires on both | fires on both | not caught | **anisotropic cost caught** (3697 ms flattened vs 3 ms unflattened; master 110 ms vs 4 ms, below the 300 ms floor of the rule) | only the new relation |
| #719 `ExactMeshBoolean` Antipodal, depends on the span (overflow of squared grid coordinates) | c677da7 | not caught | not caught (6 symmetries: 402 triangles, area 3208.7229880566224 each) | not caught | not caught | **tiny coordinate caught** (402 vs 408 triangles, area 3208.7 vs 3220.6; master equal) | only the new relation |
| #710 `ExactMeshBoolean` Difference, cubes at 3.7e-223 (InCircle filter overflow) | f5f4985 | throws at every scale (also the identity run: a crash, no relation needed) | throws | throws | n/a | | crash oracle |

Readings, and what the control changed:
- **Scale is the productive relation, for absolute constants** (#856, #745, #644): the three bugs the harness exists for (for #856 only on the parent and on master alike). It
  fails where the cost or the defect does not depend on the magnitude (#712) and where the defect depends on the span, not the
  scale (#719).
- **#712 is a cost bug of an anisotropic input.** The 2^k scale of the failing input is as slow as the input; only a relation
  that changes the shape ("the same tetrahedron with x unflattened") separates it. This relation (A1 below) is not in the
  earlier catalogue. It is one function: flatten one axis by 2^-46 ... 2^-1 and bound the cost against the unflattened input.
- **#719 needed a perturbation below the features** (EMI's "change what the run does not look at"): one coordinate 0 -> 2^-1000.
- **Symmetry and permutation fired on the control in three of six cases (#856, #712, #710)**, so they caught nothing on this
  set. On #856 the contact makes the cut scale- and axis-dependent on master as well. On #712 the repaired mesh differs per
  symmetry and per order on master too (not diagnosed: a tie-break of the wrap and solidify stages, or a defect; it is one hour
  for the repair owner). On #710 the identity run is outside the range (below). A first run also fired on #719; that was an
  artifact of the harness (a coordinate that is +0 in the transformed frame became -0 after the sign flip and printed as
  "-0"), found by the review and removed (`+ 0.0` in the signature): after it, 402 triangles and area 3208.7229880566224
  under identity and five symmetries. As a regression net they stay (`SymmetryInvarianceTests` with its `Toleranced`
  classes, owner decision of 2026-11-13). The 2^k comparison also produced a range-edge effect (#710 on master: 20
  triangles at 3.7e-223, outside the 2^-200 .. 2^200 range of `ExactMeshBoolean`, 16 at 2^700, 2^740 and 2^760: the rounding
  runs inside the range only, QA.md 2.5a). A scale relation must compare inside the supported range, or the documented
  outside status.
- #856 on master still fires without `RefuseContact` (documented in the class comment); not a new finding.

**Not done.** Step 4 of the brief (black-box runs of the Boolean relations against `manifold` and `cgal_epeck` via the
BooleanComparison workflows) was not run: the harness there reports volume identities per op, but the relations of this note
need a transformed input path in `Program.cs` first, and the expected gain is small: the same relations were all green on
`ExactMeshBoolean` in the existing tests, and a bug of the other kernel found by a relation would be reported upstream, which
is outside the scope. The thingi-contact class is not merged (no branch at this commit). Left as an option if the owner wants
the numbers: adding `scale2k` and `tinyCoordinate` columns to `BooleanComparison` costs about a day.

## 4. Matrix: relation x fault class over the issues of the repository

83 issues classified (**E** = executed in section 3, **R** = body read, **T** = title and the fixing commit's subject only: a T verdict
is an estimate). A cell is the number of defect issues of the class a relation
catches.

| Fault class (issues) | scale 2^k | symmetry | perm / relabel | cost | aniso. / far part | tiny shift | bool. algebra | refine | idemp. | determ. par = ser | no relation (oracle / guarantee / differential / crash) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **A. Absolute constant, scale, span or rotation dependence** (9): #856 E, #745 E, #644 E, #719 E, #854 T, #731 T, #628 T, #664 T, #955 T (open) | **8** (856, 745, 644, 854, 731, 628, 664, 955; #955 and #664 by the non-exact rotation and UTM of `Similarity.All()`) | 0 | 0 | 0 | 0 | **1** (719) | 0 | 0 | 0 | 0 | 0 |
| **B. Cost cliff, hang, livelock** (17): #712 E, #711 T, #636 T, #638 T, #947, #940, #866, #855, #738, #694, #692, #693, #695, #696, #697, #698, #976 | 0 | 0 | 0 | 0 | **4** (712 E: anisotropic; 711, 636 T: by analogy; 638 R: a far point) | 0 | 0 | 0 | 0 | 0 | 13 (the time rule alone; #697 was found by the 2^k test being slow, not by a result difference) |
| **C. Crash on extreme input** (1): #710 E | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 (the identity run throws; the scaled runs throw as well: no relation involved) |
| **D. Guarantee or contract not met** (12): #873 T, #919, #861, #808, #805, #800, #790, #756, #747, #743, #647, #645 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **1** (873: d(A, refine A) = 0) | 0 | 0 | 11 (`NotCleanReason`, bound properties, contract tests) |
| **E. Oracle disagreement of the 2D mesher** (7): #637, #723, #728, #729, #863, #864, #870 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 7 (#863 might be a "duplicate an input point" relation, T) |
| **F. Numerical error of a port** (1): #718 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 (differential against the reference) |
| **G. Concurrency** (1): #882 R | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **1** (parallel = serial at 2^1000 and 1e308) | 0 |
| **H. Platform difference** (3): #802, #803, #804 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 3 (cross-platform determinism, a differential) |
| **Not a defect** (32): enhancements #740 #742 #748 #792 #814-816 #848 #886 #887 #944 #980, local tasks #620 #621 #623-#625 #929, CI flakes #630 #671 #676 #733 #823 #824 #939, fuzz-tool #794, audits and watches #789 #817 #825 #858 #859 #888 | n/a | | | | | | | | | | |

Totals. Relation-found: 8 + 1 + 4 + 1 + 1 = **15 of 51**. No relation: 13 + 1 + 11 + 7 + 1 + 3 = **36** (25 by time rule, crash or contract
check, 11 by oracle or differential). Executed (E) verdicts: five issues (#856, #745, #644, #712, #719) and one crash (#710).
Caveats: 5 verdicts are E (plus the #710 crash), 1 is R (#882: body read), the other 44 are T (title and the subject line of the
fixing commit), so the 15 / 51 is an estimate within a few issues either way. The classes overlap (an issue may be caught by two relations; each issue is counted once, in the
column of the relation that found or would most simply find it). The point is the shape, not the decimals: **scale (with the
non-exact similarities) covers the absolute-constant class, and every other relation is the mechanism for at most four issues.**

## 5. Relations: what to build

The catalogue of the earlier note stands (S1-S5, F1-F3, B1-B8, C1-C2, I1, A1-A2, L1-L2, M1-M2, W1-W4). This run adds or
re-ranks:

| ID | Relation | Evidence | Verdict |
|---|---|---|---|
| **A1** | **Anisotropic scale**: flatten or stretch one axis by 2^(-46 .. +46) (exact); the result may change (a flat input is legitimately different), the **cost and the validity may not** (time, wrap or vertex counts within a factor, `NotCleanReason == ""` where the unflattened input is clean) | #712 E, #636, #711 (the fuzz found all three by time) | **build** (repair, exact boolean, alpha wrap, QualityMesh2, `MeshRobustRepair`) |
| **P1** | **Tiny perturbation below the features**: set the smallest coordinate (or a vertex) to 0 -> 2^-500 ... 2^-1000, or move a vertex one ulp: the result changes by at most the shift (areas and counts within the input's own tolerance; classification of whole pieces unchanged) | #719 E | **build** for classifiers (Boolean winding, open-mesh Antipodal, `IsInside` family) |
| S1 symmetry | exact 48 | 0 true positives in 6 repros; fired on the control in 3 (#856, #712, #710) | **keep as regression net**, subset in PR tier (as decided) |
| C1 cost under 2^k and symmetries | | #745 (20x), but #712 not | keep (it exists) |
| R-scale | 2^k | 8 of 15 relation-found issues | keep, **compare inside the supported range only** |
| B1-B8 Boolean algebra beyond pairs, F1 refinement, I1 idempotence, L1 far part | | 1-2 issues each at most (873 for refinement, 638 for far part), none for B / I | **park** (cheap net, not a finder) |
| D1 "duplicate an input point must not change the mesh" (EMI-like for the 2D mesher) | #863 T | unverified | **try once** (hour-sized probe) |

## 6. Cost per relation

| Relation | Code | Test time (PR tier) | Failure analysis |
|---|---|---|---|
| 2^k scale | exists (`Similarity.All()`) | milliseconds per case | low; false positives only at range edges |
| symmetry (S1) | exists | subset of 6: sub-second per op; all 48 nightly | **high**: ties and range effects (3 of 6 fired on the control); the signature needs `-0` normalised, a `Toleranced` class per op |
| permutation | exists | milliseconds | low |
| cost (C1) | exists | counters: free; wall clock: noisy (#676, #671) | medium (JIT warm-up: this run saw a 20x spread from the first call alone until it ran a warm-up) |
| A1 anisotropic | one helper, per op a table row | seconds (the failing case was 3.4 s on the bug) | low: a cost bound, not a result compare |
| P1 tiny shift | one helper per classifier | sub-second | medium: the tolerance of "predictable shift" is per op |
| Boolean algebra, refinement, idempotence | exist or mid-size | 1-5 s per family | medium: circular oracles when both sides use the same kernel |
| Black-box vs other kernels | `BooleanComparison` + CI | minutes per run, GitHub runner | high: another kernel's tie-breaks |

## 7. Recommendation to the owner and stop rules

**Build** (one PR, tier M, test-only, about two days): A1 (`MeshRobustRepair`, `ExactMeshBoolean`, `MeshAlphaWrap`,
`QualityMesh2`) and P1 (classifiers: Boolean winding, `Antipodal`, `FastWindingNumber`), each with the three companions of
CLAUDE.md. **Park** the Boolean-algebra, refinement, idempotence and far-part relations (the earlier note's R3-R5) until an area
is reopened: one issue each at most (#873 for refinement, #638 for far part), none for Boolean algebra or idempotence. **Drop** black-box comparison against other kernels as a bug-finding method: it needs
a reference, which is a differential and exists already. **One cheap probe**: D1 on `QualityMesh2` (is #863 a family?).

**Stop rules.** (1) After the A1 + P1 PR: if the two relations found no defect on the 25 operations of
`RobustnessSweepTests.Ops` (no issue opened by them), stop adding relations; the class is exhausted by scale, fuzz and the
finding-driven tests. (2) A relation row stays in the PR tier only if it fired on a past bug or costs under 100 ms per
operation; otherwise nightly. (3) Re-run `benchmarks/MetamorphicFalsification` against the next 15 closed `bugfix` issues; if
fewer than a third are catchable by a relation, the method is at its limit and the owner decides whether to continue the
research item. (4) The "benchmark applicable to other kernels as black boxes" part of the research entry is **not
recommended**: no relation fired without the control run, and a control run needs the fixed version of the same kernel.

## 8. Evidence and honesty

- The 83 issues were listed with the GitHub tools; bodies were read for #856, #719, #882, #745 and the fix tests of #710, #712;
  everything else is classified from the title and the fixing commit's subject (**T**). The classification is mine and
  may be disputed per row; the six executed repros are the firm part.
- The probes use one input per issue (the issue's own where it gives one). A relation not firing on one input does not prove
  it cannot fire: "not caught" means not caught on the issue's repro.
- Parent commits are the first parent of the merge commit or the parent of the fixing commit (`git log --merges --grep`), listed
  in `benchmarks/MetamorphicFalsification/README.md`.
- No library code was changed; no new defect was found, so no issue was opened.

## 9. Sources

Verified by a search on 2026-11-16 (title, authors, venue, DOI where given); no code was read or ported from any of them.

- Chen, Kuo, Liu, Poon, Towey, Tse, Zhou, ACM Computing Surveys 51(1), art. 4, 2018, doi:10.1145/3143561.
- Segura, Fraser, Sanchez, Ruiz-Cortes, IEEE TSE 42(9), 805-824, 2016, doi:10.1109/TSE.2016.2532875.
- Yang, Chen, Eide, Regehr, "Finding and understanding bugs in C compilers", PLDI 2011, pp. 283-294, doi:10.1145/1993316.1993532.
- Le, Afshari, Su, "Compiler validation via equivalence modulo inputs", PLDI 2014, doi:10.1145/2594291.2594334.
- Donaldson, Evrard, Lascu, Thomson, "Automated testing of graphics shader compilers", OOPSLA 2017, doi:10.1145/3133917.
- Trettner, Nehring-Wirxel, Kobbelt, "EMBER: Exact Mesh Booleans via Efficient and Robust Local Arrangements", ACM TOG 41(4), 2022.
- Cherchi, Livesu, Scateni, Attene, "Fast and Robust Mesh Arrangements using Floating-point Arithmetic", ACM TOG 39(6), 2020,
  doi:10.1145/3414685.3417818.
- Chen, Cheung, Yiu, HKUST-CS98-01, 1998: from memory, not verified; cited only as the origin.

## 10. Status of the A1 and P1 build (2026-11-16)

- Built: `tests/geometry3Sharp.Tests/MetamorphicRelationTests.cs` (A1 for `MeshRobustRepair`, `ExactMeshBoolean`, `MeshAlphaWrap`,
  `QualityMesh2`; P1 for the winding number, `DMeshAABBTree3.IsInside` and the open-mesh Antipodal union), helpers in
  `Harness/MetamorphicRelations.cs`. The PR tier runs a few exponents per relation; the nightly tier runs the full sweep.
- Validation (`benchmarks/MetamorphicFalsification/results/relations.txt`): A1 fires on the parent of #712 (cbf8b78: 136 018 wrap
  triangles against 0), P1 on the parent of #719 (c677da7: 402 vs 408 triangles); both quiet on master.
- Sweeps on master found two defects, not fixed here, pinned with their issues: **#985** (`ExactMeshBoolean` with SafeRounding,
  the union of flattened inputs at 2^±18 and beyond: an empty, open result with status Success; 689 of the 1656 rows) and
  **#986** (`QualityMesh2` returns Empty for a valid pentagon at 2^±41 and beyond).
- Caveat on the 2D mesher: its output size follows the aspect ratio (a sliver needs points along its length), so A1 bounds
  it by the reference times 2^|e|, not by the reference. Where the input's Steiner limit is reached the output says so.
- Not done: Stryker runs on the new file (not in the mutation config); the sensitivity check is the parent runs above.
