# NotCleanBenchmark

Time of the checks behind `MeshRobustRepair.NotCleanReason` on big procedural meshes (no mesh files): a 200k-triangle
sphere, the 185k-triangle alpha wrap of the thin plates of `MeshRobustRepairTests.ThinPlates(6)`, and two shifted copies of
that wrap (20 293 real self-intersecting pairs). Per mesh: the verdict, the number and a hash of the self-intersection
pair set (it must not change between builds), and the median time of the checks.

```
dotnet run -c Release --project benchmarks/NotCleanBenchmark [repeats]
```

Linux container, 4 cores, `net10.0`, Release; the timings vary by about 10-20 % between runs.

## Lever 1: the cheap pair filter in the self-intersection search (2026-11-10)

`MeshRepairMetrics.SelfIntersectionPairs` / `HasSelfIntersections` called the exact `IntersectsBeyondShared` for every
pair of triangles whose boxes overlap. They now call `Remesher.pair_intersects`, which first runs `TriangleTriangleFilter`
(decides pairs that a projection onto an axis plane settles with `Orient2d` only) and, for neighbours sharing one vertex,
an exact plane test, and falls back to the same exact predicate. It rejects only where the exact predicate says no:
`SelfIntersectionPairsTests` compares the pair sets with a brute force over all pairs (procedural cases with coplanar,
touching and shared-edge pairs, lattice soups with zero-area triangles, similarities) and
`SelfIntersectionPairsProperty` (CsCheck) does so on random lattice soups.

| mesh | verdict | pairs (hash, before = after) | all-pairs search ms before / after | HasSelfIntersections ms before / after | NotCleanReason ms before / after |
|---|---|---|---|---|---|
| sphere, 200k triangles | "" | 0 (`DF3F619804A9`) | 264 / 178 | 273 / 164 | 326 / 203 |
| alpha wrap, 185k triangles | "" | 0 (`DF3F619804A9`) | 405 / 283 | 400 / 287 | 536 / 443 |
| two shifted wraps | "self-intersections" | 20293 (`39ED0B0AC51D`) | 1060 / 813 | 181 / 191 | 487 / 469 |

The other checks of `NotCleanReason` (closed, bowties, degenerate triangles, orientation, inside-out components) take
130-340 ms on the wrap meshes, about a quarter of the total.

## Lever 2: the box-tree self-join instead of one tree traversal per triangle (2026-11-10)

The candidate pairs now come from the self-join of the box tree of the exact boolean (`TriangleBoxTree.SelfPairTasks` /
`SelfPairs`, closed box overlap, 256 work items in parallel) over the non-degenerate triangles, instead of one traversal of
a `DMeshAABBTree3` per triangle. Every intersecting pair has overlapping closed boxes, so none is missed; the exact test
(lever 1) decides. Each work item fills its own list and the pairs are sorted at the end, so the result does not depend on
the number of threads (`Pairs_ParallelEqualsSerial_AndRepeatedRunsAreIdentical`: repeated runs, `gParallel.SerialOnThisThread`,
and the brute force). With `HasSelfIntersections` (stop at the first pair) the pair found first depends on the thread
timing, as before; only its existence is used. The pair counts and hashes are the same as before and after lever 1. The candidates are streamed to the exact test (`TriangleBoxTree.SelfPairs` with a visit callback), not listed per work item: a fan of 2000 triangles, where every box overlaps every other, needs no list of box pairs, and `HasSelfIntersections` stops its work item at the first pair (`DenseFan_...` test; found by review).

| mesh | all-pairs search ms: master / lever 1 / lever 2 | HasSelfIntersections ms: master / lever 1 / lever 2 | NotCleanReason ms: master / lever 1 / lever 2 |
|---|---|---|---|
| sphere, 200k triangles | 267 / 204 / 135 | 297 / 166 / 119 | 319 / 221 / 148 |
| alpha wrap, 185k triangles | 420 / 287 / 170 | 439 / 295 / 152 | 541 / 417 / 319 |
| two shifted wraps (20 293 pairs) | 1080 / 849 / 438 | 183 / 186 / 200 | 481 / 488 / 512 |

(Median of 9 runs on a quiet machine, 4 cores; on the last row `NotCleanReason` stops at the first pair, and the time is dominated by the other checks.)

## Lever 3: bowtie and degenerate-triangle checks over blocks of ids in parallel (2026-11-10)

`NotCleanReason` / `FindDefects` ran `MeshIterators.BowtieVertices(mesh).Any()` and the degenerate-area test one after the
other on one thread. Both are reads only and their answer is a bool, so they now run over blocks of 8192 ids in parallel
(meshes with up to 16384 ids stay serial) and stop once a block finds one. The checks themselves are unchanged, so the
verdicts are too (`NotCleanParallelChecksTests`: a bowtie or a zero-area triangle in the first, middle and last block, id
gaps from removed triangles; a mutant that skips the last block fails 3 tests).

| mesh | closed + bowtie + degenerate + orientation + inverted ms: lever 2 / lever 3 | NotCleanReason ms: lever 2 / lever 3 |
|---|---|---|
| sphere, 200k triangles | 65 / 36 | 148 / 144 |
| alpha wrap, 185k triangles | 130 / 93 | 319 / 240 |
| two shifted wraps | 308 / 224 | 512 / 446 |

What is left of `NotCleanReason` on the clean wrap (240 ms): the self-intersection search 155, `FindInvertedComponents` 48,
the rest of the cheap checks about 40.
