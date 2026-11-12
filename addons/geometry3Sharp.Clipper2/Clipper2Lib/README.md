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
- Changes, mechanical only, so that these types never clash with the `Clipper2` package (which the public API of
  `geometry3Sharp.Clipper2` still uses for `Point64`, `Path64`, `Paths64`, `ClipType`, `FillRule`, `JoinType` and
  `EndType`):
  1. `namespace Clipper2Lib` → `namespace g3.Clipper2.Vendored`;
  2. top-level types `public` → `internal` (members unchanged).

  Reproduce from a checkout of the tag:

  ```
  sed -E -e 's/^namespace Clipper2Lib\s*$/namespace g3.Clipper2.Vendored/' \
         -e 's/^( {0,2})public ((static |abstract |sealed |readonly |partial )*(class|struct|enum|interface) )/\1internal \2/' \
         CSharp/Clipper2Lib/<file> > <file>
  ```

  Any later change to these files (bugfixes) is listed here, with its PR.
- `.editorconfig` here silences CS8632: upstream compiles with `<Nullable>enable</Nullable>`, this package does not,
  and the annotations of the files without a `#nullable` directive only warn; they do not change the compiled code.
- Behaviour: `tests/geometry3Sharp.Clipper2.Tests/Clipper2VendoredDifferentialTests.cs` runs these sources and the
  NuGet package side by side (booleans in every clip type, fill rule and PreserveCollinear setting, open paths,
  offsets in every join and end type, Minkowski sums and differences, SimplifyPaths, Area, bounds, point in polygon)
  and requires the same output point for point, at the adapter's magnitudes (±2^20 to ±2^50) and with degenerate
  paths. The review of #751 compared the netstandard2.0 build with the package's DLL by IL: the same types, members
  and method bodies, apart from a method-group delegate cache of the newer compiler in `ConvertHorzSegsToJoins`. In the
  net10.0 build, `x.EnsureCapacity(n)` (`Clipper.Engine.cs`, `ClipperEngine.AddPathsToVertexList`) binds to
  `List<T>.EnsureCapacity` instead of the extension method of the sources, which changes list capacities only.
- Licences in the package: `THIRD-PARTY-NOTICES.txt` (next to the addon's csproj) holds the BSL-1.0 text and the
  notices of `HashCode.cs`; the package licence expression is `BSL-1.0 AND MIT AND BSD-2-Clause`.
