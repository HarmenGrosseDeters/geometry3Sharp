# Research Notes & Comparative Studies

Prior-art checks, literature surveys, external library comparisons, and exploratory feasibility studies that inform engineering decisions without modifying library code directly.

| Study | Topic & Summary |
|---|---|
| [BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) | Prior-art check for post-3.0 exact boolean research directions |
| [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md) | Spike for plan #1256: measured cost of a certifying checker of `ExactMeshBoolean` (stage 3, the embedding), a certified user mode, and the state of exact coplanar clip coordinates |
| [BOOLEAN_TRIAGE_2026-11.md](BOOLEAN_TRIAGE_2026-11.md) | Diagnosis and triage of boolean robustness findings collected across corpora |
| [CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md) | Cross-disciplinary survey of hard computational geometry problems and candidate methods |
| [ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md](ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md) | Spike on HP 10: a reduction driven by the exact polyhedral envelope of Wang et al. 2020 against `Reducer.MaxDeviation` (leaning no-go: 1.1-1.9x the triangles, mostly 5-98x the time) |
| [CROSS_FIELD_TRANSFER_2026-11.md](CROSS_FIELD_TRANSFER_2026-11.md) | Problem-first transfer search: the structures behind the open hard problems, the fields that solve them routinely, a ranked shortlist with spike briefs, and the candidates closed by existing numbers |
| [EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md) | Upper bounds of speed levers for `ExactMeshBoolean` and the rounding that keep every guarantee (seam-local work, certified operands, serial tail, wide trees), with a ranked list |
| [GEOGRAM_BOOLEAN_COMPARISON_2026-11.md](GEOGRAM_BOOLEAN_COMPARISON_2026-11.md) | Head-to-head comparison of `ExactMeshBoolean` against Geogram's exact mesh booleans |
| [GEOGRAM_MEASUREMENTS_2026-11.md](GEOGRAM_MEASUREMENTS_2026-11.md) | Targeted measurements of high-priority ideas identified in the Geogram survey |
| [GEOGRAM_SURVEY_2026-11.md](GEOGRAM_SURVEY_2026-11.md) | Detailed architectural survey of Geogram (BSD-3) algorithms relevant to `geometry3Sharp` |
| [IMPRINT_CORPUS_2026-11.md](IMPRINT_CORPUS_2026-11.md) | Corpus evaluation of `ExactMeshImprint` on Thingi10K contact pairs |
| [MESHING2D_HP6_HP7_SPIKE_2026-11.md](MESHING2D_HP6_HP7_SPIKE_2026-11.md) | 2D meshing spike: proven small-angle refinement rules against the library (HP 6) and a certifying checker against the oracle (HP 7), with measured upper bounds and ranked options |
| [INTRINSIC_INTEGER_COORDINATES_2026-11.md](INTRINSIC_INTEGER_COORDINATES_2026-11.md) | Spike for integer (normal) coordinates in intrinsic triangulations: failure rate of the traced common subdivision, prototype, cost, options |
| [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md) | Metamorphic testing and fuzzing study for geometry kernels with falsification runs |
| [SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md) | End-to-end audit of `SolidBooleanPipeline` on a procedural adversarial corpus with an independent oracle: findings, measured options for the open owner items, ranked improvement PRs |
| [PLANE_BASED_KERNEL_STUDY_2026-11.md](PLANE_BASED_KERNEL_STUDY_2026-11.md) | Feasibility study of a plane-based (EMBER-style) boolean kernel (concluded no-go) |
| [TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md](TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md) | Spike on a proven dihedral floor for tet meshing (HP 5): isosurface stuffing and surface chamfering read, a lattice prototype measured against `MeshTetrahedralization`, options (leaning: stop) |
| [QUAD_SHEAF_OBSTRUCTION_2026-11.md](QUAD_SHEAF_OBSTRUCTION_2026-11.md) | Spike: cellular-sheaf / cohomology diagnostics of the pure-quad fallbacks (predictor, area invariant, upper bounds, options) |
| [TET_TIE_ORDER_2026-11.md](TET_TIE_ORDER_2026-11.md) | Spike: the tet improver under relabelings of the stage-1 mesh (tie order): effort at the floor 20 varies little (the work cap stands), the default floor's two open meshes are lifted by other orders; proposal of an opt-in restart |
| [UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md) | Cross-disciplinary survey and ROI evaluation of untapped 2021–2026 math, physics, biology, and chemistry research for computational geometry |
| [SIGGRAPH_2025_2026_SURVEY_2026-11.md](SIGGRAPH_2025_2026_SURVEY_2026-11.md) | SIGGRAPH 2025/2026 paper survey, deep dive on fast sparse matrix permutation (arXiv:2602.00898), and codebase audit of FEWN/Antipodal winding numbers |
| [DETERMINISM_CROSS_RUNTIME_2026-11.md](DETERMINISM_CROSS_RUNTIME_2026-11.md) | Bit-for-bit determinism across runtimes and CPUs: wide measurement (documented operations, JIT switches, arm64), static reachability of libm and other sources, stage 2 estimate, golden-master tiers, ranked options |
| [QUAD_SEAMLESS_SEED_2026-11.md](QUAD_SEAMLESS_SEED_2026-11.md) | Research spike: rectangular and Penner-coordinate parametrization (SIGGRAPH 2025) as a replacement or seed for the quad pipeline's position field, with a measured upper bound |
| [CONTOURING_SDF_SUBGRID_2026-11.md](CONTOURING_SDF_SUBGRID_2026-11.md) | Spike on SIGGRAPH 2026 contouring (sharp features from distance grids, subgrid marching tetrahedra for walls thinner than a cell) and a subdivision certificate for thin walls, with measured upper bounds |
| [SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](SNAP_ROUNDING_3D_GUARANTEE_2026-11.md) | 3D rounding with a proven guarantee (Devillers et al. 2018, read in full): measured upper bounds of its gain and cost against SafeRounding, options (leaning: no build) |
| [ROUNDING_THIN_SOLIDS_2026-11.md](ROUNDING_THIN_SOLIDS_2026-11.md) | SafeRounding removes real thin solids (#1216): root cause (thin by the bound and edited anywhere), candidate removal rules scored on 3 529 procedural cases, the guaranteed-rounding plan #1259 against it (no-go), options (leaning: below 4 ulps, or still defective, or mangled) |
| [PROVABLE_SHARP_WRAP_STUDY.md](PROVABLE_SHARP_WRAP_STUDY.md) | HP 4 study: validity theorem for checked local operations on the alpha wrap, measured upper bound of sharp-feature recovery (oracle: direct mitered offset), ranked options |
| [METAMORPHIC_BUG_HUNT_2026-11.md](METAMORPHIC_BUG_HUNT_2026-11.md) | Spike: metamorphic relations and operation chains (Boolean → repair → remesh / tet, wrap, contouring, 2D mesher) on procedural inputs with independent checks: every relation and chain tried, one defect (#1337), what is left |
