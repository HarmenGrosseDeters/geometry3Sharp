# Long-key hash audit outside the Boolean

Tier M. Scope: Terrain long tables, geodesics, WindingContouring ancestors and DualContouring cell/node tables. No key encoding, equality, algorithm, public API or Boolean changes. Acceptance: reproduce collisions with dense keys before changing a comparer; preserve operation tests. Reuse the existing SplitMix64 `EdgeKeyComparer` from #426 rather than adding a second hash implementation. Stop after this audit; no algorithm redesign or corpus timing sweep.

Run:

```sh
dotnet run --project benchmarks/LongHashAudit -c Release
```

Raw output: [perf/dense-keys.txt](perf/dense-keys.txt). Each line reports unique keys, distinct default hashes and distinct production SplitMix64 hashes on the **same** keys. This is a deterministic collision probe, not an end-to-end speed claim. The edge dataset is the horizontal, vertical and diagonal edges of a 128 by 128 grid. The coordinate probes use the exact packing formulas in the audited files. Compact grids have side 64; wide Winding grids are 512 by 2 by 512; wide Dual grids are 2048 by 2 by 32. The high-bit coarse flag is also probed.

Decisions:

- Use the existing comparer for Terrain's packed edge, overlay vertex, catch and face keys, and its merge grids. The 32-bit pair collapses 48,641 edges to 63 hash codes; the 31-bit pair to 27,178; Safety's `min * int.MaxValue + max` to 16,383; the merge grid's 16,384 cells to 1,017. Tables sharing those encodings use the same comparer.
- Use it for `MeshGeodesicPath`'s packed joint keys. Keep `MeshGeodesicDistance`'s linear weight and fan keys and `IntrinsicCommonSubdivision`'s linear dart keys: no collision effect demonstrated for the dense linear-index sample.
- Use it for Winding ancestors and DualContouring cell/node tables: compact cubes have no default collisions, but the supported wide coordinate ranges halve the distinct hash count. These tables can hold both compact and wide regions, so they share one comparer. This does not alter their packing or its supported bounds.
- Keep overlay `containerOf`, whose keys are only scalar A/B vertex IDs (distinct default hashes in the control).
- Keep `TerrainDualContouring`'s linear corner indices: the default hash is collision-free in the sample; SplitMix64 introduces one collision.

No new hash mechanism or external code. Prior art: #426 and the in-tree `EdgeKeyComparer` (SplitMix64 finalizer); .NET `Int64.GetHashCode` folds the high and low 32 bits with XOR. Equality stays exact long equality. Small inputs with already distinct default hashes pay for mixing; total operation speed has not been measured here.

Regression tests: `ContouringQualityLongHashTests` checks the poor distributions, exact set membership with the comparer, and all 40 constructors of the audited packed-key tables. Existing Terrain, geodesic and contouring tests exercise operation results, reuse and parallel paths.

## Local validation

.NET SDK 10.0.403, `DOTNET_PROCESSOR_COUNT=4` (2 for subsequent builds). Commands:

```sh
dotnet test tests/geometry3Sharp.Tests -c Release --no-restore -m:1 --filter 'FullyQualifiedName~Terrain|FullyQualifiedName~Geodesic|FullyQualifiedName~Intrinsic|FullyQualifiedName~Contouring'
dotnet test tests/geometry3Sharp.Tests -c Release --filter FullyQualifiedName~ContouringQualityLongHashTests
dotnet build geometry3Sharp_netstandard.csproj -c Release -m:1
dotnet build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true -m:1
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o /tmp/longhash-pack -p:Version=1.0.0-ci.1 -m:1
```

The area run passed 755 of 756 tests; its sole failure was the new source scan's expected constructor count (39 instead of 40: one line creates two sets). After correcting that assertion, all five new tests passed, including the source scan. No operation test failed. Both library targets, the net48 test build and package API validation passed. No net48 execution on Linux; full repository CI is left to the draft PR.

Mutation check: temporarily replace `MeshGeodesicPath`'s `new HashSet<long>(EdgeKeyComparer.Instance)` with `new HashSet<long>()`; run `dotnet test tests/geometry3Sharp.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~AuditedPackedTables_UseTheMeasuredComparer`. It fails (1 failed, 0 passed). Restore the file and run all five new tests with `--no-build --no-restore`: 5 passed. This is a source-policy mutation, not a Stryker run.

Independent Tier M review found one unjustified change (`containerOf`): reverted and added the scalar control. It also kept the large membership test on hash lookups instead of an enumerable assertion. No remaining concrete correctness finding.
