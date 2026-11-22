# QuadIntegerJumps

Research spike (2026-11-22), prototype of option C of [QUAD_SHEAF_OBSTRUCTION_2026-11.md](../../docs/research/QUAD_SHEAF_OBSTRUCTION_2026-11.md):
integer jumps on the frozen edges of stage 3B (`QuadRemesher.PureQuads`, `QuadFlowRepair`) before its flow. The note with the numbers,
the stop rule and the options is [QUAD_INTEGER_JUMPS_2026-11.md](../../docs/research/QUAD_INTEGER_JUMPS_2026-11.md). No library change: the
library is read and driven by reflection.

What a run does, per input (the 54 runs of [QuadFlow3BGate](../QuadFlow3BGate/README.md), same shapes and seeds):

1. Stage 2 and the library's pure-quad attempt (`QuadRemesher`).
2. **Replay**: the steps of `QuadFlowRepair.Run` called one by one through reflection, on the same position field. It must give the
   library's status and, on `Success`, the library's mesh bit for bit (`replay mismatches` must be 0; the exit code is 1 otherwise).
3. **Jumps**: the same replay with one step added between the triangles and the flow. The edges and the breadth-first rotation of the
   equations are rebuilt as in `SolveFlow` (so the same edges are frozen); the problem with all edges free is relaxed to a min-cost
   flow on the doubled network (every node and arc with its mirror) and solved with the library's `MinCostFlow` and the library's step
   costs; the frozen edges take the rounded relaxed value (`round=real`: a half goes to the neighbour nearer to the real offset;
   `keep`: towards the start value; `down`); everything else is the library (greedy balancing of what the rounding leaves, flow,
   subdivision, greedy moves, SAT, positions, extraction). The relaxed solution is checked against every equation.
4. Both results are classified with the independent checks of the gate (manifold, quads only, finite, valid, no quad facing away from
   the nearest input triangle), never by a cost.

```
dotnet run -c Release --project benchmarks/QuadIntegerJumps -- [only=<regex>] [var=<regex on "scale/ratio">] [adapt=0|1|2] [align=<x>] [mingain=<x>] [round=real|keep|down]
```

Columns: `area` is the lattice area of the final map (half the sum of the integer determinants) over the geometric area in cells;
`frozen / changed / half (all edges) / clamped / max / moved by the balancing` counts the frozen edges, those whose value the jumps
change, the half-integral components of the relaxed solution on frozen edges (and on all edges), values outside the flow's start range
of -2 to 2, the largest change, and the frozen edges whose value after the library's flow is not the one the jumps wrote; `(sized)`
marks a run where `SizeAdaptationApplied` is true; `cost` is the library's step cost of the offsets after the flow (library, half the cost of the relaxation, jumps); the quality
columns give stage 2 / library / jumps. Times are single runs on a 4-core cloud container; `pure jumps est.` is the library's
`Compute()` time with its repair replaced by the replay with the jumps.

## Results (2026-11-22, master 4137234d, net10.0 Release)

| file | command | library clean | jumps clean | gained / lost | clean with either | undetected bad |
|---|---|---|---|---|---|---|
| `results_adapt0.txt` | (no argument) | 43 / 54 | 52 / 54 | 10 / 1 | 53 / 54 | 0 |
| `results_adapt2.txt` | `adapt=2` | 48 / 54 | 52 / 54 | 5 / 1 | 53 / 54 | 0 |
| `results_align10.txt` | `only='capsule\|capped' align=10` | 3 / 12 | 12 / 12 | 9 / 0 | 12 / 12 | 0 |

`adapt=1` (`Scale`) is not listed: the size field is not applied on the capsule and the capped cylinder, the rows equal those without
an argument. `round=keep` and `round=down` give the same counts as `round=real` on the 54 runs without size adaptation (52 clean, the same row lost).
