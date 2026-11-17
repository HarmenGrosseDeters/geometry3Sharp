# QuadRemeshSpike

A throwaway feasibility prototype for quad remeshing ([QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md),
section 3). It is **not** library code and has no tests. It shows what a first field-aligned quad pipeline built
from the library's existing blocks delivers on procedural shapes. Once a design is chosen, the stages are rebuilt
in the library with tests. This code is not promoted as it is.

Pipeline per shape:
1. Input: implicit shape, then `MarchingCubes`, then `Remesher` (20 passes, isotropic edge `h = ratio * s`, projected onto the MC mesh).
2. Cross field: Knöppel, Crane, Pinkall, Schröder 2013. The smoothest 4-RoSy field is stored per vertex and built
   with cotan weights, where negative cotans are clamped to 1e-3. It is the smallest eigenvector of the connection
   Laplacian, found by 30 inverse iterations with `SparseCholesky` on the 2n x 2n real form. The singular faces and
   their total index are counted, and the total must equal the Euler characteristic.
3. Position field: Jakob, Tarini, Panozzo, Sorkine-Hornung 2015 (Instant Meshes), implemented from the paper:
   extrinsic 4-RoSy compatibility, the middle point of two tangent planes, and lattice rounding. Gauss-Seidel
   smoothing runs on a greedy pair-merging hierarchy, with `sweeps` sweeps per level.
4. Extraction (simplified): edges with zero lattice offset are merged by union-find, and unit-offset edges become
   graph edges. Dangling vertices are removed. Faces are traced in each vertex's tangent plane. There is no cleanup.
5. Baseline: greedy triangle pairing of an isotropic remesh at edge `s` (quad-dominant, Blossom-Quad-like but greedy).

```
dotnet run -c Release --project benchmarks/QuadRemeshSpike -- [only=<regex>] [ratio=0.5] [sweeps=40] [scale=1]
```

Output: one Markdown row per shape and method. The columns:
- face counts by number of sides; faces with more than 6 sides are counted as extraction defects (holes);
- irregular vertices: valence other than 4;
- irregular vertices after one Catmull-Clark step: the irregular vertices plus one per non-quad face;
- quad corner angle deviation from 90 degrees (p50 / p95), and the share of corners within 70-110 degrees;
- quad aspect (longest / shortest edge);
- symmetric `MeshHausdorffDistance` from the fan-triangulated output (faces up to 6 sides) to a remesh at half the
  input edge, divided by the bounding box diagonal;
- times in seconds for the field (frames, assembly, factorization, iterations), the position field and the
  extraction. The baseline row's time includes its remesh.

The program is single-threaded and deterministic. No mesh files are read or written. The results are in
QUAD_REMESHING.md section 3.
