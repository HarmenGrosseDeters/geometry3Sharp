# Boolean comparison

Runs `ExactMeshBoolean` and external mesh boolean implementations on the same inputs and evaluates every
result the same way. The inputs come in data classes (see below): pairs of meshes A and B, of which union,
intersection and difference are computed by every method, and sequences of booleans, of which the final
result is evaluated.

The evaluation (`BooleanComparison eval`) welds result vertices by position and reports for each method:
closedness in the winding sense, manifold closedness, exact self-intersections of the output, triangles of zero
area, volumes (measured relative to the centre of A, so that coordinates far from the origin do not cancel) and
the volume identities `V(A∪B) + V(A∩B) = V(A) + V(B)` and `V(A−B) = V(A) − V(A∩B)` (relative error); for
synthetic cases also the error against the analytically known volumes of the results.

The external code is not part of this repository; build it next to it as described below.

**Licenses:** the files in `external/` are small drivers written for this comparison. `cgal_bool.cpp` links
against CGAL's Polygon Mesh Processing, which is GPL: a compiled `cgal_bool` falls under the GPL, so it is not
distributed, and nothing of CGAL is part of the library (which stays under the Boost license). Cherchi et al.
(MIT) and Manifold (Apache-2.0) are only run as external programs as well. See also `docs/SOURCES.md`.

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

## Data classes

| class | cases | what it tests | how to get it |
|---|---|---|---|
| `thingi10k` | Thingi10K meshes, each with a rotated, shifted copy | real-world models, many open or self-intersecting | `benchmarks/MeshRepairBenchmark/fetch_data.py`; a list or directory of meshes |
| `cgal` | 89 CGAL test and example meshes, each with a rotated, shifted copy, and the pair of CGAL's corefinement examples (`blobby`, `eight`) | CAD and scanned models, some open or degenerate | `python3 data/fetch.py cgal <dir>` (CGAL v6.0.1) |
| `cherchi` | the test meshes of Cherchi et al.: bunny with cow at 25k–200k triangles, two spheres, a cube with the armadillo, the stencil of the paper (fertility minus 703 spheres given as one mesh), five meshes with a rotated, shifted copy | larger inputs, time | `python3 data/fetch.py cherchi <dir>` (commit 7bd6c26) |
| `synthetic` | 24 pairs of boxes and spheres | coplanar faces, solids touching at a face, an edge or a vertex, overlaps and gaps of 1e-9, nearly coplanar faces, UTM coordinates (3e5, 5.6e6), coordinates of 1e8, scale differences of 1e6 | `BooleanComparison synth <dir>` |
| `sequence` | iterated cube grids: n³ overlapping unit cubes (spacing 0.9, so the faces of a layer are coplanar) united one after the other, then a cube of edge 0.5 subtracted at the centre of each (n = 5: 249 booleans, n = 10: 1999) | repeated booleans on results of booleans, exactly coplanar faces | `BooleanComparison synth <dir>`; one pass over all meshes: `BooleanComparison nary` (below) |
| `nreal` | n-ary unions from real data: 15 multi-component Thingi10K meshes split into their connected components (3-26 inputs each, overlapping shells of one STL), and 8-copy arrays of the CGAL parts `fandisk`, `pinion`, `anchor` | the one-pass n-ary boolean on real inputs (coplanar CAD neighbours, many self-pairs) | `python3 perf/nreal.py <tool dir> <cgal meshes>` (`BooleanComparison components` / `array`); `profile` runs the union of all `N*.off` of a case; `perf/cases-nreal.txt` |
| `knot` | an open tube wound tightly inside a ball like a ball of yarn (one component, two open ends, many self-intersecting pairs per triangle), with the export's rotated, shifted copy: a procedural stand-in for the knot of Thingi10K 55928 | open operands with a dense self-intersecting region: per-piece open winding and triangulation cost (`docs/design/EXACT_BOOLEAN_TAIL_55928.md`) | `BooleanComparison knot <dir> [--segments s] [--ring m] [--turns t] [--tube r] [--seed k]` ([below](#tail-case-knot-the-55928-stand-in)) |
| `thingi-pairs` | 1000 pairs of different clean Thingi10K models (1k-100k triangles each) after the EMBER protocol, B randomly rotated and shifted; `thingi-pairs-size`: 100 pairs of models with 100k-1M triangles | the performance and correctness of the three operations on real models at scale, with `NotCleanReason` and the volume identities as the oracle | `python3 data/thingi_pairs.py fetch/filter/manifest` ([below](#thingi10k-pairs-after-the-ember-protocol)); the pair lists are `thingi-pairs/pairs.csv` and `pairs_size.csv` |
| `thingi-contact` | Thingi10K pairs (or one model against itself) with deliberate exact contact: coplanar faces touching or overlapping, a one-ulp or few-ulp offset, a mirror image; 5 variants, 100 pairs each in the list | the hard cases of the exact predicates: exactly coplanar faces, touching edges, flush parts (the use case of `ExactMeshImprint`), where `thingi-pairs` almost never touches exactly | `python3 data/thingi_contact.py fetch/manifest` ([below](#thingi10k-pairs-with-exact-contact)); the list is `thingi-pairs/contact_pairs.csv` |

`data/fetch.py` downloads the meshes at a fixed commit and writes a case manifest (`cases.jsonl`); nothing of the
data is part of this repository (sources and licenses in `docs/SOURCES.md`). The `cube_grid` sequences are
modelled after the iterated cube grid of the Open Boolean Benchmark, whose cases are not published.

In a sequence, ExactMeshBoolean and Manifold keep the intermediate results in memory (both run the sequence as
an Open Boolean Benchmark request, see below), CGAL keeps them in memory as well (`cgal_bool seq`, so EPECK stays
exact between the steps), Cherchi et al. run one process per boolean and pass the results as OBJ files (its
program has no other interface).

## Thingi10K pairs after the EMBER protocol

The classes `thingi-pairs` (1000 pairs) and `thingi-pairs-size` (100 pairs) are a reproducible boolean benchmark on
real models after the protocol of EMBER (Trettner, Nehring-Wirxel, Kobbelt, *EMBER: Exact Mesh Booleans via Efficient
& Robust Local Arrangements*, SIGGRAPH 2022: random pairs of Thingi10K solids, a random rotation, boxes that overlap,
all operations; read from the paper only, EMBER's code is commercial and was not used). Only the pair lists are in the
repository (`thingi-pairs/`), never meshes: the models are fetched at run time from the Thingi10K dataset on
huggingface into a directory outside the repository (individual licences, mostly Creative Commons; see
`docs/SOURCES.md`).

**Files** (`thingi-pairs/`, all generated, small):

| file | content |
|---|---|
| `clean_models.csv` | the 3236 models (of 3399 candidates) that pass the filter below: id and triangle count |
| `pairs.csv` | 1000 pairs, seed 20261104: model ids a and b, quaternion q (x y z w) and translation t of B |
| `pairs_cgal_sample.csv` | 100 of the 1000 pairs (same rows and pair numbers), seed 20261106, partial Fisher-Yates over the list: the pairs CGAL EPECK runs on |
| `pairs_size.csv` | the size class, 100 pairs, seed 20261105, models with 100 001-1 000 000 triangles |

**Filter** (which models may be drawn): Thingi10K's own metadata first (`geometry_data.csv`, `input_summary.csv`:
closed, edge manifold, vertex manifold, no degenerate faces, no duplicated faces, PWN, zero self-intersections, 1k-1M
triangles: 3399 models), then our own check on the mesh as `StandardMeshReader` reads it (`BooleanComparison
thingi-filter`): `MeshRobustRepair.NotCleanReason(mesh) == ""` and a positive volume. The check removed 163 of the 3399:
122 degenerate triangles (area < 1e-12 diagonal²), 34 inside-out components, 7 self-intersections (that the metadata
does not report). The 3065 models with 1k-100k triangles are the pool of `pairs.csv`, the 171 with 100k-1M of
`pairs_size.csv` (so the size class uses most models about once, some twice: the pool is small). Models with several connected components are allowed.
`python3 data/thingi_pairs.py filter` rebuilds `clean_models.csv`; it depends on the library's `NotCleanReason`, so the
file is committed, not recomputed per run.

**Pairs** (`python3 data/thingi_pairs.py generate`, a pure function of `clean_models.csv` and the seeds, own PRNG
SplitMix64, values written with 12 decimals: running it again gives the identical files, a test checks it): per pair
two different models drawn uniformly with replacement across pairs; a uniformly distributed rotation
(Shoemake's quaternion) and a translation uniform in [-0.5, 0.5]³ for B. `BooleanComparison thingi-pair` turns this
into the case: A and B are each moved so that the centre of their bounding box is the origin and scaled so that the
longest side is 1 (the probe of 2026-11-04 did the same), then B is rotated about the origin and translated. The
boxes of A and B are therefore always within half a unit in every axis, so they overlap in the usual case, as EMBER
draws them; a thin model can still miss (an empty intersection is then correct and counted). A.off / B.off are written
with round-trip precision, so all tools read the same doubles.

**Oracle** (`eval`, for results of all tools alike; the case's `info.json` has `notclean`): per result
- `clean_reason`: `MeshRobustRepair.NotCleanReason` of the result welded by exact position (closed, manifold, no
  degenerate triangles (area < 1e-12 diagonal²), consistent orientation, no inside-out components, no
  self-intersections: found by `MeshRepairMetrics.SelfIntersections` on any size, `eval` otherwise stops at 400k triangles);
  `""` is clean, `"empty"` is an empty result;
- the volume identities `V(A∪B) + V(A∩B) = V(A) + V(B)` (relative to V(A) + V(B)) and `V(A−B) = V(A) − V(A∩B)`
  (relative to V(A)) on the doubles of the written results, volumes relative to the centre of A. An empty result is
  not an error by itself (A∩B of two apart models, A−B of A inside B), and an empty union, or an empty difference with
  V(A−B) > 0, fails the identity. But the identities alone do not make an empty result correct: a method that returns
  A and B concatenated as the union, nothing as the intersection and A as the difference passes both. Its union
  self-intersects, which for clean inputs in random poses a correct result does not, so in these classes a result with
  self-intersections (or inside-out components) is not correct (`analyze.py`), and the summary counts an empty result
  as volume-consistent only if no result of the pair self-intersects.

`analyze.py` (outcome per pair and method as everywhere, `degenerate` is new: correct but NotCleanReason names degenerate
triangles, bowties or inconsistent orientation) uses the tolerance **1e-7** for these classes (1e-6 elsewhere).
Why: the only error a correct boolean has is the rounding of the result's vertices to double and of the volume sum.
A vertex is moved by at most half an ulp (1e-16 of the box), the volume by about surface area × that, so the relative
error is about 1e-16 × area × size / volume (an estimate, not a bound; larger for thin models; the probe of 2026-11-04
saw 5e-10 on 30 pairs; the trial below at most 1.1e-11 for our results and 1.6e-14 for the others, which do not round
a new vertex). 1e-7 is 100× the largest value seen and still catches a displaced sheet of a result
(a moved vertex of 1e-4 in a unit box gives 1e-5 in a test). A limit of a volume oracle: a small closed part of the
union that is dropped whole (the rest closed and clean) is seen only if its volume is above 1e-7 (V(A) + V(B)), about a
cube of 3.7e-3 of the box; the triangle counts of the methods are in the raw rows. The tolerance is `CLASS_TOL` in
`analyze.py`; change it only with the numbers of a run. Reported per method (`python3 data/thingi_pairs.py summary results.jsonl`): the
operations by status, clean / empty / unclean (with the reasons), the largest volume error, the time of the boolean
alone (median, geometric mean, p95, max, as every class) and the median result triangles. A result with
`degenerate triangles` is an outcome of the check, not necessarily a defect that matters; the table says it, the reader decides.

**Running** (all commands from `benchmarks/BooleanComparison`; the tools as in [Running](#running)):

```sh
D=/path/outside/the/repo/thingi            # about 2.5 GB of STL for the models of the lists
python3 data/thingi_pairs.py fetch $D --pairs
python3 data/thingi_pairs.py manifest thingi-pairs/pairs.csv $D $D/cases.jsonl
python3 data/thingi_pairs.py manifest thingi-pairs/pairs_size.csv $D $D/cases_size.jsonl

cp -r bin/Release/net10.0 /some/where/frozen; export BOOLCMP_TOOL=/some/where/frozen/BooleanComparison.dll
export MANIFOLD_PYTHON=...    # as above; CGAL_BOOL only for the sample run

# the full run, ours + Manifold (1000 pairs, then the size class; 3 operations each, -j 1 for timings)
python3 compare.py $D/cases.jsonl results-pairs.jsonl --methods ours,manifold --timeout 300 -j 1 --resume
python3 compare.py $D/cases_size.jsonl results-size.jsonl --methods ours,manifold --timeout 900 -j 1 --resume

# the CGAL sample: EPECK on 100 of the 1000 pairs (same case names as in the full run)
python3 data/thingi_pairs.py manifest thingi-pairs/pairs_cgal_sample.csv $D $D/cases_cgal.jsonl
export CGAL_BOOL=/path/to/cgal_bool
python3 compare.py $D/cases_cgal.jsonl results-cgal.jsonl --methods cgal_epeck --timeout 300 -j 1 --resume

python3 analyze.py results-pairs.jsonl results-size.jsonl results-cgal.jsonl
python3 data/thingi_pairs.py summary results-pairs.jsonl results-size.jsonl results-cgal.jsonl
```

One result line is one pair and one method (with the three operations); `--resume` skips the pairs that have a line for every
method of `--methods` (a cut-off last line is ignored, its pair runs again), so an interrupted run loses at most the
pair under way. Pair `NNNN` is the case `pNNNN_<idA>+<idB>`; `--only` runs single ones. `manifest ... --limit N` takes the
first N pairs (the trial below). All three commands are resumable (rerun the same command). The CGAL sample is a separate run and output file, because the full run does not include CGAL; its rows have the same class and case names as the full run's. `--methods` may include `cgal_epick` and `cherchi` as well (not tried here). `analyze.py` tables compare methods on the cases they share, so read the CGAL column on the sample only. The
results of the other tools are not committed (CGAL is GPL; `cgal_bool` stays an external, non-distributed binary).

### Trial run (2026-11-08, not the full run)

The first 20 pairs of `pairs.csv` (60 booleans) and the first 3 of `pairs_size.csv` (9 booleans), tool built from this
branch, 4 cores, `-j 1`, timeout 300 s, `--methods ours,manifold,cgal_epeck` (Manifold 3.x from pip, CGAL 6.0 from the
Ubuntu package, `cgal_bool` built from `external/cgal_bool.cpp`), `python3 data/thingi_pairs.py summary`:

| class | method | pairs | operations | ok | rejected | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | geomean ms | p95 ms | max ms | median result triangles |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | cgal_epeck | 20 | 60 | 60 | 0 | 0 | 49 | 6 | degenerate triangles 5 | 1.20e-14 | 52 | 40 | 268 | 284 | 5417 |
| thingi-pairs | manifold | 20 | 60 | 60 | 0 | 0 | 43 | 6 | degenerate triangles 11 | 1.57e-14 | 29 | 24 | 123 | 174 | 5308 |
| thingi-pairs | ours | 20 | 60 | 60 | 0 | 0 | 54 | 6 | – | 1.13e-11 | 190 | 163 | 746 | 970 | 5373 |
| thingi-pairs-size | cgal_epeck | 3 | 9 | 9 | 0 | 0 | 3 | 0 | degenerate triangles 6 | 4.91e-14 | 1520 | 1385 | 2682 | 2682 | 253792 |
| thingi-pairs-size | manifold | 3 | 9 | 9 | 0 | 0 | 0 | 0 | degenerate triangles 9 | 4.47e-14 | 499 | 364 | 686 | 686 | 253486 |
| thingi-pairs-size | ours | 3 | 9 | 9 | 0 | 0 | 9 | 0 | – | 3.39e-12 | 2608 | 2401 | 4749 | 4749 | 253574 |

Times of `ours` are cold (one process per boolean, as everywhere), the booleans alone. Of 23 pairs, ExactMeshBoolean's results
are all clean or correctly empty; Manifold and CGAL EPECK results contain triangles below 1e-12 diagonal² (the
oracle's degenerate-triangle threshold; their volumes are exact to 1e-14). Six of the 60 operations are empty intersections
(models that do not touch), consistent with the volumes.

## Thingi10K pairs with exact contact

The class `thingi-contact` fills the gap of `thingi-pairs`: that class rotates B randomly, so the operands almost never touch exactly. Here B is
placed against A by a **translation, a power-of-two scale or a reflection, never a rounded transform** (no quaternions), so that
coplanar faces, touching edges and exactly flush parts occur on real models. These are the hard cases of the exact predicates and the main
use case of `ExactMeshImprint`. The models, the clean list and the data rules are those of `thingi-pairs` (models fetched at run time,
`thingi-pairs/clean_models.csv`, no mesh in the repository); the list is `thingi-pairs/contact_pairs.csv` (generated by
`python3 data/thingi_contact.py generate`, SplitMix64 seed 20261116 plus 7919 per variant index; a test checks that the committed file is
the generator's output): 100 pairs per variant from the 2739 clean models with 1k-30k triangles, columns `pair,a,b,variant,axis,sign,param`.
A run takes a prefix of each variant (`--limit N`; the first run: 30).

**Exactness.** The placements are in `ContactTransforms.cs` (no dependency on the tool, unit-tested in
`tests/geometry3Sharp.Tests/ThingiContactTransformTests.cs` on procedural meshes, bit for bit). Both models are first normalized exactly: scaled by
the power of two that gives a longest side in [0.5, 1), moved to the origin up to a grid of 1/32 of that side. Every coordinate sum
(`v + t`, `2c - v`) is checked with TwoSum: if it is not exactly representable the placement is **refused, never rounded** (the case becomes an
`inexact transform` error row and is listed in the summary). The Thingi10K models are single precision STL files, so a double sum with a grid
translation is almost always exact; a model with a coordinate far below the rest (1e-30 next to 1) is refused.

| variant | what B is | oracle |
|---|---|---|
| `face-contact` | B on one side of A with a planar face of B in the plane of a planar face of A, touching only | the volumes are known exactly: V(A∩B) = 0, V(A∪B) = V(A) + V(B), V(A−B) = V(A) (`expected`); plus the identities and `NotCleanReason` |
| `partial-overlap` | as `face-contact`, then moved into A along the contact axis by 2^-d of B's extent (d = `param` in 0, 1, 2; the shift of A's face plane is exact: a power of two times a double). d = 0 is the flush case: B's far face lies in the plane of A's face, both planar and overlapping; d = 1, 2 embed B by a half or a quarter | the identities and `NotCleanReason` |
| `ulp-shift` | A against A translated by one ulp of A's largest coordinate along an axis (about 1e-16 of the model size: near-coincident surfaces, every filter fails) | the identities and `NotCleanReason` |
| `ulp-shift-few` | as `ulp-shift` with 2 to 5 ulps (`param` + 2) | the same |
| `mirror` | A against its mirror image across an axis plane through a vertex coordinate of A (the `param`-th vertex coordinate in order of distance from the box centre; `x' = 2c - x`, the triangle orientation is kept outward): vertices and faces on the plane coincide | the same |

**Which face (`face-contact`, `partial-overlap`).** A face of the bounding box is a point or an edge contact for most models (the extreme is
reached by a vertex or an edge), not the coplanar face contact this class is about. So the contact face is an *actual planar face*: the triangles whose three
vertices have bit-equal coordinates on the extreme plane (flat bottoms and sides of printed parts are exactly that). For each of the six
directions, starting at the one of the pair list, the tool computes the planar extreme face of A on its side and of B on the opposite side (for `partial-overlap` d = 0 the far side of B), aligns the centres of the two meshes
laterally on the grid and takes the first direction with a planar face on both sides whose bounding boxes overlap laterally and an exact placement (`kind` = `face` in
`info.json`; the patches themselves can still miss each other, eg a gap in A's patch under B's patch: the area of contact is not computed; extremes are taken over the vertices that triangles use); a model pair with no such direction is placed through its bounding box (`kind` = `bbox`: an exact contact through vertices or edges, still a
hard case, but not the coplanar one); `partial-overlap` with d = 1, 2 has `kind` `embedded` (no face of B lies in a face plane of A; only d = 0 is coplanar, and the tool checks that B's far plane is bit-equal to A's). The summary column *face contact* counts the pairs with `kind` `face`. The contact plane coordinates of the two faces are bit-equal (a test).

**Oracle.** As in `thingi-pairs` (`eval`): `NotCleanReason` of every result, the volume identities, and `expected_error` for `face-contact`; the inputs
are checked again after the transform (`inputs_clean` in the case). A result that self-intersects is not correct in this class
(`analyze.py`, `thingi-contact` has the tolerance 1e-7 and `NO_SELFINT`). Two limits of the oracle: touching solids merged at a single vertex or edge (`kind` `bbox`)
give a union with a non-manifold vertex or edge, which `NotCleanReason` reports (`bowties`, `non-manifold`) although the result is correct; and a shell
one ulp thick has a volume far below the tolerance, so a method that drops it passes the identities. For `ours` the rounding stage
(`SafeRounding`, on by default; owner decision 2026-11-16: the default stays, no new option, `SafeRounding = false` and
`ComputeExactVertices` give such a shell to callers who need it) drops pieces that cannot survive the rounding to doubles
(`ExactMeshBoolean.Rounding.RemovedComponents`). Each result of `ours` therefore records `removed_components` and `removed_triangles`
(`<op>_removed_components` in the raw rows, from the `counts` of `BooleanComparison run`), and the summary tells an empty result that the
rounding produced by removing components (`empty, removed by rounding`) from a really empty one (`empty (volume-consistent)`, nothing
removed). Other methods have no such field (shown as `–`); the first run's rows have the number in `<op>_counts` only, and `summary` reads both.

**Running** (from `benchmarks/BooleanComparison`; the tool and `cgal_bool` as for `thingi-pairs`):

```
D=/path/outside/the/repo/thingi-contact
python3 data/thingi_contact.py fetch $D --limit 30
python3 data/thingi_contact.py manifest $D $D/cases.jsonl --limit 30                  # --variants face-contact,mirror: some variants only
python3 compare.py $D/cases.jsonl results-contact.jsonl --methods ours,manifold,cgal_epeck --timeout 300 -j 1 --resume
python3 data/thingi_contact.py summary results-contact.jsonl                           # per variant and method
```

On GitHub: the workflow `thingi-contact.yml` (`workflow_dispatch` only, no schedule; one job per variant; Manifold from `pip install
manifold3d`, CGAL EPECK as a black box built from the runner's `libcgal-dev`, GitHub-hosted runners only) fetches the models, runs the
three methods and writes the table to the step summary; the raw rows go to the data branch `runs/thingi-contact`.

ExactMeshImprint is not a method of this class yet. TODO: add it as a method once PR #981 is merged.

**First run** (GitHub run [43949164518](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/43949164518), 2026-11-16, master d5fb874a, `workflow_dispatch`, ubuntu-latest with 4 cores, `-j 1`, timeout 300 s; CGAL 6.1.1 from apt as `cgal_bool` (EPECK), manifold3d 3.0.1; the first 30 pairs of each variant; raw rows on the data branch `runs/thingi-contact`, folder `run-43949164518`):

| variant | method | pairs | face contact | operations | ok | failed / crash / timeout | clean | empty (volume-consistent) | empty, removed by rounding | unclean | max volume error | median ms | max ms | removed components |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| face-contact | cgal_epeck | 22 | 16 | 66 | 64 | 2 | 39 | 20 | 0 | bowties 1, not evaluated 4 | 1.19e-14 | 24 | 84 | – |
| face-contact | manifold | 22 | 16 | 66 | 66 | 0 | 41 | 19 | 0 | empty (inconsistent) 3, not closed 2, self-intersections 1 | 5.46e-14 | 24 | 48 | – |
| face-contact | ours | 22 | 16 | 66 | 66 | 0 | 41 | 20 | 0 | bowties 1, empty (inconsistent) 2, not closed 2 | 1.19e-14 | 112 | 335 | 0 |
| partial-overlap | cgal_epeck | 24 | 5 | 72 | 72 | 0 | 61 | 4 | 0 | bowties 1, degenerate triangles 6 | 1.45e-14 | 24 | 385 | – |
| partial-overlap | manifold | 24 | 5 | 72 | 72 | 0 | 41 | 4 | 0 | degenerate triangles 27 | 2.31e-14 | 29 | 48 | – |
| partial-overlap | ours | 24 | 5 | 72 | 72 | 0 | 67 | 4 | 0 | bowties 1 | 9.61e-13 | 109 | 394 | 0 |
| ulp-shift | cgal_epeck | 30 | 0 | 90 | 84 | 6 | 0 | 0 | 0 | degenerate triangles 10, not closed 74 | 1.12e-14 | 858 | 3054 | – |
| ulp-shift | manifold | 30 | 0 | 90 | 90 | 0 | 67 | 4 | 0 | bowties 1, degenerate triangles 2, inside-out components 3, not closed 13 | 2.38e-13 | 45 | 179 | – |
| ulp-shift | ours | 30 | 0 | 90 | 90 | 0 | 59 | 0 | 30 | degenerate triangles 1 | 2.59e-09 | 798 | 3435 | 1440 |
| ulp-shift-few | cgal_epeck | 30 | 0 | 90 | 87 | 3 | 0 | 0 | 0 | degenerate triangles 14, not closed 73 | 1.46e-14 | 625 | 93228 | – |
| ulp-shift-few | manifold | 30 | 0 | 90 | 90 | 0 | 63 | 5 | 0 | bowties 1, degenerate triangles 4, inside-out components 1, not closed 15, self-intersections 1 | 5.27e-14 | 74 | 221 | – |
| ulp-shift-few | ours | 30 | 0 | 90 | 90 | 0 | 60 | 0 | 30 | – | 4.97e-08 | 792 | 10702 | 689 |
| mirror | cgal_epeck | 30 | 0 | 90 | 81 | 9 | 21 | 0 | 0 | bowties 15, degenerate triangles 45 | 2.14e-13 | 295 | 4416 | – |
| mirror | manifold | 30 | 0 | 90 | 90 | 0 | 30 | 0 | 0 | bowties 16, degenerate triangles 34, not closed 9, self-intersections 1 | 1.51e-14 | 53 | 245 | – |
| mirror | ours | 30 | 0 | 90 | 90 | 0 | 66 | 0 | 0 | bowties 15, not closed 9 | 9.38e-06 | 432 | 3216 | 284 |

`pairs` are the placements that were accepted (of 30 per variant), `face contact` those with `kind` `face`, the other columns count operations as in `thingi-pairs`; `empty, removed by rounding` and `removed components` are explained in the oracle note (ours only). Times are the boolean alone, from one run on a shared runner (indicative only, no repeats).

Placements refused as inexact or failed to load: 14 of 150 cases:
- face-contact_p0000_88616+92837: RuntimeError('inexact transform: contact shift not exact')
- face-contact_p0002_37282+985160: RuntimeError('inexact transform: translation not exact')
- face-contact_p0008_136634+84320: RuntimeError('inexact transform: translation not exact')
- face-contact_p0013_925244+65149: RuntimeError('inexact transform: contact shift not exact')
- face-contact_p0022_100339+40051: RuntimeError('inexact transform: translation not exact')
- face-contact_p0025_47748+985160: RuntimeError('inexact transform: translation not exact')
- face-contact_p0028_113862+59228: RuntimeError('inexact transform: translation not exact')
- face-contact_p0029_242305+178193: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0001_91452+73413: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0002_121868+200963: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0009_107439+514852: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0011_92836+49546: RuntimeError('inexact transform: embedding depth not exact')
- partial-overlap_p0023_44397+54467: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0028_472179+76057: RuntimeError('inexact transform: translation not exact')

What the run shows (all of it from the raw rows; causes marked *not diagnosed* were not investigated):
- **Refused placements: 14 of 150, all in the two variants that move B by a general shift** (8 of 30 `face-contact`, 6 of 30 `partial-overlap`); `ulp-shift`, `ulp-shift-few` and `mirror` had none. Probable cause (*not verified on the models*): the shift is of order 1, and a model with a coordinate that is nearly but not exactly zero (single precision noise, about 1e-8) has a finer last bit than a sum of that size can keep. That is the exactness rule working (the case is refused, not rounded), but it costs 27 % of the pairs of these two variants. A follow-up can pre-select the models whose coordinates survive the placement.
- **`face-contact`: 16 of 22 accepted pairs have planar faces on both sides** (`kind` `face`), 6 touch through vertices or edges (`bbox`). `partial-overlap`: 5 of 24 `face` (flush), the others `embedded` or `bbox`. In 4 of 24 `partial-overlap` pairs the intersection is empty although B was moved into A (no volume under B's footprint), which the oracle accepts as volume-consistent.
- **ours, `face-contact`**: the union of 2 pairs is `not closed` (p0012, p0021; both `kind` `face`); manifold has 2 `not closed` results too (which cases was not compared). Volumes are right (expected_error 2.4e-15). Not diagnosed; issue filed.
- **ours, `mirror`**: the difference A - mirror(A) is `not closed` for 8 of 30 pairs and `bowties` for 15 (bowties are plausible for pieces that meet on the mirror plane); the largest identity error is 9.4e-06 (p0024) and 5.2e-07 (p0001), above the class tolerance 1e-7. Issue filed (pair ids and parameters in it).
- **ours, `ulp-shift` / `ulp-shift-few`**: the difference A - A' (a shell one ulp thick) is empty for all 30 pairs of both variants. All 60 of these empty results are `empty, removed by rounding` (1 to 733 removed components each; 1440 and 689 removed components over all operations of the two variants), none is really empty; the exact result is a shell of volume about 1e-16 of the model, which the identities cannot see. Intended behaviour of `SafeRounding` (owner decision 2026-11-16, see the oracle note above), not a defect. Identity errors up to 5.0e-08 (`ulp-shift-few`). In contrast the 26 empty intersections of `face-contact` and `partial-overlap` removed nothing: they are really empty.
- **CGAL EPECK** failed or timed out on 18 operations (`ulp-shift` 6, `ulp-shift-few` 3, `mirror` 9; one `ulp-shift-few` case took 93 s) and its results are mostly `not closed` under our weld-by-position check for `ulp-shift` (74 of 90) and `ulp-shift-few` (73 of 90): not analysed whether that is CGAL's output after the conversion to double or the check. **manifold** is `not closed` or `self-intersections` in a few cases per variant, and `degenerate triangles` in 27 of 72 `partial-overlap` results.
- Summary rows for results that `eval` could not read back were crashing `summary` (a result without a clean reason); fixed here (`not evaluated`), and the workflow's report steps now fail on a summary error (`set -o pipefail`; the run above shows an empty table in the step summary for that reason, the table here is computed from the raw rows).

## Open Boolean Benchmark runner

The [Open Boolean Benchmark](https://github.com/Open-Boolean-Benchmark/boolean-benchmark-runners) compares
mesh booleans of about a dozen libraries through small runner programs with a common protocol (a request JSON
with SSA-style operation lists, a result JSON with per-operation status and times). `BooleanComparison obb`
implements this protocol for ExactMeshBoolean:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll obb --request request.json --result result.json
```

It passes the smoke test of the runners repository (`tests/smoke`, cube minus cube). `obb-runner/` contains a
`runner.yaml` and `build.py` in the layout of that repository; copied there as `exactmeshboolean/<version>/`
(with `upstream.ref` set to a fixed commit), the benchmark's meta-runner can run ExactMeshBoolean next to the
other libraries. The meta-runner and the benchmark cases (Nefertiti, dental scans, the iterated cube grid) are
not published, so this comparison cannot be run here.

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
export BOOLCMP_SAFE_ROUNDING=1       # optional: ExactMeshBoolean.SafeRounding on (1) or off (0); unset: the library's default

python3 compare.py meshes.txt results.jsonl --timeout 300 -j 1        # a list of mesh files or a directory (class thingi10k)
python3 data/fetch.py cgal data_cgal && python3 compare.py data_cgal/cases.jsonl results.jsonl
python3 data/fetch.py cherchi data_cherchi && python3 compare.py data_cherchi/cases.jsonl results.jsonl
dotnet $BOOLCMP_TOOL synth synth && python3 compare.py synth/cases.jsonl results.jsonl --timeout 900
python3 analyze.py results.jsonl                                      # Markdown tables per data class
```

Use `-j 1` for timings: several methods are multi-threaded. `--timeout` is per boolean, or per sequence.
`--only name,...` runs single cases of a manifest.

Each run gets a status: `ok`, `rejected` (the tool refuses the input), `failed` (the tool reports a failure on an
input it accepts), `crash` (signal, exception or exit code without a report) or `timeout`. `analyze.py` combines
these with the evaluation into one outcome per case and method (see the comment at its top), per data class, on
the cases that every method accepts, and gives the time on the cases that every method solves correctly.

A single run of our method, with phase times, counts and GC statistics:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll export mesh.stl dir      # writes dir/A.off, dir/B.off
dotnet bin/Release/net10.0/BooleanComparison.dll run dir/A.off dir/B.off union out.off
```

A profile of our method over many cases (phase times and allocations, GC, counts, a hash of each result for
before/after comparisons; see [Profile and performance work](#profile-and-performance-work-october-2026)):

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll profile --repeat 5 cases.txt grid:10   # cases.txt: one case directory per line
dotnet build -c Release -p:G3PredicateStats=true     # a build that also counts the filter failures of the predicates
```

## Results per data class

Run on 4 cores, `-j 1`, timeout 300 s per boolean (900 s per sequence); ExactMeshBoolean as of the branch of #33
(master with the faster exact booleans). The times of ExactMeshBoolean below are from before the performance work
of October 2026 (see [Profile and performance work](#profile-and-performance-work-october-2026)), which made a
boolean measured the same way (one process per boolean, as `compare.py` runs it) 2.4-2.8 times faster on the
`cgal` and Thingi10K inputs and 1.3-2.3 times faster on the `cherchi` inputs; the other methods were not rerun. Outcomes per case and method over all its operations, as defined at
the top of `analyze.py`: **clean** (correct, closed manifold, no self-intersections), **self-int.** (correct,
but self-intersections after rounding to double or where solids touch), **volume** (closed, but a volume
identity or a known volume off by more than 1e-6), **not closed**, **failed** (the tool reports a failure on an
input it accepts), **timeout**, **crash**, **rejected** (the tool refuses the input). Times are the sum of the
three operations (boolean only, without I/O), on the cases that every method solves correctly.

### Re-run (28 October 2026)

Our method only (the external tools were not rebuilt), master `e90a034`, `compare.py --methods ours -j 1`, with
`SafeRounding` off (the library default at `e90a034`) and on (`BOOLCMP_SAFE_ROUNDING=0|1`), both runs at the same time on 4
cores (times under load; the sequences alone, see ⁵). Outcomes per case as defined above:

| class | SafeRounding off | on | time (sum of the booleans) off → on |
|---|---|---|---|
| `cgal` (90) | 63 clean, 3 self-int., 24 not closed¹ | 65 clean, 1 self-int., 24 not closed¹ | 107 → 148 s (+39 %) |
| `cherchi` (13) | 13 clean | 13 clean | 67 → 95 s (+42 %) |
| `synthetic` (24) | 23 clean, 1 self-int.³ | 23 clean, 1 self-int.³ | 5.4 → 8.6 s (+59 %) |
| `sequence` (2) | 2 clean | 2 clean | grid 5 / grid 10: 3.4 / 106 s → 4.9 / 168 s (both measured alone, ⁵) |

Against the table below (before the performance work and `SimplifyFlatRegions`): `cgal` one more case clean (62 → 63)
and one fewer not closed; the 1999-boolean sequence finishes. Of the time with SafeRounding, the repair after the rounding
is 29 % on `cgal` and 30 % on `cherchi` (on a result without defects it is the exact check of the rounded result for
self-intersections and degenerate triangles).

### Summary

| class | cases | where ExactMeshBoolean stands |
|---|---|---|
| `cgal` | 90 | as good as CGAL and Manifold on the 50 inputs all accept; the only method without rejections (CGAL refuses 40, Manifold 33); 9× slower than CGAL, 11× slower than Manifold (median per case) |
| `cherchi` | 13 | all methods clean; 5× slower than CGAL, 10× slower than Manifold, 3× slower than Cherchi et al. (median per case; on the largest cases 2–3× slower than CGAL and Cherchi et al.) |
| `synthetic` | 24 | clean everywhere except where the correct result touches itself (as Manifold); CGAL refuses the union of boxes touching at an edge; Cherchi et al. leaves coplanar and touching cases open |
| `sequence` | 2 | 249 and 1999 booleans clean (3.4 s and 106 s, re-run 28 October; Manifold 0.2 s and 7.5 s); before `SimplifyFlatRegions` the 1999 booleans timed out (see below) |

### `cgal` (90 cases, 50 with closed inputs without self-intersections)

| method | clean | self-int. | volume | not closed | rejected |
|---|---|---|---|---|---|
| ExactMeshBoolean | 62 | 3 | – | 25¹ | – |
| CGAL EPECK | 47 | 3 | – | – | 40 |
| CGAL EPICK | 47 | 3 | – | – | 40 |
| Manifold | 49 | 4 | 4 | – | 33 |
| Cherchi et al. | 27 | 25 | 3 | 35 | – |

On the 50 cases that every method accepts:

| method | clean | self-int. | not closed |
|---|---|---|---|
| ExactMeshBoolean | 48 | 2² | – |
| CGAL EPECK / EPICK | 47 | 3 | – |
| Manifold | 49 | 1 | – |
| Cherchi et al. | 27 | 22 | 1 |

Time on the 49 cases that every method solves correctly: ExactMeshBoolean median 532 ms (total 49 s), CGAL EPECK
62 ms (9.0 s), EPICK 68 ms (8.8 s), Cherchi et al. 113 ms (19 s), Manifold 41 ms (4.4 s).

¹ open inputs: by design (generalized winding number). ² `cheese` and `u_arch`: the exact methods (ours, CGAL
EPECK, Cherchi et al.) all leave a few self-intersections after rounding the new vertices to double; Manifold's
results are clean.

### `cherchi` (13 cases, all inputs closed without self-intersections)

All five methods give clean results on all 13 cases. Time: ExactMeshBoolean median 3631 ms (total 58 s), CGAL
EPECK 645 ms (16 s), EPICK 682 ms (16 s), Cherchi et al. 1363 ms (23 s), Manifold 304 ms (5.9 s). The gap shrinks
with size: bunny 200k + cow 200k takes us 6.8 s (CGAL EPECK 2.2 s, Cherchi et al. 3.0 s, Manifold 0.9 s), the
stencil (fertility minus 703 spheres) 15 s (CGAL EPECK 6.3 s, Cherchi et al. 6.4 s, Manifold 1.1 s).

### `synthetic` (24 cases)

| method | clean | self-int. | volume | not closed | failed |
|---|---|---|---|---|---|
| ExactMeshBoolean | 23 | 1 | – | – | – |
| CGAL EPECK / EPICK | 23 | – | – | – | 1 |
| Manifold | 23 | 1 | – | – | – |
| Cherchi et al. | 10 | 2 | 2 | 10 | – |

| case | ExactMeshBoolean | CGAL EPECK / EPICK | Manifold | Cherchi et al. |
|---|---|---|---|---|
| coplanar faces (5 cases: shifted, half box, rotated 45°, meshed faces) | clean | clean | clean | **not closed** |
| identical box, identical sphere | clean | clean | clean | **volume** |
| touching at a face (full, partial), nested touching a face | clean | clean | clean | **not closed** |
| touching at an edge | self-int.³ | failed (union: "output not manifold") | self-int.³ | self-int. |
| touching at a vertex | clean | clean | clean | clean |
| UTM buildings (axis-aligned, rotated, same ground plane) | clean | clean | clean | **not closed** |
| sphere and its rotated copy (same centre) | clean | clean | clean | self-int. |
| overlap / gap of 1e-9, nearly coplanar (1e-6°), sliver (0.01°), UTM spheres, coordinates 1e8, scale 1e3 vs 1e-3, scale 1e-6, generic spheres | clean | clean | clean | clean |

³ the union of two boxes touching at an edge is non-manifold there; welded by position, the two boxes touch.

### `sequence` (iterated cube grids)

| case | ExactMeshBoolean | CGAL EPECK | CGAL EPICK | Manifold | Cherchi et al. |
|---|---|---|---|---|---|
| 5×5×5, 249 booleans | clean, 3.4 s⁵ | self-int.⁴, 2.1 s | failed after 5 | clean, 0.2 s | crash after 7 |
| 10×10×10, 1999 booleans | clean, 106 s⁵ (before: **timeout** after 47) | self-int.⁴, 64 s | failed after 10 | clean, 7.5 s | crash after 12 |

EPICK throws "Unauthorized intersections of constraints" on the coplanar faces; Cherchi et al. crash (SIGSEGV)
on intermediate results passed as files. ⁴ EPECK stays exact in memory, but its final result, rounded to double,
has duplicated points, triangles of zero area (432 for n = 10) and self-intersections.

⁵ Re-run of 28 October 2026 (master `e90a034`, our method only, alone on the machine): both sequences clean, 2472
and 16332 triangles (the same as the n-ary one pass below), exact volume. With `SafeRounding` (`BOOLCMP_SAFE_ROUNDING=1`)
the same results in 4.9 s and 168 s.

Before `SimplifyFlatRegions` (now on by default), ExactMeshBoolean fragmented coplanar regions: where faces of the
two inputs overlap in a plane, the result kept the subdivision of both (eg points where the diagonals of the
overlapping side faces cross), and after rounding to double these points lay slightly off the lines they were on. The next boolean intersects them again, so the
results grow geometrically (after 29, 33, 37, 41 unions of the 10×10×10 grid: 1364, 2342, 6002, 21936
triangles; 28 s for the 46th boolean). The results stayed correct (closed, exact volume). `ExactPlanarSimplify`
(`SimplifyFlatRegions`) now removes such vertices exactly and retriangulates the regions, so the triangle count stays
bounded and the 1999 booleans finish.

### Many meshes in one operation

`ExactMeshBoolean` takes any number of meshes into one arrangement, with an operation or a predicate on their
winding numbers (n-ary union / intersection / difference, "inside at least k of n", ...). `BooleanComparison nary`
compares this with the chain of pairwise booleans:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll nary grid 10                     # chained, onepass, twostep
dotnet bin/Release/net10.0/BooleanComparison.dll nary spheres 100 chained onepass
```

`grid n` is the `sequence` case: n³ cubes united, minus a void in each cube. `onepass` is one ExactMeshBoolean
over all 2n³ meshes with the predicate "inside a cube and in no void", `twostep` the n-ary union of the cubes and
then the n-ary difference of that union and all voids. `spheres k` is the union of k random overlapping spheres
(1536 triangles each). Before timing, the requested variants run untimed on a small case of the same kind for
3 s (warm-up, see below). Run on 4 cores, booleans only (without building the inputs):

| case | inputs | chained (pairwise booleans) | one pass | two steps | result |
|---|---|---|---|---|---|
| grid 3 | 54 | 0.56 s (53) | 0.14 s | 0.21 s | identical: 624 triangles, exact volume, clean |
| grid 5 | 250 | 3.2 s (249) | 1.3 s | 1.5 s | identical: 2472 triangles, exact volume, clean |
| grid 10 | 2000 | 124 s (1999) | 15.3 s | 15.8 s | identical: 16332 triangles, exact volume, clean |
| spheres 20 | 20 | 1.8 s (19) | 0.73 s | – | same volume, clean; 30576 instead of 30864 triangles |
| spheres 100 | 100 | 31 s (99) | 5.7 s | – | same volume, clean; 110934 instead of 113774 triangles |

One pass is 2.5–8× faster: every input triangle is intersected and retriangulated once, instead of the growing
intermediate result again in every step. Only meshes whose boxes overlap are intersected, and a patch gets a
winding number ray only for the meshes whose box contains it, so far-apart inputs cost little. The one-pass
result rounds the new vertices once; the chain keeps rounded vertices of intermediate results (more triangles on
curved inputs). For grid 10 the time goes to the merge of intersection points (6.3 s), the classification of
42048 patches (4.0 s), the triangulation (2.6 s) and the intersections (2.3 s).

**Warm-up.** A variant run alone in a cold process used to take much longer than in the table: grid 5 one pass
2.8–3.6 s alone, 1.6 s after the chained variant. This was not a regression of the library: the PR head of the
n-ary booleans (24fc5cd), its merge (edb069f) and master at 7f0f97c all take 2.8–3.6 s cold and 1.5–2.0 s in the
second or third run in the same process. The difference is mostly JIT compilation (tiered: optimized code only
after enough calls; `DOTNET_TieredCompilation=0`: 2.1 s cold), partly growth of the GC heap (server GC,
`DOTNET_gcServer=1`, which the benchmark does not use: 1.0–1.4 s warm). The earlier table was measured with all
variants in one process, so one pass came after the chained run. Now every run warms up on a small case first
(this covers the JIT, the heap only partly); grid 5 one pass alone: 1.6–1.9 s (before: 2.8–3.6 s).

## Results (80 Thingi10K meshes)

Each mesh A with a rotated, shifted copy B; union, intersection and difference. Outcomes per mesh and method
(all three operations): **rejected** (the tool refuses the input: CGAL needs closed polygon meshes without
self-intersections, Manifold manifold ones), **crash**, **timeout** (300 s per operation), **not closed**,
**volume** (closed, but a volume identity is off by more than 1e-6), **self-int.** (correct, but the output has
self-intersections after rounding to double), **clean** (correct, closed manifold, no self-intersections).

Inputs that all methods accept (38: closed, manifold, no self-intersections):

| method | clean | self-int. | other |
|---|---|---|---|
| ExactMeshBoolean | 38 | 0 | – |
| CGAL EPECK (exact constructions) | 38 | 0 | – |
| CGAL EPICK (inexact constructions) | 38 | 0 | – |
| Manifold | 38 | 0 | – |
| Cherchi et al. | 3 | 35 | – |

On the common inputs ExactMeshBoolean is as good as CGAL and Manifold, not better: every result of all four is
clean (EPECK and EPICK alike; there is no EPECK failure to trace back to the rounding of its output). The
difference is coverage, on the inputs that other tools refuse:

| inputs | method | clean | self-int. | volume | not closed | crash / timeout | rejected |
|---|---|---|---|---|---|---|---|
| closed, self-intersecting or non-manifold (20) | ExactMeshBoolean | 8 | 9 | 3¹ | – | – | – |
| | CGAL EPECK / EPICK | – | – | – | – | – | 20 |
| | Manifold | 4 | 6 | 7 | – | – | 3 |
| | Cherchi et al. | – | 4 | – | 6 | 10 | – |
| open (22) | ExactMeshBoolean | 1 | 2 | – | 19² | – | – |
| | CGAL EPECK / EPICK | – | – | – | – | – | 22 |
| | Manifold | – | – | – | – | – | 22 |
| | Cherchi et al. | – | – | – | 12 | 10 | – |

¹ inputs whose solid has (almost) no volume (flat or sheet-like, volume 0 or 1e-12): the relative volume error
is meaningless there; the results are closed. ² by design: open inputs use the generalized winding number,
whose solid is in general not bounded by a closed surface.

Self-intersections in our results on self-intersecting inputs come mostly from rounding the new vertices to
double and from solids that touch (the touching sheets are kept apart only by duplicated vertices); see
`MeshRobustRepair.SeparateTouchingVertices` for how the repair handles the latter.

Time (union + intersection + difference, boolean only, median on the 38 common inputs): ExactMeshBoolean 994 ms,
Cherchi et al. 264 ms, CGAL EPICK 110 ms, EPECK 110 ms, Manifold 57 ms. ExactMeshBoolean, Cherchi and Manifold
use all cores, CGAL one.

## Profile and performance work (October 2026)

The state, everything tried and the open candidates, for continuing this work:
[docs/perf/EXACT_BOOLEAN_PERFORMANCE.md](../../docs/perf/EXACT_BOOLEAN_PERFORMANCE.md); the scripts to measure:
[perf/](perf/).

`BooleanComparison profile` (`Profile.cs`) runs ExactMeshBoolean on a list of cases and writes one JSON line per
case and operation: the time of each run, the phase times and the bytes allocated per phase (`PhaseClock`), GC
collections and pause time, the counts of the boolean, and a hash of the result mesh (with `--exact` also of the
exact result vertices). A library built with `-p:G3PredicateStats=true` also counts, per predicate, the calls and
how often its floating-point filter does not decide (`PredicateStats`; in a normal build the counting calls are
removed by the compiler). Inputs:

- `cherchi`: the 13 cases of the class;
- `cgal`: the 50 cases with closed inputs without self-intersections;
- Thingi10K: the 32 of the 40 clean controls of `MeshRepairBenchmark`'s selection (`fetch_data.py`) whose
  exported pair is closed and free of self-intersections (each with a rotated, shifted copy, as `export` writes it);
- n-ary: `grid:5`, `grid:10` (one pass) and `spheres:20` of `nary`.

All on 4 cores, union, intersection and difference. **Warm**: 5 runs in one process after a warm-up, the fastest.
**Cold**: one process per boolean, as `compare.py` runs it (`run`: a short warm-up on two small spheres, then the
boolean). Times per case are the sums of the three operations.

### Where the time went (master, warm)

| class | runs | median / case | setup | trees | pair search | planes | tri-tri | merge | triangulation | classification | assembly | (simplify) | GC pause | alloc / run |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| cherchi | 39 | 1208 ms | 8 % | 7 % | 21 % | 6 % | 3 % | 4 % | 14 % | 10 % | 26 % | 7 % | 34 % | 467 MB |
| cgal | 150 | 67 ms | 4 % | 6 % | 22 % | 3 % | 5 % | 12 % | 11 % | 9 % | 22 % | 5 % | 15 % | 61 MB |
| thingi | 96 | 120 ms | 3 % | 4 % | 21 % | 6 % | 10 % | 16 % | 13 % | 8 % | 15 % | 4 % | 23 % | 91 MB |
| n-ary | 3 | | 0 % | 0 % | 1 % | 0 % | 12 % | 41 % | 20 % | 25 % | 1 % | 0 % | 25 % | 5.3 GB |

Phases as shares of the total time (they include the GC pauses that fall into them; simplify is part of assembly).
Pair search: the box tree queries and the exact tests that discard pairs of triangles that cannot intersect (most
of them neighbours in the same mesh: self-intersections are computed too); tri-tri: the intersection of the
remaining pairs; merge: the merge of coincident points and the crossings of segments in a triangle.

- **No phase dominates on the real-world inputs.** The pair search is the largest single phase (21-22 %), then the
  assembly of the result mesh (15-26 %). The exact arithmetic is not the bottleneck there: most predicates are
  decided by their filters (below).
- **GC pauses take 15-34 % of the time**, most on the large inputs (467 MB allocated per boolean of `cherchi`).
- **The n-ary grid is dominated by exact arithmetic**: coincident points from different constructions (merge 41 %),
  exactly cocircular points in the triangulation (74 % of its incircle tests need the exact stage), and the ray
  tests of the 42048 patches. Grid 10: 13.7 s, of which merge 5.8 s, classification 3.3 s, triangulation 2.6 s.
- **JIT.** A cold boolean took 3-9 times as long as a warm one (`cgal` median 602 ms cold, 67 ms warm). With tiered
  compilation, methods with loops start as unoptimized code and are replaced only after many iterations (on-stack
  replacement), and the optimized versions arrive after a call-counting delay that keeps being extended while new
  methods are compiled. `DOTNET_TieredCompilation=0` made a cold boolean of `cgal/elephant` 3.3 times faster
  (367 -> 114 ms), `DOTNET_TC_QuickJitForLoops=0` almost as much.

Filter failures per predicate (master; calls over all runs of the class, and the share that needed the exact stage):

| predicate | cherchi | cgal | thingi | n-ary |
|---|---|---|---|---|
| `ExactPredicates.Orient3d` (double input: pair search, tri-tri) | 120 M, 0.0 % | 67 M, 11.6 % | 85 M, 8.2 % | 8.4 M, 31 % |
| `ExactPredicates.Orient2d` (degenerate triangles, coplanar tests) | 11 M, 0.3 % | 39 M, 10.6 % | 34 M, 7.2 % | 0.2 M, 27 % |
| arrangement `Orient` (implicit points) | 3.9 M, 11.8 % | 2.1 M, 11.1 % | 2.4 M, 10.0 % | 18.5 M, 20.8 % |
| arrangement `InCircle` | 5.0 M, 0.0 % | 2.6 M, 0.2 % | 2.8 M, 0.0 % | 1.9 M, 74.1 % |
| arrangement `compare_along` | 3.7 M, 0.0 % | 2.5 M, 0.0 % | 4.7 M, 0.0 % | 5.7 M, 13.6 % |
| arrangement `ComparePoints` (order of points, merge) | 1.6 M, 0.4 % | 1.6 M, 29.6 % | 3.0 M, 43.9 % | 25 M, 21.9 % |
| winding ray: plane side | 0.18 M, 0.0 % | 0.04 M, 1.5 % | 0.08 M, 3.2 % | 0.73 M, 22.2 % |
| winding ray: `orient3d` | 0.24 M, 0.0 % | 0.04 M, 0.0 % | 0.09 M, 0.0 % | 0.67 M, 1.5 % |

The exact stages are frequent where the input has exact coplanarity (CAD models in `cgal`, many Thingi10K models:
flat regions, axis-aligned faces, the copies' faces) and where coincident points are compared (`ComparePoints` in
the merge). The 11-12 % of the arrangement's `Orient` are mostly exactly collinear points (points on the edges of
the triangle they split).

Parallel scaling (master, warm, sums over the cases; `taskset` and `DOTNET_PROCESSOR_COUNT`):

| cases | 1 core | 2 cores | 4 cores | 4 / 1 |
|---|---|---|---|---|
| 5 smaller `cherchi` cases | 8.2 s | 5.5 s | 4.7 s | 1.75 |
| 12 Thingi10K cases | 8.9 s | 6.3 s | 4.9 s | 1.82 |
| grid 5 | 2.3 s | 1.7 s | 1.3 s | 1.74 |
| bunny 200k + cow 200k | 7.0 s | 5.3 s | 4.5 s | 1.55 |

The serial parts (classification, assembly, building the points) and the GC pauses limit the scaling.

### What changed

All changes keep the results bit-identical: the same triangles, vertex coordinates and ids, the same exact vertices,
the same status, on every case above (hashes of the result mesh, and with `--exact` of the exact vertices, compared
with master: 0 differences over all warm runs and 263 exact runs), on 1, 2 and 4 cores, and in repeated runs.

- **JIT** (`core/JitHints.cs`): the methods of the boolean pipeline (and the append path of `DMesh3`) are marked
  `AggressiveOptimization`, so they are compiled optimized at their first call. This is the largest gain for single
  booleans; it does not change the generated arithmetic (no contraction to FMA).
- **Merge of coincident points**: the points of a cluster are grouped by their exact (canonical) point instead of
  sorted with `ComparePoints`, so each exact point is computed once, not in every comparison.
- **Exact stages of the arrangement's `Orient` and `InCircle`**: integer points whose coordinates are doubles go
  through the adaptive double predicates (`ExactPredicates`); `InCircle` is a 3x3 integer determinant of differences
  instead of a 4x4 one in a `BigInteger[,]` (the derivation is in the code).
- **Pair search**: the box tree on arrays, queries into a reused list (no closure, stack or delegate per query), the
  trees of the meshes built in parallel, `Parallel.For` over index ranges instead of `Parallel.ForEach` over an
  enumerable (`gParallel.ForRange`).
- **Setup**: input triangles built in parallel; the degenerate test starts with the projection along the largest
  normal component (the others are degenerate for triangles in coordinate planes and needed the exact stage).
- **Classification**: the winding numbers of the patches are computed in parallel, each patch with a random number
  generator of its own (the winding numbers are exact; the rays only change the diagnostic counts
  `winding_plane_sides`); the stacks of coplanar pieces in parallel.
- **Assembly**: arrays per point id instead of dictionaries and hash sets; the incidence lists of
  `ExactPlanarSimplify` as arrays, lists only for the points that change; no checks before `AppendTriangle` that
  cannot fail (no edge with more than two pieces and no conflicting pieces); `DMesh3.FindEdge` without an iterator.
- **Allocations**: planes of the input triangles in an object created only for the triangles that need them,
  directions of segment lines rounded to double once, smaller point index shards; the ids of `DMesh3` (and
  `NTMesh3`, `DGraph*`) enumerated without boxing each id (`RefCountVector` had only a non-generic enumerator);
  presized lists; `ExactTriangulation2` reuses the arrays of removed triangles.
- **Unoptimized runtime code**: the edges are grouped with a radix sort of our own (stable) instead of
  `Array.Sort(long[], int[])`, whose generic code took about 14 % of a cold boolean's main thread; loop bodies of the
  parallel phases are methods with the JIT hint instead of lambdas.
- **Coplanar triangles share their constructions**: triangles in the same plane (exactly; candidates found by a
  rounded key of the plane's approximation) get one plane key, so an edge crossing that plane is one implicit point,
  not one per triangle. In grid 10, 95 % of the points were such duplicates: 656k points -> 143k, merge 2.6 -> 1.0 s.

### After (warm)

| class | median / case before | after | speedup (median of cases) | total before | after | GC pause | alloc / run |
|---|---|---|---|---|---|---|---|
| cherchi | 1208 ms | 685 ms | 1.61 | 32.1 s | 22.0 s | 34 % -> 28 % | 467 -> 322 MB |
| cgal | 67 ms | 56 ms | 1.21 | 12.5 s | 9.3 s | 15 % -> 9 % | 61 -> 41 MB |
| thingi | 120 ms | 106 ms | 1.20 | 12.4 s | 8.9 s | 23 % -> 9 % | 91 -> 62 MB |

| n-ary case | before | after | merge | triangulation | classification |
|---|---|---|---|---|---|
| grid 5 | 1278 ms | 746 ms | 365 -> 139 ms | 464 -> 258 ms | 305 -> 250 ms |
| grid 10 | 13.7 s | 5.7 s | 5.8 -> 1.0 s | 2.6 -> 1.7 s | 3.3 -> 1.9 s |
| spheres 20 | 531 ms | 375 ms | 140 -> 21 ms | 57 -> 161 ms | 167 -> 36 ms |

(The class rows are from the state before the shared constructions of coplanar triangles, which changes nothing
measurable outside the n-ary grid: on 17 cases of the three classes 3.96 / 3.97 s before, 3.97 / 3.82 s after. The
n-ary rows include it. spheres 20: the GC pauses fall into different phases; the total went down.)

### After (cold, one process per boolean)

| class | median / case before | after | speedup (median of cases) | total before | after |
|---|---|---|---|---|---|
| cgal (50 cases) | 602 ms | 199 ms | 2.84 | 56.1 s | 23.1 s |
| Thingi10K (32 cases) | 917 ms | 325 ms | 2.41 | 49.7 s | 22.1 s |
| cherchi, 5 smaller cases | 2166 ms | 846 ms | 2.30 | 14.4 s | 6.1 s |
| bunny 200k + cow 200k | 7.9 s | 4.7 s | 1.69 | | |
| stencil (fertility minus 703 spheres) | 16.7 s | 13.3 s | 1.26 | | |

Parallel scaling after (warm, the cases of the table above): 5 `cherchi` cases 6.1 / 3.9 / 2.9 s on 1 / 2 / 4
cores (2.08), 12 Thingi10K cases 6.9 / 4.6 / 3.3 s (2.09), grid 5 1.47 / 1.01 / 0.73 s (2.01), bunny 200k +
cow 200k 4.8 / 3.6 / 2.8 s (1.68).

**Cold runs and the application's JIT settings.** A cold boolean still takes about 3 times as long as a warm one:
the runtime's generic collections over the library's value types (dictionaries of point keys, lists of segments)
start as unoptimized code. ReadyToRun does not help: the tool published with `PublishReadyToRun` (the library
precompiled) took the same time (`cgal/elephant` 139 against 144 ms, Thingi10K 79241 1099 against 791 ms). The
methods with the JIT hint are not precompiled by it, and the generic code of the runtime over the library's types is
not part of the library's image. What helps is a setting of the application that runs single booleans:
`<TieredCompilation>false</TieredCompilation>` in its project (or `DOTNET_TieredCompilation=0`) made the same cold
booleans 1.4-1.9 times faster (`cgal/elephant` 144 -> 75 ms, Thingi10K 79241 791 -> 552 ms); `<TieredPGO>false</TieredPGO>`
is a milder version. Long-running applications (many booleans in one process) do not need it.

### SIMD spike (not merged)

The floating-point filters are a small part of the time: in a sampling profile of the warm `cgal` and Thingi10K runs,
`ExactPredicates.Orient3d` (whose body is its filter) takes about 10 % of the samples, almost all of it in the
pair search; the box tests and the arrangement's filters are below 1 % each. The spike (branch
`claude/boolean-simd-spike`) filters the three corners of a triangle against the plane of another one in the lanes
of a `Vector256<double>` (`ExactPredicates.Orient3d3`): the same IEEE operations in the same order per lane as
`Orient3d`, no FMA (checked in the disassembly: separate `vmulpd` / `vaddpd`), no reductions across lanes, the exact
stage per point as before. On 17 cases (8 Thingi10K, 8 `cgal`, bunny 50k + cow 50k; union and difference, warm,
two rounds): pair search 842 / 885 ms -> 744 / 782 ms, total 4117 / 4176 ms -> 3979 / 4136 ms, about 2 %, within the
noise of the runs; results bit-identical. The same batching without SIMD (scalar, the plane terms computed once) was
slower than the original (pair search 977 / 1009 ms). Not worth a second code path per platform, so the determinism
rules for SIMD paths (and a switch to force the scalar path) are not needed.

### Tried, no measurable gain (left out)

- **Reused buffers in the intersection of a pair** (the two arrays of sides and the two lists of plane points per
  candidate pair, from a per-thread buffer): tri-tri 325 / 329 -> 319 / 314 ms on the 17 cases above, total
  3744 / 3728 -> 3790 / 3740 ms: within the noise. The other temporary lists and arrays in the hot loops are smaller.
- **`readonly` members of the core structs** (`Vector3d`, `Index3i`, `AxisAlignedBox3d`, ...): they only avoid
  defensive copies when instance members are called on `in` parameters or `readonly` fields; the hot code of the
  pipeline does not do that (its operators are static, its `in` parameters are read by field), so there was nothing to
  measure. Not changed.
- **ReadyToRun**: see above.

### Round 3: the input triangles and the GC

A heap snapshot (`dotnet-gcdump`) in the middle of bunny 200k + cow 200k: of about 970k live objects, 800k were
the 400k input triangles (`InputTriangle`, 152 bytes) and their arrays of corners (`int[3]`). They live through the
whole boolean, so the GC copies each of them twice (into gen1, then gen2); a gen0 collection that promoted 103 MB
took 110-150 ms. Now the corners are a field (`Index3i`), the input coordinates one array of the arrangement (on
the large object heap, never copied): 400k objects of 88 bytes, 64 MB promoted. `prepare_triangle` and
`triangulate` created the closure of their lambdas on entry, for all 400k triangles, also the ones they return from
at once (29 % of the bytes allocated under `triangulate`): the bodies are methods of their own now. Results
bit-identical (263 exact hashes).

Warm, two rounds each (the class sets of the tables above, union / intersection / difference):

| class | median / case before | after | total before | after | GC pause | alloc / run |
|---|---|---|---|---|---|---|
| cherchi | 716 / 720 ms | 707 / 703 ms | 23.2 / 23.6 s | 21.4 / 21.3 s | 27 % -> 22 % | 329 -> 307 MB |
| cgal | 58 / 58 ms | 62 / 63 ms | 9.66 / 9.59 s | 9.63 / 9.13 s | 9 % -> 7 % | 39 -> 37 MB |
| thingi | 96 / 100 ms | 97 / 96 ms | 8.34 / 8.32 s | 8.46 / 7.97 s | 9 % -> 4 % | 56 -> 53 MB |

The large inputs gain 8-10 %; the small ones (with few or no collections in a boolean) are within the noise. n-ary
(grid 5, grid 10, spheres 20): 7.10 / 6.92 s before, 6.96 / 6.94 s after, unchanged.

Tried in this round, no measurable gain (left out):
- **`ExactTriangulation2` on flat arrays** (the corners of all triangles in one `int[]` instead of an array per
  triangle): triangulation 474 / 499 -> 514 / 491 ms on the 17 cases of the SIMD spike, allocations of the phase
  -2 %. Its allocations are the hash maps of the edges and `BigInteger`s of the exact stages, not the triangles.
- **Larger chunks of the point lists** (16384 instead of 2048 elements, on the large object heap, so that the GC
  does not copy them): cherchi 14.8 / 15.1 s -> 15.1 / 15.2 s (more gen2 collections for the large objects).

### Round 4: exact edge planes, plane normalization

A heap snapshot of fertility + spheres (2.4 s, 40 % GC pause): 1.3-1.65M live `BigInteger` digit arrays, most of
them in the exact planes of the triangles near the intersection (the plane and three edge planes, four
`BigInteger`s each). Counted on fertility + spheres, bunny 200k + cow 200k, Thingi10K 61583, `cgal/fandisk` and
grid 5: no real input uses the exact edge planes, only their approximations; grid 5 uses them for 1500 of 1750
triangles (exact stages of coincident constructions). They are now computed with the plane for the approximations,
as before, and kept only when used: 0.6M live digit arrays. In `ExactPlanarSimplify`, the gcd that normalizes a
plane goes over one component after the other (the gcd of the first two is mostly small, the others are then
cheap) and the division by 1 is skipped: same groups; simplify on fertility + spheres 195-220 -> 162-170 ms.
Results bit-identical (263 exact hashes).

Warm, two rounds each (same sets as above; this machine was faster than during round 3, so compare within the
table only):

| class | median / case before | after | total before | after | GC pause |
|---|---|---|---|---|---|
| cherchi | 579 / 585 ms | 576 / 589 ms | 15.5 / 16.7 s | 15.0 / 15.6 s | 19-21 % -> 18 % |
| cgal | 46 / 50 ms | 46 / 47 ms | 7.29 / 7.44 s | 7.20 / 7.44 s | 5-6 % -> 5 % |
| thingi | 80 / 78 ms | 76 / 79 ms | 6.50 / 6.58 s | 6.51 / 6.52 s | 0 % |
| n-ary (grid 5 + grid 10 + spheres 20) | | | 6.07 / 6.14 s | 5.92 / 6.28 s | 33 % |

cherchi -3 to -7 %, the others within the noise.

Looked at in this round, not changed:
- **Pair search within one mesh** (the test for self-intersections): on bunny 200k + cow 200k about 65 ms of the
  170 ms are the box queries of each triangle in its own mesh, about 90 ms the exact tests of the 2.4M pairs that
  overlap (1.84M share a vertex, 0.6M an edge; 120k reach `ExactTriangleIntersection.IntersectsBeyondShared`, which
  is 17 % of a `cgal/fandisk` boolean). A self-join of the box tree and a test that reuses the orientations already
  computed would save perhaps 5 %, in the exact code that other operations share.
- **Assembly**: no single cost (bunny 200k + cow 200k: grouping the edges 25-36 ms, `DMesh3.AppendTriangle`
  35-47 ms); building the mesh from the grouped edges would need a bulk constructor of `DMesh3`, for 2-4 %.
- **`ExactTriangulation2` on flat arrays** again, on the inputs with the most triangulation (fertility + spheres,
  the two spheres, cube + armadillo): 4.66 / 4.94 s -> 4.78 / 4.72 s, no gain.
- **`Orient` = 0 by construction** (branch `claude/collinear-shortcut`): 99 % of the exact stages of the
  arrangement's `Orient` on real inputs are triples with a point constructed on the line through the other two
  (fertility + spheres 111054 of 111976, `cgal/fandisk` 1502 of 1533); answering them without the exact points cut
  the GC pause on fertility + spheres by 18 %, but not the time, and the classes moved within the noise (cherchi
  15.0 / 15.0 -> 15.0 / 15.5 s, cgal 7.27 / 7.18 -> 7.18 / 7.10 s, thingi 6.51 / 6.37 -> 6.31 / 6.28 s). Not merged.
- **Planes in a fixed-width `Int384`** (branch `claude/int256-spike`, .NET 8+): planes phase on fertility + spheres
  285 -> 53 ms, allocations -14 %, total unchanged alone; together with the `Orient` shortcut -5..-7 % on that case
  (both reduce the `BigInteger` objects that live through the boolean; either alone leaves enough of them). Not
  merged; the next step is in [docs/perf/EXACT_BOOLEAN_PERFORMANCE.md](../../docs/perf/EXACT_BOOLEAN_PERFORMANCE.md).
- **GC settings of an application** (not the library's choice): server GC (`DOTNET_gcServer=1`) made fertility +
  spheres 2.19 / 2.06 -> 1.69 / 1.56 s (GC pause 955 -> 225 ms), but small inputs 5-25 % slower;
  `DOTNET_GCgen0size=0x40000000` 1.99 / 1.91 s, neutral on small inputs.
  On the 55928 tail case (Difference, 4 cores, 263 GB allocated; docs/design/EXACT_BOOLEAN_TAIL_55928.md, section 4),
  `DOTNET_gcServer=1 DOTNET_gcConcurrent=0 DOTNET_GCgen0size=0x40000000` made it 474 -> 358 s, with GC pauses
  131 -> 44 s and peak RSS 10.2 -> 10.0 GB. For long booleans on large inputs it is a setting worth documenting
  for the application; a library cannot set it.
- **`Span` / `stackalloc` / `ArrayPool`**: nothing to use them on. The GC cost is the objects that live through
  the boolean, not temporaries, and `BigInteger` allocates its digits itself; reused buffers (round 1) gave nothing.

### What is left

- **`BigInteger`**: 32 % of the bytes allocated by fertility + spheres are its digit arrays (planes, exact points,
  exact stages), and the exact planes and points that live through the boolean are most of what the GC copies.
  The planes have bounded sizes (normal 109 bits, offset 164, edge planes 218 for 53-bit coordinates), so a
  fixed-width integer without allocation would do for them.
- **GC pauses** (4-22 %): the input triangles are still one object each (a struct array would change every user of the
  `Triangles` list: the boolean, the planar simplification, the tests), and the exact stages allocate `BigInteger`s.
- **Pair search** (15-26 %): mostly the exact tests of neighbouring triangles of the same mesh (needed to find
  self-intersections), with exact stages where neighbours are coplanar.
- **The n-ary grid**: still dominated by `BigInteger` arithmetic of the remaining coincident points (other kinds of
  constructions of the same point) and of cocircular points in the triangulation.
- **Cold runs** still take about 3 times as long as warm ones (see above; an application setting, not the library).

### Devillers-Guigue prefilter (November 2026)

The arrangement decides candidate pairs whose triangles strictly straddle each other's plane from two more `Orient3d`
(docs/design/TRITRI_PREFILTER.md) before it constructs any point. Master against master + the prefilter, 5 slow
Thingi10K cohort models × 3 operations, 4 cores, best of 2 rounds, all result hashes identical
(`perf/tritri_prefilter_2026-11-08.jsonl`):

| | master | prefilter |
|---|---|---|
| total | 154.1 s | 120.7 s (−21.7 %) |
| `intersections.intersect` | 34.8 s | 5.5 s (−84.1 %) |
| operations over 5 s | 10 | 7 |
| 1517923 (union / intersection / difference) | 9.9 / 7.0 / 7.6 s | 6.8 / 4.4 / 5.1 s |
| 668056 / 668057 union | 6.5 / 6.0 s | 4.9 / 5.1 s |
| 519511 union | 36.4 s | 28.4 s |

## Thread scaling: the tree build threshold (November 2026)

`TriangleBoxTree` builds its tree (Morton codes, the sort, the nodes) in parallel from `ParallelBuildFrom` boxes on, to the same tree as the serial build
(node for node, `BoxTreeParallelBuildTests`). The threshold was 262 144, so the trees of every operand of 1k-100k triangles (the input trees of the
arrangement and SafeRounding's tree of the result) were built serially: 17 % of the 4-thread time of the T6 set
([docs/perf/BOOLEAN_THREAD_SCALING.md](../../docs/perf/BOOLEAN_THREAD_SCALING.md)). It is now 8192 (above the median split's 4096 boxes, which stays serial).
Callers of the constant: the constructor (the build and the copy of the leaf boxes), `morton_order` and `sort_codes` (only reached for trees above the median split);
the trees of fewer than 8192 boxes, among them every small dirty tree of SafeRounding, are unchanged.

A/B on the T6 set (98 pairs, 294 booleans, SafeRounding on, warm, `profile --repeat 2`, the faster run per boolean, sums), the thresholds interleaved in
3 rounds (order reversed in round 2), 4-vCPU container; `--tree-parallel-from <n>` sets the threshold; raw rows
[perf/tree_threshold_2026-11-15/](perf/tree_threshold_2026-11-15/):

| threads | threshold | totals of the 3 rounds (ms) | mean | against 262144 | trees phase | rounding phase |
|---|---|---|---|---|---|---|
| 1 | 262144 (before) | 35715 / 36711 / 34792 | 35739 | – | 2135 | 7535 |
| 1 | 8192 | 34928 / 34992 / 35160 | 35027 | −2.0 % | 2078 | 7383 |
| 1 | 4096 (effective 4097) | 36690 / 34574 / 35639 | 35634 | −0.3 % | 2047 | 7525 |
| 4 | 262144 (before) | 20073 / 18570 / 18983 | 19209 | – | 1684 | 4121 |
| 4 | **8192** | 18619 / 18370 / 18066 | 18352 | **−4.5 %** | 998 | 3726 |
| 4 | 4096 (effective 4097) | 18841 / 18518 / 17795 | 18385 | −4.3 % | 988 | 3797 |

At 1 thread the difference is noise (±3 %); at 4 threads the trees phase falls by 41 % and the rounding by 10 %, the whole by 4.5 %. 8192 and 4097 do not differ; 8192
is kept (smaller trees gain little from the parallel build). All 18 files give the same result hash for every one of the 294 booleans (1 and 4 threads, three thresholds).

The re-check against #837's case set (37 unions of the extreme-runtime study, thresholds 262144 / 16384 / 8192, 4 and 1 threads, 3 interleaved rounds): rows `perf/tree_threshold_2026-11-15/rerun_837_variants_2026-11-15.jsonl.gz`, tables by `python3 perf/rerun_837_report.py <rows>` (classification does not slow down: −0.1 % at 8192, 4 threads; the details are in the PR body of #956).

## Thread scaling on the T6 set (`perf/thread_scaling.ps1`)

The question: what limits the speedup of `ExactMeshBoolean` with the number of threads (the analysis and the lever list:
[docs/perf/BOOLEAN_THREAD_SCALING.md](../../docs/perf/BOOLEAN_THREAD_SCALING.md)). `perf/thread_scaling.ps1` runs the T6 case set (the 98 pairs of
`perf/t6.py`, 294 booleans, SafeRounding on) or the three cases with the largest rounding time in the earlier cold measurement (`-Set three`: p0466_203344+80414, p0861_80557+285607,
p0974_97674+472023) at 1, 2, 4, 8 and 12 threads on net10.0 (and net48, Windows only), the combinations alternated round by round, and writes
the phase times and the result hashes to CSV. Thingi10K is fetched at run time (about 300 MB, outside the repository); the script needs `python3`
(or `python`) and the .NET 10 SDK, in Windows PowerShell 5.1 or PowerShell 7.

```powershell
# the plan and the exact commands, nothing runs
.\benchmarks\BooleanComparison\perf\thread_scaling.ps1 -DryRun
# a short check of the setup: 3 cases, 1 round (fetches the models, builds, exports the cases)
.\benchmarks\BooleanComparison\perf\thread_scaling.ps1 -Set three -Rounds 1 -Limit 3 -ThreadCounts 1,2
# the run: T6 and the three cases, 1/2/4/8/12 threads, 3 rounds (a long run, the first 4-core container run of one round took 15 minutes; not resumable)
.\benchmarks\BooleanComparison\perf\thread_scaling.ps1 -Set t6,three -Rounds 3 -OutDir D:\scaling
# net48 as well (builds with -p:BenchNet48=true)
.\benchmarks\BooleanComparison\perf\thread_scaling.ps1 -Set t6 -Runtimes net10.0,net48 -Rounds 3 -OutDir D:\scaling48
# one physical core per thread on a machine with SMT (up to 6 threads on a 6-core / 12-thread CPU)
.\benchmarks\BooleanComparison\perf\thread_scaling.ps1 -ThreadCounts 1,2,4,6 -Stride 2
```

Output in `-OutDir`: `runs.csv` (one row per process), `cases.csv` (best time and hash of each boolean), `phases.csv` (phase times of the fastest run of
each boolean), `summary.csv` (sum of the best times per thread count, speedup, efficiency), `raw\*.jsonl`, `settings.json`. The exit code is 1 if
a run failed or one boolean has two different hashes over all threads, runtimes and rounds (the drift check; the hash covers vertex ids, coordinates
and triangles). Phase tables and the Amdahl bounds from the raw rows:

```powershell
python3 benchmarks/BooleanComparison/perf/scaling_report.py --bound 1=D:\scaling\raw\t6_net10.0_t1_r1.jsonl,D:\scaling\raw\t6_net10.0_t1_r2.jsonl 4=D:\scaling\raw\t6_net10.0_t4_r1.jsonl 12=D:\scaling\raw\t6_net10.0_t12_r1.jsonl
python3 benchmarks/BooleanComparison/perf/scaling_report.py --hashes 1=...\t6_net10.0_t1_r1.jsonl 12=...\t6_net10.0_t12_r1.jsonl
```

On net48 the mask is set right after the process start and there is no `DOTNET_PROCESSOR_COUNT`: check the `cores` column of `raw\\*.jsonl` (the processor count the process saw) before trusting a net48 thread count; on net10.0 the script fails a run whose `cores` differs from the requested count.

Threads are limited by the affinity mask of the process (and `DOTNET_PROCESSOR_COUNT` on net10.0): `-Stride 1` takes logical processors 0..n-1,
which are SMT pairs on a CPU with hyper-threading, so 2 threads there are one physical core. Times are warm (one process per thread count and round
runs all cases after a warm-up, `profile`); one-process-per-boolean cold times are those of `compare.py`.

## Indirect predicates (`indirect`)

`BooleanComparison indirect [rounds]` times the generated indirect predicates (`math/Generated/IndirectPredicates.cs`,
docs/design/INDIRECT_PREDICATES.md section 9): Orient3d of a line-plane point and of a three-plane point on 30-bit
integer inputs, general and degenerate, against constructing the point as a reduced rational first. The numbers are in
section 9 of the design doc.

## Polyhedral envelope segments (`envelope`)

`BooleanComparison envelope [rounds]` runs `PolyhedralEnvelope.ContainsSegment` (docs/design/ENVELOPE.md, PR 2 of #263)
on normalized-cube spheres. Each row uses 2000 segments of two edge lengths, tangent to the surface within ±eps / 2.
The command then replays the C2 loop with the same candidates, crossings and early exits, and times only the
`Orient3dLPI` calls. The best of 5 rounds is shown; µs are per segment. This is the upper-bound check for the
prepare / evaluate split (ENVELOPE.md section 6, 1b).

| mesh | eps | contained | µs / segment | LPI calls / segment | LPI µs / segment (Stopwatch per call) |
|---|---|---|---|---|---|
| sphere 10 | 0.5 edge | 855 | 85.9 | 976 | 85.0 |
| sphere 20 | 0.5 edge | 1443 | 117.4 | 1660 | 125.3 |
| sphere 20 | 0.2 edge | 561 | 28.1 | 274 | 26.0 |
| sphere 40 | 0.5 edge | 1604 | 139.7 | 1816 | 171.8 |

The per-call Stopwatch adds about 20-30 ns. Even so, the calls times the standalone filtered cost (65-80 ns,
`indirect`) give about 90 % of the query time: `ContainsSegment` is almost all `Orient3dLPI`.

In the LPI formula, the construction part (n, lp, lq, d) depends only on the segment and the crossed facet. It is
about 30 of 65 operations of the filter. It is evaluated anew against every facet of every candidate. A prepare /
evaluate split saves at most that part, about 45 % of the LPI time, so the query could get up to about 1.7× faster.
That is far above the noise, so 1b goes ahead. The other lever is the number of calls: the paper's covering set
(section 3.2.4) cuts it. That comes with the triangle query (PR 3, #268, merged 2026-11-02: `ContainsTriangle`).

**Built (1b):** `ContainsSegment` now prepares each crossing once (`Orient3dLPIPrepared`). Alternating rounds A
(88d50d2, before) and B (1b), µs per segment, best of 5 per run, two runs each:

| mesh | eps | A | B | speedup |
|---|---|---|---|---|
| sphere 10 | 0.5 edge | 65.5 / 85.7 | 41.2 / 48.3 | 1.6-1.8× |
| sphere 20 | 0.5 edge | 106.9 / 137.4 | 57.2 / 70.0 | 1.9-2.0× |
| sphere 20 | 0.2 edge | 27.7 / 28.0 | 12.8 / 16.5 | 1.7-2.2× |
| sphere 40 | 0.5 edge | 147.0 / 131.5 | 67.7 / 75.8 | 1.7-2.2× |

The column "LPI µs / segment" of the command still replays the unsplit calls, so after 1b it exceeds the query time.

## Tail case knot (the 55928 stand-in)

`BooleanComparison knot` writes an open tube (`--segments` rings of `--ring` vertices) wound `--turns` times inside a unit
ball, tube radius `--tube`, with the export's pairing as B. It reproduces the profile of Thingi10K 55928
(`docs/design/EXACT_BOOLEAN_TAIL_55928.md`): both operands open with few boundary segments (16 here, 133 there), the
classification dominating, hundreds of plane-side tests per exact ray, a few pieces per patch.

| `knot` options | triangles per mesh | pieces | self pairs per triangle | pieces per patch | plane sides per piece |
|---|---|---|---|---|---|
| `--segments 200 --ring 6 --turns 16 --tube 0.4` (test size) | 2,400 | 136,866 | 3.8 | 8.0 | 143 |
| `--segments 400 --ring 8 --turns 24 --tube 0.6` (benchmark) | 6,400 | 615,682 | 6.2 | 7.2 | 295 |

Before the tail-case stage (4 cores, the benchmark size, `profile --repeat 3`, ms, median; classification and
triangulation of the fastest run):

| build | Difference | classification | triangulation | Union | hash Difference / Union |
|---|---|---|---|---|---|
| 79a2c1ab (open operands per patch, fast winding numbers) | 5,107 | 3,416 | 1,076 | 4,926 | 52258C35170BF15E / 2F867E8DDE54CA6D |
| master 88342701 (per piece, Antipodal Method) | 15,771 | 13,682 | 1,127 | 15,300 | 52258C35170BF15E / E8871D31AAB24DF1 |

The per-piece classification of open operands (98def50f, d458662c) costs 4 times the per-patch one here, about 2 times on
55928 itself (classification 390 -> 772 s, 579 -> 973 s per Difference). The Difference results are the same; the Union
results differ (98def50f changed the classification of open operands on purpose: a patch can reach through a hole from
inside an open mesh to outside it, so the threshold is taken per piece).

