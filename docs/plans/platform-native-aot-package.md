# Plan: NativeAOT native package (record of owner "no" decision & reopen criteria)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Platform/QA/CI: NativeAOT package"
- **Kind:** parked — **owner decided NO (2026-11-17)**; kept here as a decision record and reopen checklist only
- **Tier:** S (record only); would be L if ever reopened (C ABI stability, multi-OS build/packaging, NativeAOT trimming)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only — **do not start implementation without a new owner decision.**

## Current state

- Feasibility spike completed and merged (#1052, 2026-11-17):
  - Design & numbers: [NATIVE_AOT_FEASIBILITY.md](../design/NATIVE_AOT_FEASIBILITY.md).
  - Proof-of-concept: `tools/NativeAotSpike/` (C ABI wrapper over `ExactMeshBoolean` / `ExactMeshImprint`, `c/caller.c`,
    managed driver, `run_spike.sh`) and `.github/workflows/native-aot.yml` (runs only on changes to the spike; builds on
    `linux-x64`, `osx-arm64`, publishes `win-x64`).
- **Owner decision (2026-11-17):** **No** — no NativeAOT package is planned ([ROADMAP.md](../ROADMAP.md#now),
  [OPEN_TOPICS.md](../OPEN_TOPICS.md) §4: "Next step / waiting on: None"). (Consistent with the 2026-11-16 decision that
  no public NuGet package is planned either.)

## Trigger / decision needed

Explicit owner reversal — e.g., a concrete C / C++ / Rust / Python downstream application requiring in-process calls
into `ExactMeshBoolean` / `MeshRobustRepair` where `g3cli` / MCP process invocation is too slow (sub-millisecond call
overhead or large in-memory mesh transfer).

## Goal

Preserve the #1052 feasibility findings and define the minimal reopen checklist so no future session restarts NativeAOT
packaging without a concrete trigger and owner sign-off.

## Scope

- Today: none (optional Tier S housekeeping: keep `tools/NativeAotSpike` and `.github/workflows/native-aot.yml` dormant,
  or remove them if the owner wants zero maintenance surface).
- Only if reopened: C ABI design doc (`docs/design/NATIVE_AOT_ABI.md`), `tools/NativeAot/` -> `native/` library project
  (`PublishAot=true`, zero trim/AOT warnings), C header (`g3native.h`), C/Python smoke tests, release workflow.

## Non-goals

No public NuGet / native package release; no modifications to core library public API; never introduce reflection or
unannotated dynamic code that regresses AOT compatibility unnecessarily.

## Interface

None unless reopened (if reopened: flat `extern "C"` ABI with explicit handle create/destroy, flat vertex/triangle
buffers, explicit status codes, zero exceptions across the ABI boundary).

## Look before you build

1. Library: `docs/design/NATIVE_AOT_FEASIBILITY.md`, `tools/NativeAotSpike/`, `.github/workflows/native-aot.yml`,
   `tools/g3cli/` (compare IPC / CLI overhead first — `g3cli` + MCP already serves headless cross-language callers).
2. Prior art: .NET 8-10 NativeAOT `UnmanagedCallersOnly` shared-library publishing; `cbindgen` / flat C ABI patterns
   (e.g. Manifold C bindings, Clipper2 C export).

## Steps (only if owner reopens)

1. Confirm `g3cli` / MCP IPC cannot meet the caller's latency or memory requirements (measured upper bound).
2. Freeze the C ABI surface (exact operations, ownership rules, error codes, thread safety, determinism under AOT).
3. Eliminate any remaining NativeAOT trim/AOT warnings in `geometry3Sharp_netstandard.csproj` (for `net10.0` target).
4. Add cross-platform C smoke tests (`linux-x64`, `win-x64`, `osx-arm64`) verifying bit-identical outputs between JIT
   and NativeAOT builds.

## Acceptance

- Today: none (closed/no-go; optional: ask owner if this row can be removed from `OPEN_TOPICS.md` §4 since "Next step:
  None" and `tools/NativeAotSpike` can stay archived).
- If reopened: zero NativeAOT warnings, C caller memory-leak clean (ASan/Valgrind), outputs bit-identical to managed
  `net10.0` on `DeterminismHarness` and `ExactMeshBoolean` fixtures.

## Sources and licences

Microsoft .NET NativeAOT documentation; own spike (#1052).

## Budget and stop rule

Zero budget while owner decision is "No". If reopened, gate on C ABI design approval before writing wrappers.

## Risks and open questions

- `tools/NativeAotSpike` and `.github/workflows/native-aot.yml` may bit-rot silently as the library evolves because the
  workflow only triggers on spike edits; decide during next cleanup whether to archive/delete the spike.
