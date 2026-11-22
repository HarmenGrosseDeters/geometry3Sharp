# Clause learning in the 3B local repair: spike (2026-11-22)

Shortlist item 5.1 of [CROSS_FIELD_TRANSFER_2026-11.md](CROSS_FIELD_TRANSFER_2026-11.md) (owner go 2026-11-22). Measurement code and full
tables: [benchmarks/QuadFlow3BSat](../../benchmarks/QuadFlow3BSat/README.md). No library change; a solver in the library was not built.

**Result: stop.** The fallbacks of `QuadRemesher.PureQuads` come from the problem, not from the search. With every local instance decided
(no budget, no instance left undecided), 3 of the 29 fallbacks of master and 1 of the 11 fallbacks of PR #1293 become fold-free, and that
one still ends as `FlippedFaces`: the clean count would go from 25 to 28 on master and stay at 43 with PR #1293. The stop rule of the brief
(fewer than 8 runs; with PR #1293 fewer than a third of its fallbacks) applies on both baselines. The unsatisfiable instances are a test set
for the sheaf H1 question, and the measurement already gives that question a concrete candidate answer: the lattice area of the region.

## 1. Question and method

*Question (the brief):* how many `BudgetExceeded` fallbacks of [QuadFlow3BGate](../../benchmarks/QuadFlow3BGate/README.md) are caused by
the search (`GroupSatSolver`: unit propagation, chronological backtracking, no learning, 300k decisions in total and 100k per attempt)
and how many by local instances that have no solution?

*Method.* Per fallback run the benchmark (1) reads from the library's counters which budget ended it, (2) takes the integer map after the
greedy moves, (3) replays the library's repair loop with the library's own solver and budgets and checks that the counters equal the
library's (so the instances built outside the library are the library's), and decides every instance the DPLL left at its budget, (4)
runs the same loop with a clause-learning solver and no budget, and (5) where no fold is left, runs the library's remaining steps and the
independent checks of the gate. A seed triangle whose instances of all six rings are unsatisfiable is called an *obstructed seed*.

The solver is a throwaway, written from the papers (conflict analysis at the first unique implication point and learned clauses:
Marques-Silva and Sakallah 1999; the architecture with a trail, reasons, watched literals and variable activities: Eén and Sörensson 2003;
restarts: Luby, Sinclair and Zuckerman 1993). No solver code was read or run. Effort is counted in conflicts and decisions, never in time.
Every answer of the solver is checked (the instances refuted while they are built have an empty clause and need none): a model against
the clauses and on the map; an unsatisfiable answer by reverse unit propagation of the learned
clauses (Goldberg and Novikov 2003) in a checker that shares no code with the solver; both against the library's DPLL where that decides
within 20000 decisions.

Baselines: master e09cf478 and the head of PR #1293 (637990d1; `TrySat` and `RepairSat` are the same text on both). PR #1293 was merged
while the spike ran, so "master" in this note is the tree before it and the PR #1293 column is today's master for the repair (a smoke run of the
capsule rows on the merged tree gives the same statuses and the same replay check). Commands:

```
dotnet run -c Release --project benchmarks/QuadFlow3BSat -- list=0 dump=<dir>      # master, results_master.txt
dotnet run -c Release --project benchmarks/QuadFlow3BSat -- dump=<dir>             # built against PR #1293, results_pr1293.txt
dotnet run -c Release --project benchmarks/QuadFlow3BSat -- selftest=1
```

## 2. Numbers

| | master e09cf478 | PR #1293 head |
|---|---|---|
| clean / fallback (54 runs) | 25 / 29 | 43 / 11 |
| fallback status: `BudgetExceeded` / `OrientationFolds` / `FlippedFaces` | 26 / 3 / 0 | 3 / 0 / 8 |
| ended by: SAT decisions (300k) / no budget (every seed failed) / splits / not the repair | 26 / 3 / 0 / 0 | 3 / 0 / 0 / 8 |
| replay with the library's solver equals the library's counters | 29 / 29 | 3 / 3 |
| instances the DPLL left at a budget: satisfiable / unsatisfiable / undecided | 13 / 66 / 0 | 0 / 8 / 0 |
| learning trajectory, instances: satisfiable / unsatisfiable / undecided | 499 / 8905 / 0 | 6 / 242 / 0 |
| seeds solved / obstructed | 499 / 1190 | 6 / 37 |
| runs with all instances decided | 29 / 29 | 3 / 3 |
| **fallbacks fold-free if every satisfiable instance is solved** | **3 / 29** | **1 / 11** |
| of those, `Success` with the independent checks of the gate | 3 | 0 |
| conflicts over all instances / most on one instance | 3.11M / 93002 | 0.29M / 52174 |
| checks: valid RUP proofs / invalid; agree with the DPLL / disagree | 5082 / 0; 4315 / 0 | 189 / 0; 135 / 0 |

- **No split budget.** No fallback on either baseline is ended by the split budget; `BudgetExceeded` is the SAT decision budget in all
  of its 26 + 3 cases (the third doubt of the brief).
- **Master, per run.** The three runs that become fold-free are the torus at scale 0.8, 1.25 and 1.5, and all three complete to `Success`
  with the independent checks. Two of them are `OrientationFolds` rows, not `BudgetExceeded`: there the per-attempt budget of 100k decisions
  ended instances that are satisfiable, the seed was marked failed, and the total budget was never reached. So of the 26 `BudgetExceeded`
  runs exactly one (torus at scale 1.25) is caused by the search, and not because the DPLL misses a model: its three budget instances
  are unsatisfiable, the total budget is spent on them, and the later seeds, which are solvable, are never tried. In the other 25, and in the capped cylinder at ratio 0.7, obstructed seeds
  remain (2 to 192 folds left per run). The plain torus is clean in all six variations with PR #1293, by its other changes.
- **PR #1293, per run.** 8 of the 11 fallbacks are `FlippedFaces`: the repair ends with no fold and the extraction check rejects the
  mesh, so no solver can change them. Of the 3 `BudgetExceeded` runs, the capsule at ratio 0.35 becomes fold-free (4 folds, one seed solved) and then ends as `FlippedFaces` like the others; the capsule at scale 1 and the capped cylinder at ratio
  0.35 keep 19 and 16 folds, every one an obstructed seed.
- **Where the DPLL's budget went.** On master 66 of the 79 instances that ended at a budget are unsatisfiable (with PR #1293 8 of 8): the
  budget is spent on proving that nothing can be done, one to three instances per run, 27 at ring 5 and 52 at ring 6 (4 and 4 with PR #1293). This agrees with the data branch
  `runs/quad-folds` (10 and 100 times the budget: 0 of 26).
- **Learning does what the literature says.** No instance is left undecided; the hardest takes 93002 conflicts, the sum over the 5594 instances of master that reach
  the solver is 3.1M conflicts (another 3889 are refuted while they are built: a triangle with one mutable edge has no value for it), where the DPLL exhausts 300k decisions on three instances. The search is not the limit; a better search reaches the
  same fallback with a proof.

## 3. The obstructed seeds

Classification of the obstructed seeds by the region of their largest ring (ring 6: about 55 to 70 triangles and 70 to 90 mutable edges):

| | master (1190 seeds) | PR #1293 (37 seeds) |
|---|---|---|
| a singular vertex in the region / the seed at a singular vertex | 87 / 12 | 11 / 0 |
| folds in the region: only the seed / 2 to 5 / 6 or more | 36 / 477 / 677 | 2 / 29 / 6 |
| sum of det over the region: negative / zero / positive | 1057 / 58 / 75 | 29 / 3 / 5 |
| **refuted by the area alone** (the region is a disk without a singular vertex, every triangle closes, the sum is negative) | **983** | **24** |
| regions that are not a disk | 3 | 0 |
| the unsatisfiable instances the DPLL left at its budget (library trajectory) that the area refutes | 45 of 66 | 5 of 8 |
| negative sum: the first larger ring with a sum >= 0 is 1-2 rings larger / 3-6 larger / up to ring 20 / none up to ring 20 (a distance, not a bound: see below) | 222 / 308 / 338 / 189 | 7 / 10 / 12 / 0 |

**The area explains most of them.** The sum of the integer determinants of the triangles of a region is twice its lattice area. Take a
region that is a disk (connected, Euler characteristic 1), holds no singular vertex, and whose triangles close (the three offsets sum to
zero, which holds after the flow and is checked). The frame rotations are quarter turns, which keep det, and without a singular vertex
they can be removed by one choice of frame over the disk; the map is then a piecewise linear map of a disk into the plane, and its signed
area is the area enclosed by the image of the boundary. It depends on the offsets of the boundary edges only, which the local instance
keeps fixed. So no choice of the interior offsets changes the sum, and a region with a negative sum has a triangle with det < 0 under
every assignment. The benchmark tests exactly these premises per region (`AreaRefuted`): 983 of the 1190 obstructed seeds of master
(83 %) and 24 of the 37 with PR #1293 are refuted by this one-line invariant at ring 6, and 45 of the 66 (5 of the 8)
unsatisfiable instances on which the library's DPLL spent its budget. The solver's proofs say the same without knowing it. The invariant
does not hold for a region that wraps a handle or encloses several singular vertices, which is why those are not counted. This is the
local form of what [QUAD_SHEAF_OBSTRUCTION_2026-11.md](QUAD_SHEAF_OBSTRUCTION_2026-11.md) 3.3 measured for whole maps (fallback rows
with a quarter or more of their cells missing), and it is why a larger budget, a better solver and the 3B repair as designed cannot remove
these folds: the area the fold needs is not inside the region.

The remaining seeds (a zero or positive sum, a singular vertex in the region, or a region that is not a disk) are not explained by the sum alone; they are the interesting
part of the test set (a zero sum forces every triangle of the region to be degenerate; a positive sum can still be unsatisfiable through
the range of the offsets, -1 to 1 per coordinate, or through folds that sit outside the area the region can move).

**A larger ring.** The clause-learning trajectory was not completed for rings above 6: a run to ring 9 and one to ring 8 with a limit of
300k conflicts per instance did not finish the first run within the time limit of the session's background job (the solver has no clause
deletion, and unsatisfiable instances of that size are far harder than those of ring 6). The last row of the table is a weak substitute: how
far the region must grow before its sum of det is no longer negative. It is a distance, not a bound: the larger regions hold singular
vertices and can wrap the shape, where a negative sum refutes nothing and a sum >= 0 promises nothing.

**The test set for the sheaf H1 question.** `dump=<dir>` writes every ring of every obstructed seed
(`obstruction_seed<t>_r<ring>_UNSAT.cnf`; with the budget instances 3538 files for master and 173 with PR #1293, the count is in the
results files) and the budget instances, with the
seed, the ring and the master triangle and slot of every variable in the header; `list=1` prints one row per seed with the numbers of the
table above. The files are regenerated by the command and are not committed. Every `UNSAT` file has a checked refutation. What the H1
spike can now test directly: whether the support of its obstruction class marks the regions with a negative sum (the invariant above
predicts it must, for regions without cones), and what it says about the seeds the sum does not explain.

## 4. Recommendation, ranked

| # | option | gain on the 54 runs | effort | leaning |
|---|---|---|---|---|
| 1 | **Do not build a clause-learning solver into the library for stage 3B** (the stop rule) | master: 3 runs (all torus, which PR #1293 already makes clean another way); PR #1293: 0 | none | stop |
| 2 | Hand the instances to the sheaf H1 spike as its test set, with the area invariant as the hypothesis to confirm or refute (section 3) | a statement, no clean row | none here | do, if that spike is started |
| 3 | The region's sum of det as an exact pre-check in `TrySat`: skip the instance when the premises of section 3 hold (a disk, no singular vertex, a negative sum) | no clean row; it removes 45 of the 66 budget-bound proofs of master (5 of 8 with PR #1293) and with them part of the SAT time of a fallback (0.13 to 0.37 s per fallback on master, gate README; nothing is timed here), on 3 rows with PR #1293. It also turns `BudgetExceeded` into a reason that says what is wrong | S: an integer sum per region, a test on the capsule; bit-identical on clean rows only if the skipped instances are exactly unsatisfiable ones, which the invariant guarantees | only together with option A of the sheaf note (the area ratio as a reported number); not worth a PR of its own |
| 4 | Raise the per-attempt budget or order the rings differently, so that the two `OrientationFolds` torus rows of master are solved by the DPLL | master 2 rows; PR #1293 0 | S | no: PR #1293 covers them |
| 5 | Give the map its area back before the repair: the integer jumps up front (option C of the sheaf note; Heistermann, Warnett and Bommes 2023, from the paper) | the only lever with an upper bound on what is left with PR #1293: 8 `FlippedFaces` and 3 area-obstructed rows, all capsule and capped cylinder | L, a solver project | unchanged from the sheaf note: a hooked prototype on the two shapes first, on the owner's go |

*How others solve it* (search of 2026-11-22: two web searches plus the prior-art list of the sheaf note, section 1; search results and
abstracts only). The published pipelines choose the integers before the map is optimized (Campen, Bommes and Kobbelt 2015; Lyon, Campen and
Kobbelt 2021; Heistermann, Warnett and Bommes 2023) and make local injectivity a constraint of the whole map (Bommes et al. 2013). QuadriFlow
(Huang et al. 2018), which stage 3B follows, is the one that repairs after the fact; its abstract says that most of the remaining
constraints can be satisfied by a local SAT. Nothing was found that certifies a failed local repair or relates it to the area of the map.
Terms searched: "integer grid map local injectivity fold removal local repair SAT unsatisfiable certificate", "QuadriFlow SAT local flip
removal integer offsets limitations".

## 5. Limits

- 54 runs on 9 procedural closed shapes. With PR #1293 the SAT question rests on 3 runs.
- "Best case" is the trajectory of this solver: the model it finds for a satisfiable instance is the first under the repair's
  preference order with learning, not the DPLL's first model, and a later instance depends on the earlier models. Another order could
  solve or obstruct other seeds; the count of fold-free runs is exact for this trajectory only. The area argument does not depend on it.
- An obstructed seed is obstructed at the time it is tried. A later region can still clear it: the capsule at ratio 0.35 with PR #1293 has
  two obstructed seeds and no fold at the end.
- Rings above 6 were not decided (section 3).
- The numbers for PR #1293 are for its head 637990d1.
- The benchmark reads the library by reflection and mirrors `TrySat`; the per-run check against the library's counters holds for the
  library trajectory, and the learning trajectory uses the same instance builder.

## 6. Sources

| Source | Status | Used for |
|---|---|---|
| J. P. Marques-Silva, K. A. Sakallah, *GRASP: A Search Algorithm for Propositional Satisfiability*, IEEE Trans. Computers 48(5), 506-521, 1999 | from the paper | conflict analysis, learned clauses, backjumping |
| N. Eén, N. Sörensson, *An Extensible SAT-solver*, SAT 2003, LNCS 2919 | from the paper; the reference code was not read | trail, reasons, watched literals, activities |
| M. Luby, A. Sinclair, D. Zuckerman, *Optimal speedup of Las Vegas algorithms*, Information Processing Letters 47(4), 173-180, 1993 | from the paper | restart sequence |
| E. Goldberg, Y. Novikov, *Verification of proofs of unsatisfiability for CNF formulas*, DATE 2003 | from the paper | the check of unsatisfiable answers by unit propagation |
| J. Huang, Y. Zhou, M. Niessner, J. R. Shewchuk, L. J. Guibas, *QuadriFlow*, CGF 37(5), 2018 | as in [sources/meshing-and-repair.md](../sources/meshing-and-repair.md); no code read | the repair that is measured |
