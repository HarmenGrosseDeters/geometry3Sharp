# JOB-1: Thingi10K subset through TetMeshBenchmark (tet meshing stage 0 / baseline for stage 1)

Runner: Haiku runner session, one job at a time, alone on its machine (timings are only meaningful alone). No code, no
decisions. Meshes are never committed (Thingi10K licences): download to a local directory outside the repository.

1. Checkout: `git clone https://github.com/HarmenGrosseDeters/geometry3Sharp` and use `master` if PR #487 is merged,
   else the branch `claude/tet-stage0` (commit 93155849 or later). .NET 10 SDK needed (`apt-get install -y dotnet-sdk-10.0`).
2. Build: `dotnet build benchmarks/TetMeshBenchmark -c Release` (must end with 0 errors).
3. Fetch this branch's `jobs/` (this directory) and run, with a clean `<mesh dir>` outside the checkout:
   `bash jobs/run-thingi-subset.sh <repo checkout> <mesh dir> results-job1.csv 20 600`
   40 ids in `thingi-subset-ids.txt` (every 250th id of the Hugging Face dataset `Thingi10K/Thingi10K`, `metadata/file_ids.txt`).
   Mesh source: `https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/<id>.stl` (the script
   fetches it with `curl -L`; if your own Thingi fetch path is faster, place `<id>.stl` in `<mesh dir>` first).
   The script is resumable: after a restart run the same command again.
4. Output: `results-job1.csv`, one line per mesh in the column order of `dotnet <dll> wrap` header
   (`input,status,input_tris,steiner_points,wrap_ms,extract_ms,validity_ms,measure_ms,tets,boundary_tris,wrap_us_per_tet,extract_us_per_tet,re_worst,...,valid,surf_*`),
   `TIMEOUT_*` rows for timeouts, a last line `FINISHED`. Push the CSV, `uname -a`, `nproc` and the commit hash used into a
   `REPORT-1.md` on this branch (`runs/tet-meshing`). No meshes, no foreign outputs.
5. Report to the coordinator, one line: number of meshes with status Success and `valid=yes`, median wrap_ms, the maximum
   wrap_ms with its id, any TIMEOUT or failure ids.

Reference (cloud container, 4 cores shared, 2026-11-09): 40 of 40 Success and valid, wrap median 0.33 s, max 13.4 s (id 120628, 2.5M triangles).
Later jobs (stage 1) reuse this list with the refinement benchmark mode.
