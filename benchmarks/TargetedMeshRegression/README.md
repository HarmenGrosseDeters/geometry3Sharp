# Targeted Mesh Regression Benchmark

A fixed panel for `MeshAlphaWrap`, `MeshRobustRepair`, and `ExactMeshBoolean` through the public `g3cli`. The panel exercises changes in geometry and topology near thresholds, reproduces exact-contact and export cases, and complements the larger [repair](../MeshRepairBenchmark/README.md) and [Boolean](../BooleanComparison/README.md) corpora.

## Procedural panel

| Area | Coverage | Checks |
|---|---|---|
| Wrap | Open sheet, cavities, narrow bores and gaps, thin wall, small separate part, slivers and edge contact | Clean solid, bore and gap probes. Optional independent strict containment of input triangles. |
| Repair | Missing faces, seams, duplicates, winding, degeneracies, T-junctions, non-manifold contacts, cavities and clean inputs | Clean result, component count, signed volume and sampled surface distance to a known reference where defined. |
| Boolean | Disjoint, contained, identical, touching, coplanar, epsilon overlap, thin cut, rescaled, far from the origin and rational intersections | Analytical volume and emptiness, occupancy probes and bounds on box-arrangement cells, Safe Rounding diagnostics and selected binary STL round trips. |
| Import and pipeline | Nested GLB instancing with a reflection, repaired cutter and equivalent Boolean trees | World bounds, orientation, components, volume and sampled geometric agreement. |

Run from the repository root with .NET 10 and Python 3.12:

```sh
dotnet build tools/g3cli/g3cli.csproj -c Release
suite=benchmarks/TargetedMeshRegression
cli="$PWD/tools/g3cli/bin/Release/net10.0/g3cli.dll"
work="$(mktemp -d)"
fixtures="$work/fixtures"
runtime="$(command -v dotnet)"

python -m unittest discover -s "$suite"
python "$suite/generate.py" "$fixtures"
python "$suite/g3_run.py" --dotnet "$runtime" --cli "$cli" --fixtures "$fixtures" --output "$work/baseline"
python "$suite/g3_run.py" --dotnet "$runtime" --cli "$cli" --fixtures "$fixtures" --output "$work/sweep" --only wrap --sweep --timeout 12
```

Use repeated `--case` options to select fixture IDs and repeated `--job CASE CONFIG` options to retry specific wrap configurations. For example, add `--job W02-open-box alpha-D200 --timeout 45` to the sweep command. Exit status identifies runner errors. Read `results.json` for operation refusals, timeouts and discrepancies. `contact_topology` records contact geometry that warrants checking the Boolean-specific diagnostics alongside the general mesh check.

To verify strict containment independently, compile the separate CGAL 6.0.1 reference driver, then check a completed run:

```sh
g++ -std=c++17 -O2 "$suite/check_wrap.cpp" -o "$work/check-wrap" -lgmp -lmpfr
python "$suite/verify_g3_wrap.py" "$work/check-wrap" "$work/baseline" "$work/sweep"
```

## Fixed external inputs

[Corpus manifest](corpus-sources.json) records SHA-256, source revision, IDs and selection metadata. Fetch at runtime. Run from scratch storage and retain only JSON result rows when sharing runs. The generator is the source of all procedural fixtures.

| Source | Coverage and reference |
|---|---|
| ABC | Six STEP solids with bores, thin plates, fillets, splines, planar controls and small faces. Open Cascade tessellates each independently at two resolutions and supplies the B-rep Boolean volume reference. |
| Objaverse | Five CC0/CC-BY GLBs covering seams, genuinely open surfaces, material primitives and split pieces. Compare raw and exact-position-welded imports. A separate glTF reader checks orientation and placement through the Mushroom 2 node hierarchy. |
| EMBER | Five original transformed Thingi10K pairs. Compare native union, intersection and difference against an independent Manifold 3.0.1 volume. |
| ThingiCSG | Two cylinders, the 20-by-20 rod grid, the 70 rotated cubes and its 20-operand prefix. Use analytic, OpenSCAD and Manifold volume references where applicable. |
| Thingi10K | Models `356074`, `996816` and `101633` exercise projection precision, intersection density and narrow intersecting triangles. |

```sh
python "$suite/fetch_extended.py" "$work/data"
python "$suite/corpus_extension.py" objaverse --data "$work/data" --output "$work/real" --dotnet "$runtime" --cli "$cli"
python "$suite/scene_checks.py" --data "$work/data" --output "$work/real" --dotnet "$runtime" --cli "$cli"
python "$suite/real_booleans.py" ember --data "$work/data" --output "$work/real" --dotnet "$runtime" --cli "$cli"
python "$suite/real_booleans.py" csg --data "$work/data" --output "$work/real" --dotnet "$runtime" --cli "$cli"
python "$suite/g3_run.py" --dotnet "$runtime" --cli "$cli" --fixtures "$fixtures" --output "$work/thingi" --only thingi --thingi "$work/data/thingi10k"
```

For Objaverse containment, run `python "$suite/verify_corpus_wrap.py" "$work/check-wrap" "$work/real" --corpus objaverse`. Use NumPy for the scene check, Manifold 3.0.1 for the numerical Boolean reference, and OpenSCAD 2021.01 for the ThingiCSG reference. The manually dispatched [Targeted ABC mesh benchmark](../../.github/workflows/targeted-mesh-abc.yml) uses CadQuery 2.7.0 on a GitHub-hosted runner. It publishes only JSON result rows. ABC fetches, derivative meshes and assets are excluded from PR-tier tests. Source attribution and licenses are in the [source ledger](../../docs/sources/tools-and-verification.md#targeted-mesh-regression-benchmark).

## First observations

The [recorded case results](results/baseline.csv) are from the public CLI at library commit `864cd5f98a70781b4c80bb0c214daa2f6b72bbb7`. The commands above reproduce the panel. Timings are observations from one shared Linux machine.

- `B13-far-origin` lost its 0.5-wide X extent on binary STL export and reimport, after the OBJ intersection preserved the expected volume of 0.375.
- The small ABC bores closed at both tessellations under the baseline wrap parameters. The largest remained open. Compare the two tessellations against a CAD reference to avoid attributing input discretization to the Boolean solver.
- In Objaverse, position welding reduced Purple Seashell from 190 boundary edges to zero. Cartoon Paper Boat still failed robust repair after position welding, while direct Wrap strictly enclosed the imported triangles.
- A larger budget let `996816` finish via Solidify. The 70-operand rotated-cube union timed out again at 95 seconds. Its 20-operand prefix finished with a clean result.

For the longer checks, retry the timed wrap configurations at 45 seconds with `--job`; run the rotated cubes and prefix with `--case CSG-rotated-cubes --case CSG-rotated-cubes-first20 --csg-budget 95`. For the repair follow-up, run the two commands below and retain the original 25-second timeout as a separate observation.

```sh
timeout 120s "$runtime" "$cli" repair "$work/data/thingi10k/996816.stl" "$work/996816-default.obj" --compact
timeout 60s "$runtime" "$cli" repair "$work/data/thingi10k/996816.stl" "$work/996816-no-exact.obj" --no-exact-resolve --compact
```
