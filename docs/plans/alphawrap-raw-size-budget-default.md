# Plan: alpha wrap raw-size budget on by default (owner decision)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Alpha wrap: raw-size budget on by default"
- **Kind:** owner decision
- **Tier:** M (default change + full 800-mesh gate run; touches `MeshAlphaWrap` / `MeshRobustRepair` defaults)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Measured on the 800-mesh Thingi10K sample ([ALPHA_DEFAULTS_800.md](../perf/ALPHA_DEFAULTS_800.md), runs 43972566855
  and cap run 44013288852; [alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15)):
  - Raw-size budget (#846) and `MaxAlphaCoarsening` (#1014) are merged and **off by default** (owner 2026-11-17).
  - Known safe recipe for bounding the wrap tail: **budget 300 000 triangles with `MaxAlphaCoarsening = 1.2`** (coarsening
    above ~1.2 fills a hollow behind a narrow neck on mesh 40118).
  - Open-input fix merged (#1123, #1127): the estimate counts open input twice so a budget means the same on open input,
    and is computed only when a budget is set.
  - Opt-in target triangle count merged (#1106: `WrapTargetTriangles` / `WrapTargetFactor` / `WrapTargetMaxDistance`),
    defaults unchanged.
  - In the first runtime-watch report (#888), 18 Thingi10K wrap pipelines took 21.9-77.7 s at the unbounded default.

## Trigger / decision needed

Owner chooses whether to keep the raw-size budget opt-in or evaluate enabling it by default:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Keep off by default (leaning unless tail times block callers)** | Existing defaults stay bit for bit identical; callers needing a memory/time cap set budget `300 000` + `MaxAlphaCoarsening = 1.2` (or `WrapTargetTriangles`) | Zero regression risk; extreme open/thin soups can still produce > 500k raw wrap triangles and > 20 s wraps |
| B. **Run one 800-mesh benchmark of (budget = 300 000, `MaxAlphaCoarsening = 1.2`), then decide default-on** | Gives paired before/after numbers (tail time, max raw triangles, Hausdorff drift, hollow preservation on 40118) before any code default changes | One runner job (`alpha-defaults.yml`, ~1-2 h on GitHub runners); if turned on, changes output on meshes hitting > 300k raw tris and updates golden masters / docs |

How others solve it: CGAL `alpha_wrap_3` (Portaneri et al., ACM TOG 2022) has no automatic alpha coarsening — callers
pick `(alpha, offset)` explicitly relative to the bounding-box diagonal; OpenVDB / Houdini `VDBFromPolygons` caps voxel
count via a memory/voxel budget warning or clamp; Blender's Remesh / Voxel modifier enforces a UI-level floor on voxel
size to prevent OOM; MeshLab / PyMeshLab expose explicit face-budget parameters opt-in rather than silently coarsening
the geometric tolerance.

## Goal

Decide whether `(raw budget = 300 000, MaxAlphaCoarsening = 1.2)` stays opt-in or is validated on the 800-mesh sample
and promoted to the default.

## Scope

- Option A: `docs/OPEN_TOPICS.md`, `docs/roadmap/alpha-wrap-and-repair.md` (close decision row; document the recommended
  recipe in `docs/OPERATIONS.md`).
- Option B (phase 1, measurement): dispatch `.github/workflows/alpha-defaults.yml` with the recipe, append table to
  `docs/perf/ALPHA_DEFAULTS_800.md`.
- Option B (phase 2, only if owner approves after phase 1): `mesh_ops/MeshAlphaWrap.cs` / `mesh_ops/MeshRobustRepair.cs`
  defaults, `tests/geometry3Sharp.Tests/*AlphaWrap*Tests.cs`, `docs/OPERATIONS.md`, `docs/UPSTREAM_CHANGES.md`.

## Non-goals

No change to `MaxAlphaCoarsening` algorithm, no loosening of the wrap containment guarantee (`input inside` always holds),
no API break against 1.0.324.

## Interface

No new API (options already exist). Option B phase 2 changes only the default property values and XML doc comments.

## Look before you build

1. Library: `MeshAlphaWrap.MaxAlphaCoarsening` (#1014), raw-size budget (#846), double-count open-input estimator (#1123,
   #1127), `.github/workflows/alpha-defaults.yml`, `docs/perf/ALPHA_DEFAULTS_800.md`.
2. Literature: Portaneri et al. (ACM TOG 2022, "Alpha Wrapping with an Offset").

## Steps (one PR, clean commits)

1. Owner picks A or B.
2. If A: document the `(300 000, 1.2)` recipe in `docs/OPERATIONS.md` and close the §2 row.
3. If B: run `alpha-defaults.yml` on master with budget 300 000 + `MaxAlphaCoarsening = 1.2` (include mesh 40118 and the
   18 tail meshes from #888); report paired before/after table (wall time p50/p95/max, count > 20 s, Hausdorff error,
   volume IoU on 40118, clean rate) to the owner; flip defaults in one PR only after owner sign-off.

## Acceptance

- Option A: docs diff reviewed.
- Option B: 800-mesh table with workflow run ID committed to `docs/perf/ALPHA_DEFAULTS_800.md`; 0 containment failures;
  mesh 40118 hollow preserved; paired tail times improved; if default flips, full local CI + `AlphaWrap*Tests` green
  including an explicit test pinning the default budget and opt-out (`0` = unbounded).

## Sources and licences

Portaneri et al. (2022) paper; no CGAL code read or ported.

## Budget and stop rule

Option A: 10 min. Option B: one `alpha-defaults.yml` run; stop (keep opt-in) if any of the 800 meshes loses a hollow
(volume IoU drop > 5 %) or if the tail above 20 s drops by < 20 %.

## Risks and open questions

- Coarsening changes `UsedAlpha` dynamically; callers relying on a strict `alpha + offset` Hausdorff guarantee must be
  able to opt out (`budget = 0`), and `UsedAlpha` must remain clearly reported.
