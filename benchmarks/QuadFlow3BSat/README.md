# QuadFlow3BSat

Research spike, a measurement and not a gate: [docs/research/QUAD_3B_CLAUSE_LEARNING_2026-11.md](../../docs/research/QUAD_3B_CLAUSE_LEARNING_2026-11.md)
(the question, the reading of the numbers, the recommendation). It asks how many fallbacks of `QuadRemesher.PureQuads` are caused by the
search of the local repair (`GroupSatSolver`, a DPLL without learning under a budget of 300k decisions) and how many by the problem (local
instances that have no solution). The inputs are the 54 runs of [QuadFlow3BGate](../QuadFlow3BGate/README.md) (9 procedural shapes x 6
variations; the construction is copied here).

```
dotnet run -c Release --project benchmarks/QuadFlow3BSat -- [only=<regex>] [dump=<dir>] [maxring=<n>] [conflicts=<limit>] [crosscheck=<decisions>] [rup=0|1] [list=0|1]
dotnet run -c Release --project benchmarks/QuadFlow3BSat -- selftest=1
```

No library file is changed. The repair is read by reflection (`QuadRemesher.PureQuadRepair`, the private `tris` of `QuadFlowRepair`, the
counters, `GroupSatSolver`); a renamed member ends the run with a `MissingFieldException` or `MissingMethodException`.

Per fallback run:

1. **Which budget ended it**: splits (`Splits > MaxSplits`), SAT decisions (`SatDecisions >= SatTotalBudget`), SAT attempts, the round
   limit, none (every seed failed inside the budgets: `OrientationFolds`), or "not the repair" (another status, eg `FlippedFaces`).
2. **The map after the greedy moves**: a second `QuadFlowRepair` on the same position field with `SatTotalBudget = 0`, which makes
   `RepairSat` change nothing.
3. **Library trajectory**: the loop of `QuadFlowRepair.RepairSat` / `TrySat` rebuilt here (the same regions, variables, group order and
   clauses) with the library's own `GroupSatSolver` and budgets. The column "replay = library" checks that attempts, solved seeds, failed
   seeds, decisions and the final fold count equal the library's; a mismatch gives exit code 1. Every instance the DPLL leaves at its budget
   is decided by the clause-learning solver.
4. **Learning trajectory**: the same loop with the clause-learning solver and no budget (no decision, attempt or round limit; `conflicts=`
   sets one, default none). A seed whose instances of all rings (1 to `SatMaxRing` = 6, or `maxring=`) are unsatisfiable is an
   **obstructed seed**. The summary counts, for the region of the largest ring: singular vertices, folds, and the sum of the integer
   determinants (twice the lattice area of the region). `list=1` (default) prints one row per obstructed seed.
5. **Completion**: where no fold is left, the library's own remaining steps (`MapCloses`, `SolvePositions`, `Extract`) run on the repaired
   map and a `Success` gets the independent checks of the gate (manifold, quads only, finite, valid, no quad facing away from the input).

`dump=<dir>` writes DIMACS files, one directory per run: `budget_a<attempt>_seed<t>_r<ring>_<verdict>.cnf` for the instances of step 3
and `obstruction_seed<t>_r<ring>_UNSAT.cnf` for every ring of an obstructed seed. The header comments give the seed, the ring, the master
triangle and slot of every edge and its preference order; variable `9 e + k + 1` says that edge `e` has the offset
`(k % 3 - 1, k / 3 - 1)`. The files are not committed (38 MB for master); the command regenerates them bit for bit.

**The solver** (`CdclSolver.cs`, about 250 lines, throwaway): conflict analysis at the first unique implication point, learned clauses,
backjumping, two watched literals, an activity per variable with ties broken by index, Luby restarts counted in conflicts, no clause
deletion. The decision takes the group of the most active unassigned variable and sets its first value that is not false, so the
preference order of the repair is the phase. Written from the papers (Marques-Silva and Sakallah 1999; Eén and Sörensson 2003; Luby,
Sinclair and Zuckerman 1993); no solver code was read or run.

**Checks of every answer** (a failure gives exit code 1): a model is checked against the clauses and, after it is applied, on the map
(every triangle of the region closes and has det >= 0); an unsatisfiable answer is checked by reverse unit propagation of the learned
clauses in order (`RupChecker`, Goldberg and Novikov 2003; it shares no code with the solver); and both are compared with the library's DPLL
where that decides within `crosscheck` decisions (default 20000). `selftest=1` runs 4000 random formulas against the DPLL without a budget
and checks that the proof checker refuses a refutation of a satisfiable formula.

Effort is counted in conflicts and decisions; nothing is timed.

## Results (2026-11-22, net10.0 Release)

`results_master.txt`: master e09cf478 (`list=0`). `results_pr1293.txt`: the same benchmark built against the head of PR #1293 (637990d1).

| | master e09cf478 | PR #1293 head |
|---|---|---|
| clean / fallback (54 runs) | 25 / 29 | 43 / 11 |
| fallbacks ended by: SAT decisions / none (every seed failed) / splits / not the repair | 26 / 3 / 0 / 0 | 3 / 0 / 0 / 8 (`FlippedFaces`) |
| replay equals the library | 29 / 29 | 3 / 3 |
| instances the DPLL left at its budget: satisfiable / unsatisfiable / undecided | 13 / 66 / 0 | 0 / 8 / 0 |
| learning trajectory, instances: satisfiable / unsatisfiable / undecided | 499 / 8905 / 0 | 6 / 242 / 0 |
| seeds solved / obstructed | 499 / 1190 | 6 / 37 |
| **fallbacks fold-free in the best case** | **3 / 29** (torus at scale 0.8, 1.25, 1.5) | **1 / 11** (capsule at ratio 0.35) |
| of those, `Success` with the independent checks | 3 | 0 (`FlippedFaces`) |
| conflicts, all instances / most on one | 3.11M / 93002 | PR_CONFLICTS |
| unsatisfiable answers with a valid RUP proof / invalid | 5082 / 0 | PR_RUP |
| against the DPLL: agree / disagree / DPLL undecided | 4315 / 0 / 1279 | PR_CROSS |
| obstructed seeds whose largest region has a negative / zero / positive sum of det | 1057 / 58 / 75 | PR_AREA |

(The unsatisfiable instances include those refuted while they were built: a triangle with one mutable edge and no value for it.)
