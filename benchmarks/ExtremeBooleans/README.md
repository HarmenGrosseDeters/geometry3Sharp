# ExtremeBooleans

Scripts of [docs/perf/EXTREME_RUNTIME_BOOLEANS.md](../../docs/perf/EXTREME_RUNTIME_BOOLEANS.md): the inventory of the
`ExactMeshBoolean` examples with extreme run time, their stage profile and peak memory, and the probes behind its
levers. They drive `benchmarks/BooleanComparison` (`profile`, `export`); nothing here is a library change.

| file | what |
|---|---|
| `fetch.py` | Thingi10K raw meshes by id (fetched at run time, never committed: they carry their own licenses) |
| `dump2off.py` | a `MeshFuzzPilot dump` text as a case directory (A.off, B.off; round-trip coordinates) |
| `scale_case.py` | a case scaled by 2^k, optionally with tiny coordinates set to 0 (the #711 variants) |
| `run.sh` | `profile --repeat 1 --safe-rounding` per case and operation in its own process, with peak RSS and a heap cap; resumable |
| `job.sh` | the inventory job (copy it and `run.sh` to the work directory before starting: bash reads a running script lazily) |
| `summarize.py` | the table of section 3 (`--md`) |
| `speedscope_top.py`, `speedscope_callers.py` | self / inclusive CPU shares and callers from a `dotnet-trace` Speedscope file |
| `probe_bigint.py`, `probe_fwn.py`, `probe_shards.py`, `probe_det4.py` | scratch patches for a throw-away worktree (never committed to the library): BigInteger exact stages, FWN queries, `AddPoint` shards, `orient3d` without the matrix |
| `probe_boundary_sum.py` | the hierarchical Antipodal boundary sum against the full sum, offline in numpy (`python3 -I`) |
| `results/inventory_2026-11-12.jsonl` | the raw rows (master 8124a7d3 + #675 a9b00da3, 4-core cloud container) |
| `results/probes_2026-11-12.md` | the probes' outputs |

`BooleanComparison profile` gained two options for this note: `--antipodal-unlimited` (both Antipodal switches lifted,
T5 option A) and `--rounding-max-pair-tests <n>`.
