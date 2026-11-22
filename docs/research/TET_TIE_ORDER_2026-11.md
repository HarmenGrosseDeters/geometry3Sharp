# Tie order of the tet improver: distribution over 8 relabelings, and what a restart buys (2026-11-22)

Research spike (owner go 2026-11-22), shortlist item 5.2 of [CROSS_FIELD_TRANSFER_2026-11.md](CROSS_FIELD_TRANSFER_2026-11.md). No library
change and no change of a default. The harness is the `tieorder` command of
[benchmarks/TetMeshBenchmark](../../benchmarks/TetMeshBenchmark/README.md) (`TieOrder.cs`); the code it looks at is stage 2 of
[TET_MESHING.md](../design/TET_MESHING.md) (`TetMeshImprover`, sections 15 to 17.16).

**Question.** Does the effort or the outcome of the improver vary by a large factor with the order of equal candidates, so that a restart
schedule (Luby, Sinclair, Zuckerman 1993) or a portfolio of orders would pay?

**Answer in short.**
- *Effort (the work cap, #1154): no.* On the four meshes above 20 s at the floor 20 the fastest of 8 orders needs 0.64 to 0.90 of the
  median's visits (every visit measure, every mesh). The stop rule of the brief holds for this half: **the cap of #1154 is the answer**,
  and no order reaches the floor 20 on any of them (0 of 36 runs).
- *Outcome (the floor 10 at the default): yes.* 38781 reaches 10 degrees under 64 of 64 shuffled orders and 282598 under 38 of 64. The
  stop rule of the brief does **not** hold for this half.
- *What acts.* The order of the tets and the corner order within a tet, each alone. On these two meshes the vertex ids and the order of
  the boundary triangles change nothing (the runs are bit for bit today's).
- *It cuts both ways.* A shuffled order makes a mesh that reaches the floor today miss it in 6 of 236 runs on 44 other meshes (3 meshes).
  Missing the floor is a property of the pair (mesh, order), not of the mesh.
- *Proposal for the owner (section 5):* an opt-in restart of stage 2 under the next order, only for a mesh that misses the floor, with a
  fixed visit budget per try. Not a Luby schedule over the rescue phase: a try at the default floor costs 0.05 to 0.43 visits per vertex,
  a range a fixed budget covers.

## 1. Method

**The switch.** `TetMeshImprover` chooses by index and list position wherever candidates are equal to it: it tries the 6 edges, 4 faces
and 4 vertices of a bad tet in corner order and keeps the first operation that helps; bad tets of equal quality go in id order; a vertex's
star is summed in list order. The harness leaves the library alone and **relabels the stage-1 mesh** before the improver: vertex ids
permuted, tets and boundary triangles shuffled, the corners of each tet in one of the 12 even permutations (the orientation stays),
boundary triangles rotated. The geometry is bit for bit the same. Order 0 is the stage-1 mesh as it is (today's order); order k >= 1 is
the relabeling of seed k (SplitMix64, so the same on every runtime). `G3_TIE_PARTS` limits the relabeling to one part. One part is not
purely a tie switch: the quality of a tet is computed from its corners in their order, so a corner permutation can change it in the last
bits (not measured). The tet order changes no quality value and has the same effect (section 3), so the conclusion does not hang on this.

**Checks of the harness** (`tieorder check`, procedural inputs, 5 s): order 0 gives the result of `MeshTetrahedralization` bit for bit
and the same counts; a relabeling keeps the set of tets and boundary triangles by position and is valid; each order is deterministic; the
probe does not change the run; at least one relabeling changes the run. On the real inputs order 0 reproduces the documented rows
(38781: worst 9.432, 20735 tets; 282598: 9.532, 18563 tets, TET_MESHING 17.11; the floor-20 end lines of the `runs/tet-work-cap`
traces, tets below the floor and worst angle to the last digit). The check has no input that misses the floor, so the rescue columns are
covered by the real rows only (the first rescue sample of every trace is at or after `visits_at_rescue`).
Every one of the 605 results is valid (`TetMeshQuality.NotValidReason`).

**Effort is counted in visits** (`TetMeshImprover.Visits`, the unit of `MaxImproveVisitsPerVertex`), never in time. The improver's
`CancelF` is the probe: it samples the tets below the floor and the worst angle every vertices / 32 visits.

**Inputs.** All with `RelativeAlpha` 40, `RelativeOffset` 600 and the defaults (`RescuePasses` 150, no work cap), one process at a time:
the meshes above 20 s at the floor 20 in the three baselines of `runs/tet-work-cap` (38781, 40011, 46017 in all three, 85539 in one: the
"3 to 4" of TET_MESHING 17.16), and 38781 and 282598 at the default floor 10. Meshes are fetched at run time and not kept.

## 2. Effort at the floor 20: no order is much faster

Visits: *total* until the improver stops; *rescue* in the rescue phase; *own* to the order's own final count of tets below the floor
("visits to reach the final quality"); *common* to the count every one of the 9 orders reaches (the same goal for all). Minimum / median /
maximum over the 8 relabelings, today's order beside them:

| mesh | measure | today | minimum | median | maximum | minimum / median |
|---|---|---|---|---|---|---|
| 38781 | total visits | 145195 | 108243 | 128417 | 158805 | 0.84 |
| | rescue visits | 96752 | 62104 | 79678 | 103581 | 0.78 |
| | visits, own final | 141454 | 96942 | 120372 | 155150 | 0.81 |
| | visits, common level (672 below) | 61204 | 48150 | 55105 | 155150 | 0.87 |
| | tets below 20 | 628 | 392 | 515 | 672 | |
| | worst dihedral | 12.86 | 11.46 | 12.80 | 13.53 | |
| 40011 | total visits | 148494 | 138488 | 154264 | 175414 | 0.90 |
| | rescue visits | 78216 | 68051 | 80055 | 99605 | 0.85 |
| | visits, own final | 148106 | 121694 | 142142 | 163158 | 0.86 |
| | visits, common level (648 below) | 72988 | 72136 | 81295 | 131350 | 0.89 |
| | tets below 20 | 504 | 423 | 513 | 648 | |
| | worst dihedral | 16.02 | 11.22 | 14.39 | 16.19 | |
| 46017 | total visits | 216175 | 216963 | 242240 | 265088 | 0.90 |
| | rescue visits | 113168 | 120721 | 137817 | 162147 | 0.88 |
| | visits, own final | 203280 | 183392 | 214544 | 247456 | 0.85 |
| | visits, common level (1062 below) | 107536 | 99968 | 112112 | 227920 | 0.89 |
| | tets below 20 | 721 | 771 | 889 | 1062 | |
| | worst dihedral | 14.63 | 12.03 | 13.50 | 14.34 | |
| 85539 | total visits | 51678 | 32574 | 40342 | 46593 | 0.81 |
| | rescue visits | 25216 | 13221 | 17707 | 22306 | 0.75 |
| | visits, own final | 33712 | 20124 | 31476 | 43860 | 0.64 |
| | visits, common level (163 below) | 33712 | 19092 | 23736 | 26144 | 0.80 |
| | tets below 20 | 163 | 88 | 111 | 143 | |
| | worst dihedral | 13.80 | 13.21 | 14.19 | 15.04 | |

Reading: the lowest minimum / median is 0.64, above the 0.5 of the stop rule on every mesh and measure. The fastest order reaches the
common level in 0.57 to 0.99 of today's visits; the slowest needs up to 2.8 times the median (38781). All 36 runs use all 200 passes and
none reaches the floor: the visits go into a quality level, not into a search that succeeds, which is where the brief expected the
theory to fail. The spread of the result is real (the tets below 20 differ by up to 1.7 times between the relabelings, 1.9 times with
today's order, the worst angle by up to 5 degrees) but it is a spread of a hill climb's end point, and a restart would pay 7 to 43 visits per vertex (the median run of each mesh) for another draw of it.
Eight samples cannot settle the shape of a tail, only this practical question.

## 3. The floor 10 at the default: the order decides

| mesh | today | 8 relabelings: reach 10 degrees | 64 relabelings: reach 10 degrees | visits of a run, 64 orders (minimum / median / maximum) | per vertex (maximum) | passes (maximum) |
|---|---|---|---|---|---|---|
| 38781 | 9.432, 1 tet below, 1099 visits | 8 of 8 | **64 of 64** | 1008 / 1052 / 1258 | 0.21 | 111 |
| 282598 | 9.532, 1 tet below, 388 visits | 4 of 8 | **38 of 64** | 259 / 297 / 532 | 0.10 | 57 |

The 26 misses of 282598 leave 1 tet below the floor in 19 runs and up to 10 in the others (worst 8.39 degrees: an order can also be worse
than today's). The longest run of misses in the 64 is 4 (orders 1 to 4). The rescue phase ran in 3 of the 64 successes of 38781 and in
17 of the 38 of 282598: on 38781 the main passes decide, on 282598 both phases do.

**Which part of the relabeling acts** (16 orders per part, runs that reach 10 degrees):

| part alone (16 orders) | 38781 | 282598 |
|---|---|---|
| vertex ids | 0 of 16 (every run bit for bit today's) | 0 of 16 (the same) |
| boundary triangles | 0 of 16 (the same) | 0 of 16 (the same) |
| order of the tets | 14 of 16 | 10 of 16 |
| corner order within a tet | 15 of 16 | 11 of 16 |

So the outcome hangs on which bad tet of equal rank is visited first and which of its edges is tried first, as TET_MESHING 16.3 saw
("sensitive to choices between equal candidates").

**The other direction** (does a relabeling break a mesh that reaches the floor today?):

| set, default floor | today | relabelled runs that reach 10 degrees |
|---|---|---|
| 40-mesh subset (`thingi-subset-ids.txt`), 4 orders | 40 of 40 | 159 of 160: 4 of 4 on 39 meshes, 75915 misses once (9.36 degrees) |
| the 4 meshes the rescue phase lifted (17.11) and 75915, 16 orders | 5 of 5 | 74 of 80 (75915 13, 763398 15, 83719 14, 138102 and 50656 16 of 16) |

The two sets share 75915 and its orders 1 to 4 (it was added to the second set because it missed in the first): without the double count,
6 misses in 236 relabelled runs on 44 meshes. The subset is an easy set: at alpha / 40 the rescue phase runs on none of the 40 today, 18
need at most 20 visits and one has no tet below the floor after stage 1, so 4 of 4 there says little about a mesh's rate.

Under a shuffled order, reaching the floor is a draw per (mesh, order): 16 of 16 on two of the five harder meshes, 81 to 94 % on three,
59 % on 282598 (38 of 64; 95 % interval about 46 to 71 %). Today's order is **not** one of these draws: it is the stage-1 order, a
different policy. On 38781 it is the only miss among 65 orders, and 75915 passes under it and misses under 3 of 16 shuffles, so neither
policy is better across meshes on this evidence. Two consequences beyond this spike: the two open meshes of HP 5 are not geometric
obstructions of the method (a different order of the same operations lifts them), and a change of the improver that moves an order can
change which meshes miss, so a count such as 498 of 500 should not be read to the last mesh between versions (as TET_MESHING 16.3 saw
at alpha / 20: 36, 37 and 38 of 40).

## 4. What the theory says here

Restart schedules are for runs that succeed or fail with a run time that varies by orders of magnitude between seeds. The floor-10 case
has the first property and little of the second: on the two open meshes a try costs 0.05 to 0.21 visits per vertex whatever the order
(maximum / median 1.2 to 1.8 over 64 orders) and ends by itself (the improver stops when a pass changes nothing). Over all floor-10
runs of this note the most expensive try is 0.43 visits per vertex (75915, order 10: all 200 passes, and it misses), so a try can run to
the pass limit, and the passes (up to 200; each scans every tet once) are a second cost that visits do not count. With a success rate p
per try and a cost in a range this narrow, a fixed budget per try and the orders one after the other is the plain schedule, expected
tries 1 / p. Luby's sequence of budgets (1, 1, 2, 1, 1, 2, 4, ...) is the hedge for an unknown heavy tail; nothing measured here shows
one, on few meshes. The floor-20 case has neither property. So the transfer that fits is the simplest member of the family, a **portfolio run in sequence, stopped at the first success**
(Huberman, Lukose, Hogg 1997; Gomes, Selman 2001), not the schedule.

## 5. Proposal (owner decision; no code in this PR)

An opt-in option of `MeshTetrahedralization`, working name `ImproveOrders` = K (default 1 = off, bit for bit as today):

- If tets below `SliverDihedralDegrees` are left after stage 2, run stage 2 again **from the stage-1 mesh** under order 2, 3, ..., K
  (the relabeling of this note: tet order and corner order; the vertex ids need not move) and stop at the first order that reaches the
  floor. If none does, keep the best run (fewest tets below the floor, then the highest worst angle, then the lowest order), which may be
  order 1: an order can be worse than today's.
- **Cost.** Nothing for a mesh that reaches the floor (498 of 500 at the default; their results stay bit for bit). For a mesh that
  misses: at most K - 1 more runs of stage 2, each up to the pass limit (measured: up to 0.43 visits per vertex and 200 passes at the
  default floor). No snapshot is needed beyond the stage-1 mesh, which `Compute` holds anyway (the improver works on its own copy).
- **Not measured: the brief's restart of the rescue phase alone**, from the mesh after the main passes. The harness cannot do it
  without the improver's state (the slide origins of the boundary vertices). It would save the main passes of each try (most of a try's
  visits on these meshes) and could only use the share of the successes that the rescue phase brings (3 of 64 on 38781, 17 of 38 on
  282598). A restart of the whole of stage 2 is the measured variant.
- **Expected effect at the default.** 38781: every shuffled order lifts it, so K = 2 is enough. 282598: 59 % per try (46 to 71 %), so
  four more tries leave a miss with probability about 3 % (1 to 9 %) for a random choice of seeds, if the tries are independent. With
  this note's seeds and all four parts relabelled, the first four relabelings miss it and the fifth lifts it, so K = 5 would still miss
  and K = 6 would not (a relabeling of tet and corner order only draws other permutations from the same seeds): the choice of seeds is
  part of the result (below), and a K fitted to two meshes is no evidence for the next one.
- **A bound per try is needed.** At the floor 20 a try costs 7 to 43 visits per vertex (the median run of each mesh) and no order
  reaches the floor, so K tries would cost K times that for nothing; at the default floor a try can still use all 200 passes. The option
  should therefore act only with a visit budget per try (`MaxImproveVisitsPerVertex`, or its own). This is the one place a schedule of
  budgets could enter later; the numbers here do not call for one.
- **Bit-for-bit rule: the schedule becomes part of the result.** K, the generator and its seeds, which parts are relabelled, the order of
  the tries, the budget per try and the rule that picks the kept run all fix the output of every mesh that misses the floor at order 1.
  None of them can be tuned afterwards without changing outputs. The 2^k scale invariance holds (a relabeling is index-only); the result's
  vertex and tet order differ from today's on the meshes where a later order is kept.
- **Tests it would need** (CLAUDE.md): invariance over `Similarity.All()`, reuse, a golden digest for K = 1 (off), a procedural mesh that
  misses at order 1 and reaches the floor at a later one (to be found; the harness can search), and the result checked, not only the status.

**What this does not give.** No guarantee: the floor stays a target (HP 5, P5(b); a proof needs another method). No help for the slow
meshes at higher floors: there the cap of #1154 stands. And it is evidence from 2 open meshes and 44 others, not from the 500-mesh
sample: before a default, the sample should run with the option on a runner (the 498 are untouched by construction; the open point is
only the two and the time).

**Alternatives.** (a) Leave it: 498 of 500 with a documented reason for the two. (b) Make a shuffled order the default order: nothing here
shows a gain (a shuffled order also misses, 6 of 236 runs on meshes that pass today; its miss rate on the 500-mesh sample was not
measured) and every output would change.

## 6. Commands and data

```
dotnet build benchmarks/TetMeshBenchmark -c Release
D=benchmarks/TetMeshBenchmark/bin/Release/net10.0/TetMeshBenchmark.dll
dotnet $D tieorder check
benchmarks/TetMeshBenchmark/compare/run_tie_order.sh $D <mesh dir> <out dir> 8                    # sections 2 and 3, 8 orders
python3 benchmarks/TetMeshBenchmark/compare/summarize_tie_order.py <out dir>/tie-order.csv <out dir>/tie-order-trace.csv
dotnet $D tieorder <mesh dir>/38781.stl 40 600 10 64                                              # 64 orders (also 282598)
G3_TIE_PARTS=t dotnet $D tieorder <mesh dir>/38781.stl 40 600 10 16                               # one part: v, t, c or b
G3_TIE_INPUTS="$(sed 's/$/:10/' benchmarks/TetMeshBenchmark/compare/thingi-subset-ids.txt | tr '\n' ' ')" \
  benchmarks/TetMeshBenchmark/compare/run_tie_order.sh $D <mesh dir> <out dir> 4                  # the 40-mesh subset
G3_TIE_INPUTS="138102:10 763398:10 83719:10 50656:10 75915:10" benchmarks/TetMeshBenchmark/compare/run_tie_order.sh $D <mesh dir> <out dir> 16
```

Rows (commit e09cf478 of master plus the harness, cloud container, net10.0): `benchmarks/TetMeshBenchmark/results/tie-order-2026-11-22.csv`
(sections 2 and 3, 8 orders), `tie-order-floor10-64-orders-`, `tie-order-floor10-parts-`, `tie-order-subset40-floor10-` and
`tie-order-rescued-floor10-2026-11-22.csv`. The 64-order file is the two commands' rows in one file; the parts file is the rows of the
eight `G3_TIE_PARTS` commands with the part as a first column added by hand (the harness does not write it). `imp_s` is informative only
(it includes the probe). The per-sample traces of section 2 are on the data branch `runs/tet-tie-order`.

## 7. Sources

Papers only, as cited in CROSS_FIELD_TRANSFER_2026-11.md section 9: Luby, Sinclair, Zuckerman 1993 (restart schedule); Gomes, Selman,
Crato, Kautz 2000 (heavy tails); Huberman, Lukose, Hogg 1997 and Gomes, Selman 2001 (portfolios). The SplitMix64 generator of the harness
is the public-domain one of Steele, Lea, Flood (OOPSLA 2014). No code of another mesher was read or run.
