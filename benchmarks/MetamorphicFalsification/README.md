# MetamorphicFalsification

Falsification run of [docs/research/METAMORPHIC_TESTING.md](../../docs/research/METAMORPHIC_TESTING.md): which metamorphic
relations expose the known bugs of this repository? Each case is the procedural repro of a closed bug issue (no mesh files, no
network). A relation is applied to it twice: on the library at the **parent of the fixing commit** (the bug present) and on
`master` (the control). A relation counts as having caught the bug only if it fires on the parent and not on the control;
a row that fires on both is a tie-break or range effect, not the bug.

Relations per case (`Program.cs`): `scale 2^k` (exact), `symmetry` (a subset of the 48 axis permutations with signs, reflections
re-wound), `permutation` (vertex / triangle order and corner rotation), a cost-spread rule (the times of one group agree within
5x, after a warm-up run), and per-case extras (`tiny coordinate`, `anisotropic`). The comparison is an order-independent set of
triangles mapped back, bit for bit, so a symmetry row may fire for a legitimate axis-dependent triangulation; the control run
tells.

```
dotnet run --project benchmarks/MetamorphicFalsification -c Release            # all cases on the checked-out library
dotnet run --project benchmarks/MetamorphicFalsification -c Release -- 719     # one issue
# on a parent: git worktree add --detach <dir> <parent-sha>; copy this folder into <dir>/benchmarks/; run there
```

The harness uses only API that exists at the parents (`MeshMeshCut`, `PointAABBTree3`, `ConvexHull2`, `MeshRobustRepair`,
`ExactMeshBoolean`). Output of the run described in the note: `results/parents.txt` and `results/control_master.txt`
Symmetries per case are a fixed subset of the 48 (`Syms().Take(n)` in `Cases.cs`; #856 uses 18). The hull epsilon of #644 scales with the input (`1e-9 * Scale`), the ring is compared as an index set. The signature normalises `-0` to `0` (a sign flip turns +0 into -0). #856 does not set `RefuseContact`, so master still reproduces it.
(parents: #867 51270ea, #809 2c2e300, ConvexHull2 e1ff201, #703 cbf8b78, #759 c677da7, #732 f5f4985).

`diagnose` mode (`-- diagnose`, output `results/diagnose_master.txt`): the relations that fire on master (#856, #710, #712) against the documentation, with in-range variants of the inputs (note section 3.1).
