# Boolean comparison

Runs `ExactMeshBoolean` and external mesh boolean implementations on the same inputs and evaluates every
result the same way. Each input mesh A is paired with a rotated, shifted copy B; union, intersection and
difference are computed by every method.

The evaluation (`BooleanComparison eval`) welds result vertices by position and reports for each method:
closedness in the winding sense, manifold closedness, exact self-intersections of the output, volumes and the
volume identities `V(A∪B) + V(A∩B) = V(A) + V(B)` and `V(A−B) = V(A) − V(A∩B)` (relative error).

The external code is not part of this repository; build it next to it as described below.

## Methods

| name | implementation | license | threads |
|---|---|---|---|
| `ours` | `ExactMeshBoolean` (this library) | Boost | parallel |
| `cgal_epick` | CGAL Polygon Mesh Processing corefinement, inexact constructions | GPL | 1 |
| `cgal_epeck` | the same with exact constructions | GPL | 1 |
| `cherchi` | Cherchi et al. 2022, *Interactive and Robust Mesh Booleans* | MIT | parallel |
| `manifold` | Manifold (Python `manifold3d`) | Apache-2.0 | parallel |

All tools report the time of the boolean alone (without file I/O); `wall_ms` includes process start and I/O.
CGAL refuses inputs that are open or self-intersecting; Manifold refuses non-manifold inputs.

## Building the tools

```sh
dotnet build -c Release benchmarks/BooleanComparison

# CGAL (Ubuntu: apt install libcgal-dev libgmp-dev libmpfr-dev)
g++ -O2 -std=c++17 -DNDEBUG -DCGAL_NDEBUG external/cgal_bool.cpp -o cgal_bool -lgmp -lmpfr

# Cherchi et al.: clone https://github.com/gcherchi/InteractiveAndRobustMeshBooleans and build it with CMake
# (Release). To get the time of the boolean alone, wrap booleanPipeline(...) in main.cpp with a timer that
# prints {"ms":<milliseconds>} to stderr.

# Manifold
python3 -m venv env && env/bin/pip install manifold3d numpy
```

## Running

```sh
# copy the built tool, so that rebuilding the library during a run does not mix versions
cp -r bin/Release/net10.0 /some/where/frozen
export BOOLCMP_TOOL=/some/where/frozen/BooleanComparison.dll
export CGAL_BOOL=/path/to/cgal_bool
export CHERCHI_BOOL=/path/to/InteractiveAndRobustMeshBooleans/build/mesh_booleans
export MANIFOLD_PYTHON=/path/to/env/bin/python

python3 compare.py meshes.txt results.jsonl --timeout 300 -j 1   # a list of mesh files, or a directory
python3 analyze.py results.jsonl
```

Use `-j 1` for timings: several methods are multi-threaded. The input meshes can be the Thingi10K selection
of `benchmarks/MeshRepairBenchmark`.

A single run of our method, with phase times, counts and GC statistics:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll export mesh.stl dir      # writes dir/A.off, dir/B.off
dotnet bin/Release/net10.0/BooleanComparison.dll run dir/A.off dir/B.off union out.off
```
