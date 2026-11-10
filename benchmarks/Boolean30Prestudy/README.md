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
| 6 positioning (thingi-pairs CGAL sample + 10 size pairs) | `../BooleanComparison/compare.py` (see its README), methods ours, manifold, cgal_epeck | `../BooleanComparison/data/thingi_pairs.py summary` | not committed (CGAL results are GPL-tool output) | `results/positioning_summary.md` |

All timings: the 4-core cloud machine, nothing else running, one job at a time.
