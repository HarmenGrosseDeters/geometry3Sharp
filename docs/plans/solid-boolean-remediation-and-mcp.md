# Plan: Agent-Native Remediation Contracts in `SolidBooleanPipeline` + `g3cli` / MCP (Phase 1 — 5A–5C)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 5)
- **Kind:** Phase 1 — ready to start (no pre-implementation spike needed)
- **Tier:** M (pure C# pipeline diagnostics/contracts + `tools/g3cli` CLI/MCP adapter; zero exact-kernel changes)
- **Plan status:** draft, 2026-11-28 (Berlin).

## Current state

- `SolidBooleanPipeline` (`mesh_ops/SolidBooleanPipeline.cs`) has rich refusal/reporting primitives (`PipelineStatus`, `SolidBooleanResultKind`, `Route`, `InputsWithWindingOutsideZeroOne`, `RoundingRemoved`, `Verification`) and `MeshSheetExtension` (`mesh_ops/MeshSheetExtension.cs`) already implements a non-mutating `Diagnose()` with 3D witnesses and first-order volume uncertainty `U`.
- **Gap 1:** Neither `SolidBooleanPipeline` nor `MeshSheetExtension` is exposed in `tools/g3cli` or `g3cli mcp` (which currently only expose raw `boolean` and `repair`).
- **Gap 2:** `SolidBooleanPipeline` has no fast pre-flight `Diagnose()`, no structured per-input openness/orientation metrics (`Audit F5`), `Report.Clean` defaults to `true` on failed runs (`Audit F7`), `ConfigureBoolean` can accidentally switch off `SafeRounding` / `CountInsideOutCells` (`Audit F6`), and refusals/warnings return human prose strings rather than typed, machine-actionable remediation descriptors.
- **Gap 3:** `tools/g3cli/McpServer.cs` runs serially over stdio without a `timeout_ms` cancellation guard, emits no MCP `2025-06-18` tool `annotations` (`readOnlyHint`, `idempotentHint`), and lacks a hard fail-closed policy gate for MCP agents (which otherwise ignore soft advisory warnings inside `code: "ok"` responses).

## Goal

1. **Layer 1 (`mesh_ops/SolidBooleanPipeline.cs`, pure C#, zero JSON/MCP deps):** Fix Audit F5/F6/F7 contract warts, add fast $O(V+T)$ `Diagnose()` + `SolidBooleanInputDiagnosis`, and emit deterministic, strongly-typed `SolidBooleanRemediation` options useful equally to CAD UI dialogs, batch C# pipelines, and AI agents.
2. **Layer 2 (`tools/g3cli/`):** Expose `solid_boolean`, `sheet_extend`, and read-only `solid_boolean_diagnose` / `sheet_extend_diagnose` tools in CLI and MCP, with `policy: "strict_agent"` (default on MCP, opt-in on CLI), `timeout_ms` cancellation, and MCP `2025-06-18` tool annotations.

## Scope

- **Stage 5A (Pipeline hygiene + `Diagnose()`):**
  - `mesh_ops/SolidBooleanPipeline.cs`:
    - Fix `Report.Clean` to be `false` whenever `Status != PipelineStatus.Success` (Audit F7).
    - Lock `SafeRounding = true` and `CountInsideOutCells = !AllowInsideOutInputs` after `ConfigureBoolean` (Audit F6).
    - Validate exact coordinate magnitudes upfront (`|x| == 0 || 2^-200 <= |x| <= 2^200`, no subnormals, matching `MeshRoundingRepair`) as `InvalidInput` with vertex witness (Audit F7).
    - Add `SolidBooleanInputDiagnosis` and non-mutating `SolidBooleanPipeline.Diagnose()` ($O(V+T)$ welded-edge scan: openness, boundary edge/loop count, boundary length ratio, orientation flips, signed volume, thickness, 3D `WitnessPoint`).
- **Stage 5B (Typed `SolidBooleanRemediation` contract):**
  - `mesh_ops/SolidBooleanPipeline.cs`: add `RemediationRiskClass` (`GeometryPreserving`, `BoundedGeometryChange`, `IntentDependent`, `InspectOnly`) and `SolidBooleanRemediation` (`ActionId`, `Risk`, `RequiresIntentConfirmation`, `TargetInput`, `ParameterDelta`, `Assumption`, `Metrics`, `HasWitnessPoint`, `WitnessPoint`), populated deterministically on every refusal and every actionable warning (`InputsWithWindingOutsideZeroOne`, `EmptyAfterRounding`, `SolidWithContacts`, reduction `StopReason != TargetReached`).
- **Stage 5C (`g3cli` + MCP tools & hardening):**
  - `tools/g3cli/SolidBooleanCommand.cs` (new): `solid_boolean`, `solid_boolean_diagnose`, `sheet_extend`, `sheet_extend_diagnose` (shared core implementation, `--diagnose-only` flag in CLI, separate read-only tools in MCP so `readOnlyHint: true` enables auto-approval).
  - `tools/g3cli/McpServer.cs`: emit MCP `2025-06-18` tool `annotations` (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint: false`), server-level `--timeout-ms` default + per-tool `timeout_ms` wired to `CancellationTokenSource`, and updated `initialize` instructions.
  - `tools/g3cli/docs/mesh-commands.md`, `tools/g3cli/docs/mcp-reference.md`.
- **Tests:**
  - `tests/geometry3Sharp.Tests/SolidBooleanRemediationTests.cs` (covers every status/warning mapping, `Diagnose()` accuracy & invariance, `ParameterDelta` schema parity test verifying every emitted key maps to a real pipeline/CLI parameter, determinism, reuse).
  - `tests/g3cli.Tests/G3CliSolidBooleanMcpTests.cs` (JSON-RPC end-to-end tests: diagnose → refuse → explicit retry → verified `Solid`; `strict_agent` blocking on `EmptyAfterRounding` & winding-2 cavity; `timeout_ms` clean cancellation; `tools/list` annotations).

## Non-goals

- **No silent "auto-magic" healing or automatic intent guessing** (`InputRepair = Refuse`, `Orientation = Refuse`, `AllowInsideOutInputs = false` stay the defaults).
- No JSON/MCP dependencies inside `mesh_ops/`.
- No stateful in-memory `ExactMesh` handle server in `g3cli mcp` (stays stateless file-based).
- No changes to `ExactMeshArrangement` / `ExactMeshBoolean` exact arithmetic.

## Interface

- `SolidBooleanPipeline`:
  - `public bool Diagnose()` (populates `Report.InputDiagnostics`, `Report.Remediations`, sets `Status` without running the boolean or modifying inputs)
  - `SolidBooleanReport.InputDiagnostics` (`List<SolidBooleanInputDiagnosis>`)
  - `SolidBooleanReport.Remediations` (`List<SolidBooleanRemediation>`)
- `g3cli` / MCP tools:
  - `solid_boolean`: `a`, `b`, `more`, `output`, `op` (`union|intersection|difference`), `input_roles`, `policy` (`default|strict_agent`), `verification` (`none|no_invented_surface|rounding_volume|standard`), `timeout_ms`, `max_pieces`, plus 1-to-1 `SolidBooleanPipeline` parameters matching `ParameterDelta` keys (`input_repair`, `orientation`, `allow_inside_out_inputs`, `input_grid`, `automatic_steps`, `min_thickness`, `thin_inputs`, `separate_self_contacts`, `target_triangle_count`, `target_fraction`, `max_deviation`, `pair_contacts_mode`).
  - `solid_boolean_diagnose` (`readOnlyHint: true`): same input args without `output`; runs `Diagnose()` only.
  - `sheet_extend` & `sheet_extend_diagnose` (`readOnlyHint: true`): wraps `MeshSheetExtension.Compute()` / `Diagnose()`, returning `runs[]`, `u_relative_to_workpiece`, `max_depth_relative`, `witness_point`, `status`.
- **`strict_agent` policy contract:** returns `code: "policy_refused"` and MCP `isError: true` (with structured `remediations[]`) whenever `ResultKind` is `EmptyAfterRounding` or `SolidNotClean`, `ThinComponentsKept > 0`, or `InputsWithWindingOutsideZeroOne` is non-empty.

## Look before you build

1. Library: `mesh_ops/SolidBooleanPipeline.cs`, `mesh_ops/MeshSheetExtension.cs`, `tools/g3cli/McpServer.cs`, `tools/g3cli/Commands.cs`, `tools/g3cli/CommandSpec.cs`.
2. Audit & prior art: [SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](../research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md) (F1–F7), [SHEET_EXTENSION.md](../design/SHEET_EXTENSION.md), [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) §2.

## Steps (one PR, clean commits per stage 5A / 5B / 5C)

1. **5A:** Fix Audit F5/F6/F7 warts and implement `SolidBooleanInputDiagnosis` + `Diagnose()`; verify existing `SolidBooleanPipelineTests` stay green (update any test that previously asserted `Clean == true` on a failed run, with explicit note).
2. **5B:** Implement `SolidBooleanRemediation` emission table across all statuses/warnings, with a reflection/schema parity test guaranteeing every `ParameterDelta` key is a valid pipeline/CLI option.
3. **5C:** Implement `SolidBooleanCommand.cs` (`solid_boolean`, `solid_boolean_diagnose`, `sheet_extend`, `sheet_extend_diagnose`), MCP `2025-06-18` annotations, `timeout_ms` cancellation, and `strict_agent` policy.
4. Add unit, invariance, determinism, reuse, and JSON-RPC end-to-end tests; update `tools/g3cli/docs/` and run `python3 tools/check_docs.py`.

## Acceptance

- Every `PipelineStatus != Success` yields `Report.Clean == false` and at least one structured `SolidBooleanRemediation` (or explicit `InspectOnly` witness) with no prose parsing needed.
- `Diagnose()` runs in `< 20 ms` on a 100k-triangle procedural mesh and correctly classifies closed, open-hole, open-sheet, flipped, inside-out, and out-of-range inputs across `Similarity.All()`.
- End-to-end MCP JSON-RPC tests verify: (a) open cutting sheet refused → `sheet_extend_diagnose` returns `u_relative` → `sheet_extend` → `solid_boolean` succeeds; (b) outward cavity wall blocked by `strict_agent` → `orient_by_nesting` remediation produces exact hollow volume; (c) `timeout_ms` cleanly returns `code: "cancelled"` without wedging subsequent MCP requests.
- `python3 tools/check_docs.py`, full local CI, `dotnet pack` API validation, and independent review green.

## Sources and licences

Internal `SolidBooleanPipeline`, `MeshSheetExtension`, `g3cli`; MCP 2025-06-18 specification; no external code ports.

## Budget and stop rule

1–2 M sessions (or split 5A+5B library PR and 5C `g3cli`/MCP PR if diff exceeds one-pass review size). Never weaken `Refuse` defaults to make a test pass.
