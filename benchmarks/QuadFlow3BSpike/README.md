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
