# Plan: Exact Differentiable Volumetric CSG (Phase 2 — Volumetric Moments & $SE(3)$ Pullbacks; Spike `3-0` Passed)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 3)
- **Prior prototype:** [EXACT_BOOLEAN_GRADIENTS_2026-11.md](../research/EXACT_BOOLEAN_GRADIENTS_2026-11.md), `mesh_ops/BooleanVolumeGradient.cs`, `benchmarks/BooleanVolumeGradient/README.md`
- **Kind:** Phase 2 — Spike `3-0` **PASSED (STRONG GO, 2026-11-28)**; ready for Tier-M public API implementation
- **Tier:** M (builds on `ExactMeshBoolean.RetainClassification` + exact Hadamard integral formulas; no new geometric predicates or error bounds; $\nabla A$ explicitly excluded)
- **Plan status:** spike-verified (**STRONG GO**), ready for implementation, 2026-11-28 (Berlin). See [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md).

## Current state (Post-Spike `3-0`)

- `BooleanVolumeGradient` (`mesh_ops/BooleanVolumeGradient.cs`) computes exact volume gradients $\partial V / \partial x_v$ from the exact unrounded arrangement $E_s$ (before `MeshRoundingRepair`) via the Hadamard shape derivative over kept pieces, exposed in `g3cli boolean --volume-gradient`.
- **Spike `3-0` empirical results (2026-11-28, uncommitted local scratch probe on `master` `2959dfaa`, `net10.0` Release, 5 reps median):**
  - **Exact $V, M_1, M_2, SE(3)$ finite-difference agreement:** `1e-10..1e-8` across `identity`, `rotated`, and `scaled_2e-10`; `4.9e-7..1.45e-5` on `UTM_1e7_rot` (at $h = 10^{-3}$, where `1 ulp ≈ 9.3e-10` at `5.5e6` sets the binary64 central-FD cancellation floor) — **zero missed intersection-curve terms**.
  - **Untouched-triangle fast path (`49k + 49k` spheres):** **92.4%–95.8%** of kept pieces are untouched input triangles. Current `BooleanVolumeGradient` adds **137%–171%** overhead (`676–689 ms` vs. `402–412 ms` boolean) due to `BigInteger` `arr.Exact(id)` on every untouched triangle vertex. The untouched-triangle fast path cuts `fastV` to **`5.8–9.7 ms` (`1.9%–2.4%` of boolean — ~88× faster)** and full `V + M1 + M2` (`fastVM1M2`) to **`37.3–66.1 ms` (`13.5%–16.4%` of boolean — 11× faster than current volume-only gradient)**!
  - **Unrounded optimization mode (`SkipMeshOutput` + `fastV`):** **2.2×–3.9× faster per optimization step**.
  - **Z3 formal verification (`tools/Z3Pilot/boolean_next_phase_spikes.py`):** proved `TriSq`, `TriCube`, `TriInt2`, and `TriInt3` exact over all reals (`4 UNSAT + 4 SAT`).
- **Conceptual audit result:**
  - **Volumetric moments ($V$, Center of Mass $M_1$, Inertia Tensor $M_2$, weighted depth/buoyancy/cut-fill integrals $\int_\Omega f(x)\,dx$) are 100% airtight:** by the Reynolds transport / divergence theorem, the intersection-curve boundary terms cancel identically across all continuous integrands $f(x)$! Only kept-face integrals are needed; flush-contact kinks are already detected and reported cleanly.
  - **Surface Area gradient ($\nabla A$) is explicitly PARKED:** it does NOT cancel along intersection curves — it requires Tier-L seam provenance, exhibits a $1/\sin\theta$ dihedral singularity at grazing intersections, and has non-smooth edge-on-face kinks.

## Goal

- Ship a fast, exact, public differentiable volumetric CSG engine (`BooleanVolumetricDifferentiator`) supporting `Volume`, `CenterOfMass` ($M_1$), `InertiaTensor` ($M_2$), linear/polynomial depth integrals (buoyancy, cut-fill), **$SE(3)$ rigid-body pose pullbacks** (`PullbackSE3`: $\partial L / \partial t$, $\partial L / \partial \omega$ via exact skew-symmetric cross product), generic **parametric pullback** (`PullbackParameters`), the spike-verified **untouched-triangle fast path** (`1.9%–2.4%` overhead for $V$, `13.5%–16.4%` for $V + M_1 + M_2$), deterministic parallel block reduction, and unrounded optimization loop mode (`SkipMeshOutput`).

## Scope

- `mesh_ops/BooleanVolumeGradient.cs`: untouched-triangle fast path using original double coordinates + local origin shift, deterministic parallel block accumulation, $M_1$ & $M_2$ exact integrators, `SkipMeshOutput` evaluation mode.
- `mesh_ops/BooleanVolumetricDifferentiator.cs` (new public facade): `BooleanVolumetricDifferentiator`, `RigidBodyGradient`, `PullbackSE3`, `PullbackParameters`, weighted polynomial integral support.
- `tools/g3cli/VolumeGradientOutput.cs` & `Commands.cs`: optional `--moments-gradient` / `se3_gradient` JSON output.
- `benchmarks/BooleanVolumeGradient/`: add committed `moments-fd` & `fast-path` benchmark modes to record formal before/after numbers.
- `tests/geometry3Sharp.Tests/BooleanVolumetricGradientTests.cs`.

## Non-goals

- **No Surface Area gradient ($\nabla A$)** (explicitly parked; see umbrella study §5).
- No change to `ExactMeshBoolean` default behavior or existing `BooleanVolumeGradient` volume results (bit-identical volume gradients preserved).
- No smooth/SDF approximation — strictly exact double-precision mesh arrangement derivatives.

## Interface

```csharp
public sealed class BooleanVolumetricDifferentiator
{
    public bool ComputeVolume = true;
    public bool ComputeFirstMoment = false;   // M1 -> CenterOfMass
    public bool ComputeSecondMoment = false;  // M2 -> InertiaTensor
    public bool SkipMeshOutput = false;       // unrounded optimization loop mode (skips SafeRounding & DMesh3 build)
    // Optional linear/quadratic weight w(x) = c0 + c1·x for cut-fill / hydrostatic buoyancy:
    public bool ComputeWeightedIntegral = false;

    // Pullback helpers (exact chain rule from per-vertex dF/dx_v):
    public static RigidBodyGradient PullbackSE3(IReadOnlyList<Vector3d> vertexGradients, DMesh3 inputMesh, Vector3d pivot);
    public static double[] PullbackParameters(IReadOnlyList<Vector3d> vertexGradients, Func<int, Vector3d[]> dVertex_dParams, int paramCount);
}
```
Outputs include exact `Value`, sound `ErrorBound`, `FlushContacts` (kink indicator), and per-input-mesh vertex gradients (plus `SE(3)` translation/rotation torque gradients).

## Look before you build

1. Library: `mesh_ops/BooleanVolumeGradient.cs`, `mesh_ops/ExactMeshBoolean.cs` (`RetainClassification`, `Arrangement`), `benchmarks/BooleanVolumeGradient/`.
2. Literature / prior art: [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md), [EXACT_BOOLEAN_GRADIENTS_2026-11.md](../research/EXACT_BOOLEAN_GRADIENTS_2026-11.md); Edelsbrunner & Koehl (2003) volume/moment derivatives; Mirtich (1996) polyhedral mass properties (local-origin shifted); `vcad` Nov 2026 frozen-tessellation B-rep AD (for contrast — ours differentiates exact moving intersections!).

## Steps

1. Implement the untouched-triangle fast path (triangles with 1 unsplit kept piece compute exact gradients directly from input vertex triples in local-origin frame without `BigInteger` point materialization) + deterministic parallel block reduction (bit-identical across thread counts).
2. Implement `ComputeFirstMoment`, `ComputeSecondMoment`, weighted linear depth integral, `SkipMeshOutput`, and `BooleanVolumetricDifferentiator` (`PullbackSE3` / `PullbackParameters`), adding C# source contracts to `tools/Z3Pilot/boolean_next_phase_spikes.py` for the moment formulas.
3. Add committed `benchmarks/BooleanVolumeGradient/` modes to produce formal reproducible before/after timings.
4. Add an end-to-end procedural inverse-design test (e.g., optimize $SE(3)$ pose of a ballast/cutout solid via L-BFGS / gradient descent to hit target volume + CoM + principal inertia alignment — proving zero-noise convergence across topology-changing cuts!).
5. Add `Similarity.All()`, reuse, determinism, flush-contact kink reporting, and hand-mutant tests.

## Acceptance

- Existing `BooleanVolumeGradient` tests remain bit-identical.
- $V, M_1, M_2$ and $SE(3)$ translation/rotation gradients match central finite differences to `< 1e-8` relative on unit, rotated, and `2^-10`/`2^10`-scaled smooth configurations and `< 1e-4` on rotated UTM `1e7` frames (binary64 central-FD cancellation limit), while exact-vs-exact `Similarity.All()` frame invariance matches to `< 1e-12` relative.
- Committed `benchmarks/BooleanVolumeGradient` confirms untouched-triangle fast path brings $V$ overhead below `5%` and full $V + M_1 + M_2$ overhead below `20%` of boolean time.
- End-to-end optimization test converges cleanly across a crossing/topology event using `SkipMeshOutput = true`.
- Serial == parallel bit-identical; full local CI, `dotnet pack`, and independent review green.

## Sources and licences

Mirtich (J. Graphics Tools 1996, polyhedral mass properties) and Hadamard shape derivative literature (papers only); internal `BooleanVolumeGradient.cs`; no external code ports.

## Budget and stop rule

Spike `3-0` complete (2026-11-28, **STRONG GO**). Implementation budget: 1–2 M sessions.
