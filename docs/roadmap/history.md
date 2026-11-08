# History

Part of the [roadmap](../ROADMAP.md).

- **Scope:** finished material whose value is mostly the record: Phase 1, the Phase 2 feature table, and the takeover
  of the abandoned work (2026-10-30 to 2026-11-02) with its stage table and slot order. The stages themselves are
  kept on the topic pages; this page links them.
- **Status:** done.

### Open

- Nothing open here. The open items of the takeover stages are on their topic pages.

### Done / history

#### Takeover of the abandoned work (2026-10-30)

Status on 2026-10-30. Several sessions stopped mid-way; their work is parked in draft PRs titled
`[WIP, quality unverified]`. **Its quality is questionable.** After takeover, question and check everything in it,
including its tests, numbers and recorded conclusions: re-run the numbers, run the mutation checks, and get the
independent review of [REVIEW_CHECKLIST.md](../REVIEW_CHECKLIST.md). A green CI is not acceptance. A PR that does not
hold up is discarded with the owner, not patched until it passes. The stages run in this order:

The stage table of the takeover, by topic:

| Stage | Where |
|---|---|
| 0 to 3 | [below](#stages-0-to-3) |
| 4. References | [meshing2d.md](meshing2d.md#stage-4-references) |
| 5. Quadtree | [terrain.md](terrain.md#stage-5-quadtree) |
| 6. Meshing M1 | [meshing2d.md](meshing2d.md#stage-6-meshing-m1) |
| 6b. Meshing provenance check | [meshing2d.md](meshing2d.md#stage-6b-meshing-provenance-check) |
| 7. Envelope containment | [alpha-wrap-and-repair.md](alpha-wrap-and-repair.md#stage-7-envelope-containment) |
| 7b. Alpha wrap quality | [alpha-wrap-and-repair.md](alpha-wrap-and-repair.md#stage-7b-alpha-wrap-quality) |
| B. Boolean performance | [exact-boolean.md](exact-boolean.md#stage-b-boolean-performance) |
| R. SafeRounding | [exact-boolean.md](exact-boolean.md#stage-r-saferounding) |
| 8. Planned follow-ups | [platform.md](platform.md#stage-8-planned-follow-ups) |

#### Stages 0 to 3

Done on 2026-10-31: local CI green, the small fixes and the parts of #193 merged, the plans checked. Hidden tests replaced by [references](../design/ACCEPTANCE_STRATEGY.md)

Why this order: a reference must exist before an implementation is accepted against it. The quadtree comes before meshing because terrain phase 1 waits for it and its PR is further along.

#### Slot order (owner, 2026-11-01)

Limits on parallel work:

Order when slots free up (owner, 2026-11-01; on that day 4 large and 2 medium sessions ran, above the limit below):

The items of this order, by topic: 1 (Remesher) and 2 (wrap resolution) on
[alpha-wrap-and-repair.md](alpha-wrap-and-repair.md#remesher-slot-order-item-1); the terrain part of 2 on
[terrain.md](terrain.md#terrain-session-record-2026-11-01-to-2026-11-03); 3 (meshing) on
[meshing2d.md](meshing2d.md#meshing-session-record-slot-order-item-3-2026-11-01-to-2026-11-07); 4 on
[alpha-wrap-and-repair.md](alpha-wrap-and-repair.md#wrap-resolution-7b-reducer-and-remesher-sessions-slot-order-item-4).
The session limits that followed it are in the [roadmap index](../ROADMAP.md#session-limits).

5. Decided 2026-11-01: stage 7b follows the wrap-resolution session (see 7b). The question whether a large performance session pauses for Terrain is moot: the boolean-performance session finished (#256) and Terrain runs.

#### Phase 1: foundation

- [x] Targets net10.0 and netstandard2.0
- [x] CI on GitHub Actions: tests on net10.0 (Linux) and net48 (netstandard2.0 build, Windows), pack, package smoke test
- [x] Package metadata: SPDX license, README, XML docs, symbols
- [x] Mesh formats PLY, 3MF, glTF/GLB (read and write)
- Package ID and publishing: moved to Release, after the library "just works"
- [x] API docs and examples per operation ([OPERATIONS.md](../OPERATIONS.md), examples run as tests)

- [x] Fork feature overview: [Additions in this fork](../../README.md#additions-in-this-fork), grouped by capability
  with links to implemented operations and integration guides.

#### Phase 2: robustness, feature table

The Phase 2 items are on [exact-boolean.md](exact-boolean.md#phase-2-items-on-exact-booleans) and
[alpha-wrap-and-repair.md](alpha-wrap-and-repair.md#phase-2-items-alpha-wrap-remeshers-feature-detection-notcleanreason).

| Feature | Research / port source | Why |
|---|---|---|
| Exact, robust mesh booleans and arrangements | Zhou et al. 2016 *Mesh Arrangements for Solid Geometry*; Cherchi et al. 2020/2022 (indirect predicates); Trettner et al. 2022 *EMBER*; Manifold (Apache-2.0) | The biggest gap; the existing `MeshBoolean` is not robust, the winding-number variant is voxel based. `ExactPredicates` is the base. |
| Alpha wrapping / shrink wrap | Portaneri et al. 2022 *Alpha Wrapping with an Offset* | Guaranteed watertight manifold output from any input: scan repair, 3D printing, biomedical CAD. Complements `MeshAutoRepair`. |
