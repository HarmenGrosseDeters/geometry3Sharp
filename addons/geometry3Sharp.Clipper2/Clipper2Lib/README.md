# Vendored Clipper2 2.0.0 (C#)

The C# sources of Clipper2 by Angus Johnson, release 2.0.0, the code of the NuGet package `Clipper2` 2.0.0. Boost
Software License 1.0 ([LICENSE_1_0.txt](LICENSE_1_0.txt), copied from `CSharp/NuGet/License.txt`; the repository's
`LICENSE` is the same licence). The source headers are kept.

- Source: https://github.com/AngusJohnson/Clipper2 (archived 2026-10-28), tag `Clipper2_2.0.1`, commit
  `21ebba05db8894f0c7217ad35ea518080f324946`, directory `CSharp/Clipper2Lib`. The repository has no tag `2.0.0`
  for C#: the files of this directory are the same in the release commit `fbc392c` ("Release ver. 2.0.0", 2025-12-17)
  and in `Clipper2_2.0.1` (only `Clipper2Lib.csproj` differs, NuGet settings; its `<Version>` is still 2.0.0).
  Later commits on the archived `main` (`6a36be1` triangulation fixes, `4da1564` FixSelfIntersects, 2026) are not included.
- Files: `Clipper.Core.cs`, `Clipper.Engine.cs`, `Clipper.Minkowski.cs`, `Clipper.Offset.cs`, `Clipper.RectClip.cs`,
  `Clipper.Triangulation.cs` (only because `Clipper.cs` refers to it; unused), `Clipper.cs`, `HashCode.cs` (its own
  MIT / BSD-2 notices for the .NET and xxHash code it contains), `PooledList.cs`.
- Changes, mechanical only, so that these types never clash with the `Clipper2` package in code that references both
  (and with the public enums of `geometry3Sharp.Clipper2`, which have the same names):
  1. `namespace Clipper2Lib` → `namespace g3.Clipper2.Vendored`;
  2. top-level types `public` → `internal` (members unchanged).

  Reproduce from a checkout of the tag:

  ```
  sed -E -e 's/^namespace Clipper2Lib\s*$/namespace g3.Clipper2.Vendored/' \
         -e 's/^( {0,2})public ((static |abstract |sealed |readonly |partial )*(class|struct|enum|interface) )/\1internal \2/' \
         CSharp/Clipper2Lib/<file> > <file>
  ```

  Any later change to these files (bugfixes) is listed here, with its PR:
  - FINDINGS item 5 (2026-11-12, PR 2 of the vendoring): `Clipper.Engine.cs`, `DoSplitOp` drops a split-off triangle of
    the other area sign only if it does not lie inside the rest of the path (new `TriangleInsidePath`); before, such a
    hole touching its path at a vertex was dropped and filled. Marked "geometry3Sharp change" in the source.
  - #756 (2026-11-13): `Clipper.Engine.cs`, `FixSelfIntersects` takes its "micro self-intersection" branch only if the
    inserted vertex does not flip the ring's orientation (new `TryMicroFix`, first named `MicroFixKeepsOrientation`;
    otherwise `DoSplitOp`). Upstream's later removal of that branch (`4da1564`, issue #1067) is not adopted (owner 2026-11-13).
  - The rest of the #756 class (2026-11-15): `TryMicroFix` also requires the copied vertex to lie within one grid unit of
    the line of the edge it is inserted on (`PerpendicDistFromLineSqrd` <= 1); farther away the insertion cut a triangle off
    the region. This also changes a few results that were right, within the rounding (92 of 1.6 million at 2^20), so the
    vendored engine can differ from the package there too; the differential test's inputs hit none of them.
  - Fixes of the C++ Clipper2 after the archive, ported (2026-11-15, owner 2026-11-15; PolyClipping/Clipper2, the C++
    continuation by Seth Hillbrand, BSL-1.0; the C# code is translated from the C++ diffs, each marked "geometry3Sharp change
    (port of ...)", with the upstream tests translated in `Clipper2UpstreamFixesTests`):
    - `f774daf` "Round intersection points and sort them stably" (`Clipper.Core.cs`, `GetLineIntersectPt`: both segments
      are evaluated from their bottom end and the point is rounded, ToEven like C++'s `nearbyint`, instead of truncated;
      `Clipper.Engine.cs`, `ProcessIntersectList`: a stable merge sort instead of `List.Sort`). This changes most outputs
      within the rounding.
    - `ec5f2d4` "Order edges leaving a shared vertex by direction" (`Clipper.Engine.cs`, `UpdateEdgeIntoAEL`).
    - `f97d65d` "Stop CheckSplitOwner recursing forever on cyclic splits" (`Clipper.Engine.cs`, `CheckSplitOwner`): the
      same result wherever the old code terminates; no input that reaches a cycle in the C# code was found, so no test
      fails without it.
    - Not ported: `4a3bf64` (keep clamped intersections inside the scanbeam): with the rounding of `f774daf` the C# engine
      did not reach that clamp on its upstream test or on 3.2 million random results (200 000 pairs of 3- to 6-gons with
      flat edges, 70 000 x 200 up to 2^52 x 2^30, and near-coincident rings, every clip type and fill rule), so it could
      not be tested; the upstream test passes without it. Not taken either: the offset fix `ca30beb` (empty paths; the
      adapter passes none), `64a9ecc`
      (offset output normalized to positive winding, a behaviour change of 593 lines), and the build, performance and
      triangulation commits.
  - `6c7d083` "Stop ClipperOffset leaking per-path state" (2026-11-15, `Clipper.Offset.cs`, `DoGroupOffset`,
    `OffsetOpenPath`, `DoRound`; tests `Clipper2OffsetStateTests`): the end type and the group delta are locals of a path
    and a group, the normals are built before the single-point case, and a single point recomputes its arc steps under a
    delta callback (`SetArcSteps`, with the C# 2.0.0 formula). Before, a 2-point Joined path gave the rest of its group open
    ends: through the adapter, `OffsetPolyLines` with `EndType.Joined` gave 47 200 instead of 73 022 when a 2-point
    polyline came first. Seen on the way, not changed: the C# 2.0.0 code drops the single points of a Polygon group (the
    upstream test of this commit expects them offset, so `DeltaCallback_SeesTheNormalsOfItsOwnPath` uses an open group).
- `.editorconfig` here silences CS8632: upstream compiles with `<Nullable>enable</Nullable>`, this package does not,
  and the annotations of the files without a `#nullable` directive only warn; they do not change the compiled code.
- Behaviour: `tests/geometry3Sharp.Clipper2.Tests/Clipper2VendoredDifferentialTests.cs` runs these sources and the
  NuGet package side by side (booleans in every clip type, fill rule and PreserveCollinear setting, open paths,
  offsets in every join and end type, Minkowski sums and differences, SimplifyPaths, Area, bounds, point in polygon)
  and, since the ported rounding (owner 2026-11-15), requires: a boolean that differs is within the rounding of the
  package's (paths at most two grid units apart, areas within the perimeter) or within the rounding of the exact area
  (`ExactRegionArea`); offsets, Minkowski sums and open paths differ only within the rounding; everything without
  intersection points (SimplifyPaths, Area, bounds, point in polygon) is identical; at the adapter's magnitudes (±2^20 to
  ±2^50) and with degenerate
  paths. The review of #751 compared the netstandard2.0 build with the package's DLL by IL: the same types, members
  and method bodies, apart from a method-group delegate cache of the newer compiler in `ConvertHorzSegsToJoins`. In the
  net10.0 build, `x.EnsureCapacity(n)` (`Clipper.Engine.cs`, `ClipperEngine.AddPathsToVertexList`) binds to
  `List<T>.EnsureCapacity` instead of the extension method of the sources, which changes list capacities only.
- Licences in the package: `THIRD-PARTY-NOTICES.txt` (next to the addon's csproj) holds the BSL-1.0 text and the
  notices of `HashCode.cs`; the package licence expression is `BSL-1.0 AND MIT AND BSD-2-Clause`.
