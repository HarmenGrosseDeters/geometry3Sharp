# Batch ray and point queries: prior art, upper bound, API proposal (ROADMAP Phase 5 priority 1, stage 1)

Status: stage 1 done (2026-11-08); owner decision the same day: see section 0. No library code changed. The benchmark prototype is
[benchmarks/BatchQueryBenchmark](../../benchmarks/BatchQueryBenchmark/README.md) (raw results there).

## 0. Decisions (owner, 2026-11-08)

- Stage 2 is option 1 of section 4: **b1 only**, a new static class `MeshBatchQueries` in `spatial/MeshBatchQueries.cs`
  over the public tree API. No own BVH, no SIMD.
- The first PR offers **nearest hit and any-hit**; all-hits come later.
- Addendum (owner, 2026-11-08): stage 2 also gets a batched nearest-point query, `NearestPointBatch` (section 4), same b1 pattern, bit-identical to the single call. Internal callers are not switched in stage 2; a later profile decides where that pays.
- The library PR starts after the exact-boolean merge queue and the Lévy phase-2 PR have merged (it must not touch
  `DMeshAABBTree*.cs` before). The CLI commands (`raycast`, `inside`, section 5) go into the later CLI / MCP update.

## 0a. As built in stage 2 (2026-11-09)

`spatial/MeshBatchQueries.cs`: `IntersectBatch`, `AnyHitBatch`, `NearestPointBatch` and (owner, 2026-11-09, on request after the first version) `IsInsideBatch` over `DMeshAABBTree3`, with `RayHit`,
`NearestPointResult`, `BatchOptions`, `BatchStatus`, `RayStatus`, `PointStatus`. Differences from the proposal in section 4:
- the per-point status is `PointStatus` (`Ok`, `NonFinite`) for `NearestPointBatch`; `IsInsideBatch` returns `InsideStatus` (`Outside`, `Inside`, `Invalid`) per point, with `InsideMethod.Crossings` (= `tree.IsInside`) or `WindingNumber` (= `tree.FastWindingNumber(p) > 0.5`); `MaxDistance` does not apply to it; the winding cache is built once before the parallel part;
- arrays only; `Span` overloads (owner, 2026-11-09: later) and offset / count overloads are not there yet (a caller passes exactly the arrays it wants answered; buffers may be longer);
- `BatchOptions.MaxDegreeOfParallelism` is not offered (the shared `gParallel.ForRange` has none); `Parallel = false` runs on the calling thread;
- `AnyHitBatch` runs the nearest-hit query and tests for a hit: no early-out, since that needs the tree's internals;
- `T` of a hit is recomputed with `IntrRay3Triangle3.Intersects` on the found triangle (the value the tree compared), the nearest-point `Point` / `Bary` with `DistPoint3Triangle3.DistanceSqr`, so the batch allocates nothing per query (only the key and order arrays when sorting, for 1024 queries or more);
- measured with the real API in [the benchmark README](../../benchmarks/BatchQueryBenchmark/README.md): the same 3-5x for sorted + parallel on 4 cores, serial sorted 1.0-1.6x, every result bit-identical to the loop.
Finding on the way: `FindNearestTriangle`'s choice among equidistant triangles (a point on a mesh vertex or edge) is not invariant under 2^k scaling (the absolute box padding changes the traversal order); distances are. The batch inherits this from the single call; the invariance test compares distances for those ties.

## 1. Result in short

- Batching **over the existing `DMeshAABBTree3`** (b1: Morton-sorted queries, the same single-call query per ray, results
  written by index, parallel with `gParallel`) gives, on a 4-core container, **about 3-5x** the throughput of a loop of
  single calls (rays and `IsInside`); sorting alone gives 1.0-1.7x (more for incoherent input order and large meshes);
  parallel scaling is about the core count. Results are identical to the single calls (checked bit for bit, every row).
- A **separate flat BVH** (b2) adds little on top of that: serial 0.98-1.7x versus the loop, about the same as b1 sorted.
  Its extra gain over b1 is at most about 1.3x serial (rays on 1M triangles: 1.07-1.71 vs 1.10-1.74). **Not worth a second tree.**
- The **SIMD filter** (Vector256, 4 triangles of a leaf) gains 1.1-1.3x over the scalar filter of b2, serial. The per-ray
  time is dominated by traversal, memory and the exact fallback on hit triangles, not by the filter. It would need a
  second code path (net10.0 only). **Not worth it now**; it is also tied to b2's leaf layout, which the existing tree does not have.
- `FastWindingNumber`, parallel over sorted points, gives 3.3-5.3x over the loop and is the only batch inside test that is
  also defined for open meshes.
- Nearest point in the same pattern: sorted + parallel 3.7-7.2x, bit-identical (section 3).
- Recommendation: stage 2 = **b1 only** (new static class, new file, no new tree, no SIMD). It is small, exact by
  construction, allocation-free, and does not touch `DMeshAABBTree3.cs`.

## 2. Prior art ("how others solve it")

Searched for: ray packets / stream traversal, wide BVH, ray sorting, watertight ray-triangle, batched winding numbers.
Papers only; no Embree, CGAL or GEOS code read. Citation data re-checked on 2026-11-08 against the publishers' and authors' pages (title, venue, volume, DOI); the full texts were not read for this note, the relevance column is from the abstracts and the earlier reading of the techniques. Nothing is ported; the papers are listed in [SOURCES.md](../SOURCES.md) as design references.

| Topic | Source | Relevance |
|---|---|---|
| Ray packets: trace several coherent rays together, share box tests and cache lines | I. Wald, P. Slusallek, C. Benthin, M. Wagner, *Interactive Rendering with Coherent Ray Tracing*, Computer Graphics Forum 20(3), 153-165, 2001 (Eurographics 2001), doi:10.1111/1467-8659.00508; Reshetov, Soupikov, Hurley, *Multi-level ray tracing algorithm*, ACM TOG 24(3), SIGGRAPH 2005, doi:10.1145/1073204.1073329 (Intel holds a patent on the method, US 7,755,628, listed as expired; not implemented here) | Needs coherent rays and a SIMD-friendly BVH; for incoherent rays packets degrade. Gains are for camera rays. |
| Wide BVH (4-8 children) tested with SIMD per node, ray streams | Dammertz, Hanika, Keller, *Shallow Bounding Volume Hierarchies for Fast SIMD Ray Tracing of Incoherent Rays*, Computer Graphics Forum 27(4), 1225-1233, 2008 (EGSR 2008), doi:10.1111/j.1467-8659.2008.01261.x; Wald, Woop, Benthin, Johnson, Ernst, *Embree: A kernel framework for efficient CPU ray tracing*, ACM TOG 33(4), art. 143, 2014, doi:10.1145/2601097.2601199 (Apache-2.0 code, not read) | The incoherent case is handled by a wide tree, not by packets: a second tree layout, i.e. our b2 and larger. |
| Ray sorting for coherence | Garanzha, Loop, *Fast ray sorting and breadth-first packet traversal for GPU ray tracing*, Computer Graphics Forum 29(2), 289-298, 2010, doi:10.1111/j.1467-8659.2009.01598.x; Meister, Ogaki, Benthin, Doyle, Guthe, Bittner, *A survey on bounding volume hierarchies for ray tracing*, Computer Graphics Forum 40(2), 683-712, 2021, doi:10.1111/cgf.142662 | The Morton key of origin and direction, as in b1. Sorting pays when the input order is incoherent. |
| Watertight ray-triangle test | Woop, Benthin, Wald, *Watertight ray/triangle intersection*, Journal of Computer Graphics Techniques 2(1), 65-82, 2013 | The library's `ExactRayTriangle` is the exact (Plücker / `Orient3d`) variant of the same idea. |
| Batched inside test by winding number | Barill, Dickson, Schmidt, Levin, Jacobson, *Fast winding numbers for soups and clouds*, ACM TOG 37(4), art. 43, SIGGRAPH 2018, doi:10.1145/3197517.3201337 | The library already has `FastWindingNumber` (Barill); batching is queries in sorted order, in parallel. |

What the library already has: `DMeshAABBTree3.FindNearestHitTriangle` (exact, ties to the lowest id, recursive: no
stack to reuse, no allocation), `FindAllHitTriangles`, `IsInside` (exact crossing parity with symbolic perturbation,
closed meshes), `WindingNumber` / `FastWindingNumber` (thread-safe, cache built on first use under a lock),
`ExactRayTriangle` (internal), `gParallel.BlockStartEnd`. Rendering libraries target visibility of camera rays on
float data; here exactness (bit-identical to the single query) is a requirement, which favours reusing the single-query
code over a new traversal.

## 3. Upper bound (measured)

Command: `dotnet run --project benchmarks/BatchQueryBenchmark -c Release -- 256 40`, container with 4 cores, one run,
median of 3 rounds, about ±20 % noise. Speedup over (a) the loop of single calls in input order; ns per query in the
fourth column. Workloads: camera (pinhole, raster order = coherent), camera-shuffled (the same rays in random order),
random (random origins and targets); points: grid (raster order), grid-shuffled, random. Sphere meshes with 10k, 100k, 1M triangles.

| triangles | query | workload | (a) loop ns | b1 sorted | b1 sorted + parallel | b2 sorted | b2 sorted + SIMD | b2 sorted + SIMD + parallel |
|---:|---|---|---:|---:|---:|---:|---:|---:|
| 10092 | FindNearestHit | camera | 1321 | 0.89x | 2.21x | 1.47x | 1.56x | 3.97x |
| 10092 | FindNearestHit | camera-shuffled | 1896 | 1.28x | 3.76x | 1.42x | 1.56x | 5.10x |
| 10092 | FindNearestHit | random | 2922 | 1.01x | 3.19x | 1.25x | 1.29x | 4.10x |
| 10092 | IsInside | grid | 2479 | 1.12x | 3.56x | 1.48x | 1.70x | 5.61x |
| 10092 | FastWindingNumber>0.5 | grid | 2732 | parallel: 3.3x | | | | |
| 10092 | IsInside | grid-shuffled | 2674 | 1.11x | 3.66x | 1.39x | 1.76x | 5.14x |
| 10092 | FastWindingNumber>0.5 | grid-shuffled | 3293 | parallel: 3.9x | | | | |
| 10092 | IsInside | random | 2740 | 1.20x | 3.94x | 1.66x | 1.74x | 4.55x |
| 10092 | FastWindingNumber>0.5 | random | 3937 | parallel: 4.6x | | | | |
| 99372 | FindNearestHit | camera | 1848 | 1.06x | 3.05x | 1.30x | 1.50x | 4.55x |
| 99372 | FindNearestHit | camera-shuffled | 2580 | 1.39x | 4.18x | 1.48x | 1.90x | 5.52x |
| 99372 | FindNearestHit | random | 4588 | 1.23x | 3.81x | 0.98x | 1.20x | 4.29x |
| 99372 | IsInside | grid | 3160 | 1.09x | 3.55x | 1.19x | 1.38x | 4.57x |
| 99372 | FastWindingNumber>0.5 | grid | 3079 | parallel: 3.4x | | | | |
| 99372 | IsInside | grid-shuffled | 3861 | 1.29x | 4.37x | 1.36x | 1.48x | 5.35x |
| 99372 | FastWindingNumber>0.5 | grid-shuffled | 4551 | parallel: 4.5x | | | | |
| 99372 | IsInside | random | 3823 | 1.29x | 3.90x | 1.38x | 1.58x | 5.05x |
| 99372 | FastWindingNumber>0.5 | random | 4468 | parallel: 5.0x | | | | |
| 1002252 | FindNearestHit | camera | 2366 | 1.10x | 3.40x | 1.07x | 1.25x | 3.77x |
| 1002252 | FindNearestHit | camera-shuffled | 4314 | 1.74x | 5.18x | 1.71x | 2.01x | 5.74x |
| 1002252 | FindNearestHit | random | 8429 | 1.11x | 3.79x | 1.16x | 1.24x | 4.46x |
| 1002252 | IsInside | grid | 6413 | 1.25x | 4.45x | 1.19x | 1.44x | 4.98x |
| 1002252 | FastWindingNumber>0.5 | grid | 3677 | parallel: 3.5x | | | | |
| 1002252 | IsInside | grid-shuffled | 7210 | 1.40x | 5.10x | 1.31x | 1.55x | 5.43x |
| 1002252 | FastWindingNumber>0.5 | grid-shuffled | 6642 | parallel: 5.1x | | | | |
| 1002252 | IsInside | random | 7105 | 1.43x | 4.98x | 1.42x | 1.59x | 5.04x |
| 1002252 | FastWindingNumber>0.5 | random | 6146 | parallel: 5.3x | | | | |

Nearest point (`NearestPointBatch` pattern: `FindNearestTriangle` + `TriangleDistance` per point, unbounded `MaxDistance`; points in the cube [-1.2, 1.2]^3 around the sphere; `-- 256 40 30,92,290 nearest`, `results-nearest-2026-11-08.csv`), speedup over the loop, same conditions:

| triangles | grid (coherent) sorted | grid sorted + parallel | shuffled / random sorted | shuffled / random sorted + parallel |
|---:|---:|---:|---:|---:|
| 10k | 1.01x | 3.69x | 1.40x / 1.26x | 4.56x / 4.27x |
| 100k | 1.05x | 3.67x | 1.43x / 1.48x | 4.58x / 5.15x |
| 1M | 1.12x | 3.85x | 2.03x / 2.02x | 7.18x / 6.92x |

(ns per query in the loop: 6.2 us at 10k, 20-29 us at 100k, 78-137 us at 1M: the queries are far from the surface and memory bound; at 1M the 7x on 4 cores is sort and cache effect together, one run, not a scaling claim.) All results equal the loop bit for bit (triangle, point, distance).

Reading (rays and `IsInside`):
- b1 sorted (serial) alone: 0.89-1.74x. It loses a little (sorting cost, about 90-210 ns per query at 65k rays) on an
  already coherent input and on small meshes, and wins for shuffled input on large meshes.
- b1 + parallel: 2.2-5.2x for rays, 3.6-5.1x for `IsInside`. Above 4x on 4 cores is the sort effect plus noise.
- b2 (own BVH) serial: 0.98-1.71x; SIMD adds 1.05-1.3x of that; parallel adds the core count. The sum is only 0-30 % better than b1 + parallel.
- Why so little from the filter: the traversal (box tests, cache misses) and the exact fallback on candidate
  triangles (the public `Intersects` also recomputes the second point) dominate; the filter work per ray is a minor share. An upper bound for removing the filter completely is therefore small.
- Per-ray cost grows with the mesh (random rays on 1M triangles: 8.4 us in the loop), so memory is the limit; sorting helps there.

netstandard2.0: no `Vector256` (no hardware intrinsics), so the SIMD filter would be a second, net10.0-only code
path (`#if NET`), about 150 lines plus the equivalence tests (SIMD equals scalar, no FMA, same bound as `Orient3dFilter` including its underflow terms; the bound needs the review of exact code). netstandard2.0 keeps the scalar path. Cost: the second path and its review for 1.1-1.3x serial; not recommended now.
`Span<T>` is not in netstandard2.0 as the package has no System.Memory reference (the library uses `Span` only under `#if NET`), so the primary overloads take arrays.

## 4. API proposal (for the owner's decision)

New file `spatial/MeshBatchQueries.cs`, a static class over the public tree API; nothing in `DMeshAABBTree*.cs`.
The existing API is unchanged.

```csharp
public struct RayHit { public int Triangle; public double T; }          // Triangle = DMesh3.InvalidID: no hit (T = double.MaxValue)

public enum BatchStatus { Ok, InvalidArguments, MeshModified, BufferTooSmall, Cancelled }
public enum RayStatus : byte { Ok, NotNormalized, NonFinite }            // per ray; the single call throws, a batch must not die for one ray
public enum InsideStatus : byte { Outside, Inside, Invalid }              // Invalid: non-finite point

public class BatchOptions {
    public bool Parallel = true;               // gParallel; the result does not depend on it
    public int MaxDegreeOfParallelism = -1;
    public bool SortForCoherence = true;       // Morton order of the queries; results always in input order
    public double MaxDistance = double.MaxValue;   // as fMaxDist of FindNearestHitTriangle
    public System.Threading.CancellationToken Cancel;
}
public enum InsideMethod { Crossings, WindingNumber }   // Crossings = IsInside (exact, closed meshes); WindingNumber = FastWindingNumber(p) > 0.5

public static class MeshBatchQueries {
    public static BatchStatus IntersectBatch(DMeshAABBTree3 tree, Ray3d[] rays, RayHit[] hits, RayStatus[] status = null, BatchOptions options = null);   // arrays of rays.Length, offset / count overload
    public static BatchStatus IsInsideBatch(DMeshAABBTree3 tree, Vector3d[] points, InsideStatus[] results, InsideMethod method = InsideMethod.Crossings, BatchOptions options = null);
    // #if NET: the same with ReadOnlySpan<Ray3d> / Span<RayHit> (netstandard2.0 has no Span without a new package reference)
}
```

Nearest point (owner addendum), same class, same options (`MaxDistance` as `fMaxDist`: strictly closer than it, else no hit):

```csharp
public struct NearestPointResult { public int Triangle; public Vector3d Point; public double Distance; public Vector3d Bary; }   // Triangle = InvalidID: none within MaxDistance (Distance = double.MaxValue)
public static BatchStatus NearestPointBatch(DMeshAABBTree3 tree, Vector3d[] points, NearestPointResult[] results, InsideStatus[] status = null, BatchOptions options = null);
```
Per point: `t = tree.FindNearestTriangle(p, out dSqr, MaxDistance)`; `Point` = `MeshQueries.TriangleDistance(mesh, t, p).TriangleClosest`;
`Distance` = `Math.Sqrt(dSqr)` as the single-call composition computes it (the batch calls the same two functions, so it is bit-identical;
the test compares the triangle id, the point and the distance bit for bit with the loop). Non-finite points get `Invalid` and no result. A mesh with no
triangles gives `InvalidID` for every point. Ties between equally distant triangles go to the single call's choice (traversal-order dependent in the existing
`FindNearestTriangle`, but deterministic per tree; the batch does not change the tree or the per-query traversal, so it gives the same one).

Exactness semantics:
- Each result equals the result of the single call for the same tree: `hits[i]` = `FindNearestHitTriangle(rays[i], MaxDistance)` (smallest t, ties to the lowest id) and `T` = the single query's t; `IsInside` equals `tree.IsInside(p)`; the winding method equals `tree.FastWindingNumber(p) > 0.5`. The batch runs the same code per query: batching only reorders independent queries and writes by index. The min-t / lowest-id rule does not depend on the order of the triangles visited, so sorting cannot change a result.
- Tests in stage 2: batch equals loop for all `Similarity.All()` (invariance), serial equals parallel for random thread counts and repeats (determinism), sorted equals unsorted, a reuse test, `ReaderFuzz` not affected (no reader). An `T` check compares bit for bit.
- Thread safety: the tree is only read; `TriangleFilterF` must be thread-safe; the mesh must not change during the call (checked once at the start: `MeshModified`, the single calls throw). The fast winding cache is built once, before the parallel loop (it is built under a lock anyway).
- Errors do not throw per ray: invalid rays get a `RayStatus` and a no-hit result.
- Allocation: results and statuses are caller arrays; per-batch the key and order arrays (n ints and n longs); no per-query allocation.
- `IsInside` with `Crossings` on an open or non-manifold mesh has no meaning (as for the single call); `WindingNumber` is the general choice. No automatic switching in the library (the CLI does, below).

Options for the owner:
1. **b1 only (recommended; chosen by the owner, section 0)**: about 3-5x on 4 cores, exact by construction, about 300 lines plus tests, M, no change to the tree.
2. b1 now, SIMD filter later: only if a profile of the real callers shows the filter (it is 1.1-1.3x serial here).
3. Own flat BVH (b2): a second tree and its memory, a duplicate of the planned `DMeshAABBTree3` profile; 0-30 % more than b1. Not recommended.

How others solve it: see section 2 (renderers sort rays and use packets / wide BVHs for coherent or incoherent rays; geometry libraries expose batch
queries as parallel loops over the single query; the winding number route is Barill 2018).

## 5. g3cli / MCP command shape

Same `Command` spec serves the CLI and the MCP tools ([tools/g3cli/README.md](../../tools/g3cli/README.md)). Large inputs go
through files; inline arguments are capped (10 000 items) to keep agent context small. The result always has `status`, `code`, counts and a summary; per-item results are
truncated to `max_results` (default 100) and fully written to `--output` (CSV or JSON by extension).

`raycast <input> (--rays <file.json|csv> | --origin x,y,z --direction x,y,z) [--mode nearest|any|all] [--max-distance d] [--normalize true] [--output hits.csv] [--max-results n]`
- Rays: JSON `[{"origin":[x,y,z],"direction":[x,y,z]}, ...]` or CSV `ox,oy,oz,dx,dy,dz`; directions are normalized if `--normalize` (default true), otherwise a not-normalized ray gets status `not_normalized`.
- Result: `{ "rays": n, "hits": h, "misses": m, "invalid": k, "t_min": .., "t_max": .., "results": [{"ray": i, "triangle": id, "t": .., "point": [..], "front_face": true}] }`.
- Codes: `ok`; `invalid_arguments` (bad ray file, limits); `empty_mesh`; `read_error`; `operation_failed` for non-finite mesh coordinates.
- A convenience `--camera` (pinhole: position, target, fov, width, height) generating the rays avoids megabyte ray files for the common agent task ("render a depth image"): optional, stage 3.

`inside <input> (--points <file.json|csv> | --point x,y,z) [--method auto|crossings|winding] [--output flags.csv] [--max-results n]`
- `auto` (default): `crossings` if the mesh is closed and manifold (as `measure` reports), else `winding`; the result states `method_used` and, for an open mesh, a warning.
- Result: `{ "points": n, "inside": a, "outside": b, "invalid": k, "method_used": "crossings", "results": [{"point": i, "inside": true}] }`.

MCP: tools `raycast` and `inside` with the same schema (the `Param` list), arrays inline up to the cap, otherwise file paths; both are read-only and idempotent.

## 6. Open questions for the coordinator / owner

- Decided: the first PR has nearest hit and any-hit (`IntersectBatch`, `AnyHitBatch`, same options and statuses; any-hit equals `FindNearestHitTriangle(ray, maxDistance) != InvalidID`); all-hits later.
- b1 with the Lévy phase-2 fast-winding cache: the winding batch only calls `FastWindingNumber`, so it needs no change when that PR lands.
