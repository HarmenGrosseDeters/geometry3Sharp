# Performance Reports

Measured performance investigations, profiling studies, and scaling analyses for `geometry3Sharp`. Each report names the benchmark commands and datasets that produced its numbers (see [benchmarks/README.md](../../benchmarks/README.md)).

| Report | Topic & Scope |
|---|---|
| [ALPHA_DEFAULTS_800.md](ALPHA_DEFAULTS_800.md) | Evaluation of `MeshAlphaWrap` default parameters and feature/budget levers across an 800-mesh sample |
| [BOOLEAN_COPLANAR_PREDECISION.md](BOOLEAN_COPLANAR_PREDECISION.md) | Analysis of coplanar pair pre-decision in `ExactMeshArrangement` for chained booleans (ABC chains) |
| [BOOLEAN_THREAD_SCALING.md](BOOLEAN_THREAD_SCALING.md) | Multi-core thread-scaling bottlenecks in `ExactMeshBoolean` |
| [EXACT_BOOLEAN_PERFORMANCE.md](EXACT_BOOLEAN_PERFORMANCE.md) | Profiling state and optimization handover for `ExactMeshBoolean` / `ExactMeshArrangement` |
| [EXTREME_RUNTIME_BOOLEANS.md](EXTREME_RUNTIME_BOOLEANS.md) | Inventory, GC impact, and stage breakdowns for extreme-runtime boolean pairs |
| [REMESHER_PHASE2_MEASURED.md](REMESHER_PHASE2_MEASURED.md) | Measured cost and quality impact of `Remesher` quality and self-intersection safeguards (`ApplySafePreset`) |
