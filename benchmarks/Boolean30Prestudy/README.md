# Exact Boolean 3.0 pre-study: jobs, analysis scripts and results

Measurements of [docs/design/EXACT_BOOLEAN_30_PRESTUDY.md](../../docs/design/EXACT_BOOLEAN_30_PRESTUDY.md). The probes
are modes of `BooleanComparison` (`Prestudy.cs`, `PrestudyFwn.cs`); this directory holds the job scripts, the
analysis scripts and the rows. No library code; no meshes (Thingi10K models are fetched at run time with
`../LevyClassification/fetch_thingi.py` and exported with `BooleanComparison export`, the #385 pairing).

Setup of a work directory `W` (outside the repository):

```sh
dotnet build benchmarks/BooleanComparison -c Release -f net10.0 -o $W/tool_pre       # this branch (master library)
python3 -I benchmarks/LevyClassification/fetch_thingi.py $W/raw $(cut -d' ' -f1 benchmarks/Boolean30Prestudy/results/scaling_ids.txt)
for id in ...; do dotnet $W/tool_pre/BooleanComparison.dll export $W/raw/$id.* $W/cases/$id; done
```

| topic | job | analysis | rows | summary |
|---|---|---|---|---|
| 1 parallel scaling (1 / 2 / 4 cores, 73 models x 3 ops) | `scaling_job.sh W ids [cores]` (tool in `W/tool_master`) | `analyze_scaling.py results/scaling.jsonl results/scaling_ids.txt --per-op` (or `W/scaling`) | `results/scaling.jsonl` | `results/scaling_summary.md` |
| 2 memory (8 large models x 3 ops, GC variants) | `memory_job.sh W W/tool_pre "<ids>"` | `analyze_memory.py results/memory.jsonl` | `results/memory.jsonl` | `results/memory_summary.md` |
| 3 n-ary one pass vs chained | `csg_job.sh W W/tool_pre` | `analyze_csg.py results/csg.jsonl` | `results/csg.jsonl`, `results/csg_phases.jsonl` (boxes_64_1 and mixed_32_1 rerun with the phase clocks) | `results/csg_summary.md` |
| 4 exact output | `exact_job.sh W W/tool_pre "<ids>"` | `analyze_exact.py results/exact.jsonl` | `results/exact.jsonl` | `results/exact_summary.md` |
| 5.1 Antipodal cost model (T5 count) | `antipodal_job.sh W W/tool_pre` | `analyze_antipodal.py results/antipodal.jsonl` | `results/antipodal.jsonl` | `results/antipodal_summary.md` |
| 5 batched fast winding bound | `fwn_job.sh W W/tool_perpiece "<ids>"` (BooleanComparison of this branch built in a worktree of bfaa0158) | `analyze_fwn.py results/fwn.jsonl` | `results/fwn.jsonl` | `results/fwn_summary.md` |
| 7 one body against many (T3a subtract many, T3b intersect each; terrain and CAD) | `many_job.sh W W/tool_pre` | `analyze_many.py results/many_a.jsonl results/many_b.jsonl` | `results/many_a.jsonl`, `results/many_b.jsonl` | `results/many_summary.md` |
| 6 positioning (thingi-pairs CGAL sample + 10 size pairs) | `../BooleanComparison/compare.py` (see its README), methods ours, manifold, cgal_epeck | `../BooleanComparison/data/thingi_pairs.py summary` | not committed (CGAL results are GPL-tool output) | `results/positioning_summary.md` |

All timings: the 4-core cloud machine, nothing else running, one job at a time.

## T2 memory of the arrangement (`t2/`)

Exact Boolean 3.0 T2 (PR #619): data layout of the arrangement, results bit-identical. Procedural inputs, because
Thingi10K is not reachable from the build container (the large-model rerun with `memory_job.sh` is for a machine that has
the cases):

```sh
python3 t2/gen_spheres.py sph300 300 $W/cases      # 717,600 input triangles, two bumpy spheres, 0.5 % touched
python3 t2/gen_spheres.py cross150 150 $W/cases    # 178,800 input triangles, surfaces crossing everywhere, 4.8 % touched
bash t2/measure.sh <BooleanComparison dir> <label>     # prestudy-memory (retained, peak) + profile --repeat 5 (time, result hash)
bash t2/alternate.sh <case> <label> <label>...         # alternating timing rounds of tool_<label> directories (the machine's noise is +-10 %)
```
(`measure.sh` and `alternate.sh` take the work directory, which holds `cases/` and `tool_<label>/`, from the environment variable `S`.) Tools are built per commit: master, A, A+B,
A+B+C (`dotnet build benchmarks/BooleanComparison -c Release -f net10.0 -o tool_<label>`). Result hashes (`profile`) are equal
in every row. Retained = the arrangement alone after `Compute` (the probe keeps `Trees`, which the boolean releases after the
classification); peak = peak working set of the full boolean, bytes per input triangle.

| case | op | arrangement B/tri: master | A | A+B | A+B+C | peak WS B/tri: master | A | A+B | A+B+C |
|---|---|---|---|---|---|---|---|---|---|
| sph300 | union | 390.6 | 342.5 | 280.3 | 208.2 | 1262 | 1142 | 1099 | 1014 |
| sph300 | intersection | 390.6 | 342.5 | 280.3 | 208.2 | 1054 | 960 | 969 | 834 |
| sph300 | difference | 390.6 | 342.5 | 280.3 | 208.2 | 1154 | 1056 | 1061 | 961 |
| cross150 | union | 463.9 | 415.8 | 356.0 | 283.8 | 2282 | 2245 | 1918 | 1592 |
| cross150 | intersection | 463.9 | 415.8 | 356.0 | 283.8 | 2545 | 2196 | 1677 | 1921 |
| cross150 | difference | 463.9 | 415.8 | 356.0 | 283.8 | 1991 | 2014 | 1944 | 1636 |

Time (median of 15 runs, ms; alternating rounds, master -> A+B+C): sph300 union 1323 -> 1145, intersection 786 -> 741, difference
1046 -> 1046; cross150 union 449 -> 465, intersection 408 -> 446, difference 398 -> 407. The differences are inside the run-to-run noise of
the machine (a further alternating round of cross150 gave intersection 441 / difference 391 for master against 428 / 398 for A+B+C); no change adds work.
