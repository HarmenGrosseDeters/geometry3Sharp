# geometry3Sharp browser demos (G3Demos)

Interactive demos that run the real library in the browser: a standalone Blazor WebAssembly app that references
`geometry3Sharp_netstandard.csproj` (its `netstandard2.0` build) and draws with three.js. They are dogfooding: the library
used the way a customer would use it, with the result checks a customer would run shown next to every result. The
findings are in [docs/research/DOGFOODING_DEMOS_2026-11.md](../../docs/research/DOGFOODING_DEMOS_2026-11.md).

| Demo | Library entry | Knobs | Checks shown |
|---|---|---|---|
| Exact Boolean | `SolidBooleanPipeline` (default) or `ExactMeshBoolean` | operation, box or sphere operand, overlap, fine gap or overlap `10^-k`, rotation, near-coplanar tilt `10^-k` rad, scale `2^k`, pipeline `TargetFraction` | status and error, `Report` (clean, contacts, rounding volume, route, stage times), result checks, analytic volume (axis-aligned boxes), inclusion-exclusion over the three operations, bit-identity under `2^k` |
| Robust repair | `MeshRobustRepair` (defaults) | sphere, box or torus; unwelded soup, every k-th triangle flipped, holes, jitter, duplicate faces, an overlapping second shell, crossing fins | before and after: result checks, boundary edges in red, `ResultPath` and reasons, volume IoU, the volume against the known volume |
| Remeshing | `Remesher` with `PreventNormalFlips`, `ApplySafePreset()`, `PreserveBoundaryAndFeatures`, `PinSharpVertices`, `MeshProjectionTarget.Auto` | input (coarse box, twisted bar, sphere, coarse torus), target edge length, passes, each guard on or off, scale `2^k` | result checks, inverted triangles (normal against the nearest input triangle), minimum quality, slivers, distance to the input, guard counters, time per pass |

Result checks (`Demos/Core.cs`, `MeshCheck`): closed (boundary edges and `ExactMeshBoolean.IsWindingClosed`), manifold
vertices, finite coordinates, components, `MeshRobustRepair.NotCleanReasons`, volume and area. A failure or refusal is
shown with its status and error, never hidden; a success whose result does not pass the checks is shown in red.

All inputs are procedural. No mesh files are loaded or committed.

## Build and run

```
dotnet build demos/G3Demos -c Release
dotnet publish demos/G3Demos -c Release -o <dir>
```

Serve `<dir>/wwwroot` with any static file server (the page works from any sub path) and open it. three.js 0.170.0 (MIT)
is loaded at runtime from `cdn.jsdelivr.net` through the import map in `wwwroot/index.html`, pinned by sha384 integrity
hashes of the npm package's files; it is not in the repository. CI builds the demo (`dotnet build`, linux job), so a library
change that breaks it shows there; the browser check below stays local.
`dotnet run --project demos/G3Demos` serves it locally too.

WebAssembly runs the .NET IL interpreter by default. `-p:RunAOTCompilation=true` (needs `dotnet workload install
wasm-tools`) compiles ahead of time: a larger download and a slow publish, but several times faster operations (numbers in
the findings document).

## Browser check

`e2e/smoke.mjs` loads the published app in headless Chromium (the pre-installed Playwright), runs one operation in each
demo, saves a screenshot per demo and fails on any console error, page error or failed request. The CDN requests of the
import map are answered from a local copy of the same three.js version, so it needs no network:

```
npm pack three@0.170.0 && tar xzf three-0.170.0.tgz          # in a scratch folder, never committed
NODE_PATH=$(npm root -g) node demos/G3Demos/e2e/smoke.mjs <dir>/wwwroot <scratch>/package <screenshot dir>
```

With three more arguments it also runs cases of `e2e/cases.json` in WebAssembly (JS interop into `Probe.RunCase`, the
code path of the UI) and writes one line per case: `... <cases.json> <name regex> <out.txt>`. The same cases run natively
by compiling `Demos/*.cs` into a console host that calls `Probe.RunCase`; each line carries a hash of the input and of
the result mesh, so a platform difference of the input can be told from one of the operation.

## Layout

- `Demos/`: the demo logic in plain C# (no UI): operands, the library calls, the checks, `Probe` for the case runner.
- `Pages/DemoPage.razor`: the parameter panel, the canvas and the status panel.
- `wwwroot/js/viewer.js`: the thin three.js layer (vertex and index buffers in, drawing out).
- `e2e/`: the browser check and the case list.

The library project excludes `demos/**` from its compile items (`geometry3Sharp_netstandard.csproj`), as it does for
`tests/`, `benchmarks/` and `tools/`; nothing else in the library or its package changes.
