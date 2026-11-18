# QuadFlow3BSpike (throwaway, data branch runs/quad-3b, never merged)

Second stage 3B upper-bound measurement (docs/design/QUAD_REMESHING.md 4.3): the QuadriFlow position-field repair as summarized
from Huang et al. 2018 (sections 3.2-3.6), implemented from that description and Ahuja-Magnanti-Orlin (successive shortest paths
with potentials) and an own DPLL. No QuadriFlow, Instant Meshes, LEMON or MapleSAT code read. No library change.

`dotnet run -c Release --project benchmarks/QuadFlow3BSpike -- [only=<regex>] [greedy=1] [sat=1] [strict=0] [satring=6] [satbudget=100000] [smartsplit=0]`
`dotnet run ... -- dpllselftest` checks the DPLL against brute force (3000 instances).

Files: `Flow.cs` (steps 1-10), `Mcf.cs` (min-cost flow), `Dpll.cs` (SAT), `Program.cs` (inputs, report). Results: `results_*.txt`.

Representation: triangle soup with one lattice frame per triangle (frame of its first vertex) and a frame transition per edge
(D_neighbour = -R^m D, m from the per-edge orientation compatibility). The regularity constraint is then "sum of the three D = 0".
Equation rotation by a BFS over the triangles balances the tree edges and every non-tree edge whose cycle has no holonomy.

Round 2 (coordinator request): `cost=1` geometry-aware flow cost (marginal |d - d_real| tiers, +1 per unit), `h=<n>` start capacity,
`cancel=1` extraction drops pairs of triangles with the same three clusters (a 2-gon), `rounds=<n> pen=<x>` re-solves the flow with
the edges of leftover inverted triangles penalized. Results: `results_round2_*.txt`.

Round 3 (diagnosis of the sphere inversions): `SPIKE_DBG2=1` prints the relation of the leftover inversions to fixed, flow-changed
and frame-jump edges and, per inverted component, the winding number of the boundary image of growing disks (`Diag.cs`; a
negative winding inside a cone-free disk proves that no det >= 0 assignment exists with that boundary). `cost=2 signpen=<x>
signtol=<r>` multiplies the step cost by `signpen` when a step grows |d| against the sign of the real offset (|r| > signtol).
Results: `results_round3_diag.txt`, `results_round3_signcost.txt`.

Round 4 (gate table and fallback rate, coordinator request): `gate=1` prints one table row per shape with stage 2 and 3B side by
side and a class: clean, FALLBACK (exact `det < 0` count > 0 or flow infeasible), UNDETECTED-BAD (not manifold, < 100 % quads,
unpaired or flipped quads without a detected fold). `proj=1` projects the extracted vertices onto the input (without it the sphere's
Hausdorff is 1.1 s). Six more procedural shapes (bumpy, filleted box, noisy sphere, capsule, genus 2, capped cylinder), the same
six variations (scale 1, 0.8, 1.25, 1.5; ratio 0.35, 0.7). `cost=3` is a hard sign constraint (too slow to measure). Results:
`results_round4_gate.txt`. Option A over 54 runs: 23 clean, 31 fallback, **0 undetected bad**. Clean rows: irregular gate 23/23,
Hausdorff 23/23, corner p95 <= 15 18/23, time <= 2x stage 2 0/23 (median 10x). Library brief: `LIBRARY_BRIEF.md`.
