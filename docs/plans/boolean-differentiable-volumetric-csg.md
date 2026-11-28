# Plan: Exact Differentiable Volumetric CSG (Phase 2 — Spike `3-0` → Volumetric Moments & $SE(3)$ Pullbacks)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 3)
- **Prior prototype:** [EXACT_BOOLEAN_GRADIENTS_2026-11.md](../research/EXACT_BOOLEAN_GRADIENTS_2026-11.md), `mesh_ops/BooleanVolumeGradient.cs`, `benchmarks/BooleanVolumeGradient/README.md`
- **Kind:** Phase 2 — short pre-implementation Spike `3-0` (1–2 days) followed by Tier-M public API implementation
- **Tier:** M (builds on `ExactMeshBoolean.RetainClassification` + exact Hadamard integral formulas; no new geometric predicates or error bounds; $\nabla A$ explicitly excluded)
- **Plan status:** draft, 2026-11-28 (Berlin).

## Current state

- `BooleanVolumeGradient` (`mesh_ops/BooleanVolumeGradient.cs`) computes exact volume gradients $\partial V / \partial x_v$ from the exact unrounded arrangement $E_s$ (before `MeshRoundingRepair`) via the Hadamard shape derivative over kept pieces, exposed in `g3cli boolean --volume-gradient`.
- Measured overhead in [EXACT_BOOLEAN_GRADIENTS_2026-11.md](../research/EXACT_BOOLEAN_GRADIENTS_2026-11.md) is **44–142%** of the boolean wall time because every kept triangle (including untouched input triangles far from any intersection) runs the full exact piece-coordinate accumulation, and optimization loops currently pay for `SafeRounding` + mesh assembly on every gradient step even when only scalars/gradients are needed.
- **Conceptual audit result:**
  - **Volumetric moments ($V$, Center of Mass $M_1$, Inertia Tensor $M_2$, weighted depth/buoyancy/cut-fill integrals $\int_\Omega f(x)\,dx$) are 100% airtight:** by the Reynolds transport / divergence theorem, the intersection-curve boundary terms cancel identically across all continuous integrands $f(x)$! Only kept-face integrals are needed; flush-contact kinks are already detected and reported cleanly.
  - **Surface Area gradient ($\nabla A$) is explicitly PARKED:** it does NOT cancel along intersection curves — it requires Tier-L seam provenance, exhibits a $1/\sin\theta$ dihedral singularity at grazing intersections, and has non-smooth edge-on-face kinks.

## Goal

1. **Spike `3-0` (1–2 days, gate before public API):**
   - Derive and verify exact closed-form vertex gradients for **first moment $M_1 = \int_\Omega x\,dx$ (Center of Mass)** and **second moment $M_2 = \int_\Omega x x^T\,dx$ (Inertia Tensor)** against central finite differences across `Similarity.All()`, rotated UTM frames, and flush contacts (confirming zero missed seam terms).
   - Measure the upper-bound speedup of the **untouched-triangle fast path** + **unrounded evaluation mode** (`SkipRoundingAndAssembly`) on `benchmarks/BooleanVolumeGradient` (target: cut gradient overhead from 44–142% to `< 15–20%` of boolean time, and cut per-iteration optimization time by avoiding `SafeRounding`).
2. **Phase 2 Implementation (after `3-0` green):**
   - Ship a fast, exact, public differentiable volumetric CSG engine supporting `Volume`, `CenterOfMass`, `InertiaTensor`, linear/polynomial depth integrals (buoyancy, cut-fill), **$SE(3)$ rigid-body pose pullbacks** ($\partial L / \partial t$, $\partial L / \partial \omega$ via exact skew-symmetric cross product), generic **parametric pullback** (`ChainRule(dx/dp)`), untouched-triangle fast path, deterministic parallel block reduction, and unrounded optimization loop mode (`ComputeGradientsOnly`).

## Scope

- **Spike `3-0` (throwaway/benchmark probe first):**
  - `benchmarks/BooleanVolumeGradient/` (add `moments-fd` finite-difference oracle + `fast-path` timing probe; report numbers in `docs/research/`).
- **Implementation PR (if `3-0` passes):**
  - `mesh_ops/BooleanVolumeGradient.cs` (untouched-triangle fast path using original double coordinates + local origin shift, deterministic parallel block accumulation, $M_1$ & $M_2$ exact integrators, `SkipRounding` evaluation mode).
  - `mesh_ops/BooleanShapeDifferentiation.cs` (new public facade: `VolumetricMoments`, `RigidBodyPullback` for $SE(3)$ translations/rotations, `ParametricPullback` helper, weighted polynomial integral support).
  - `tools/g3cli/VolumeGradientOutput.cs` & `Commands.cs` (optional `--moments-gradient` / `se3_gradient` JSON output).
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
2. Literature / prior art: [EXACT_BOOLEAN_GRADIENTS_2026-11.md](../research/EXACT_BOOLEAN_GRADIENTS_2026-11.md); Edelsbrunner & Koehl (2003) volume/moment derivatives; Mirtich (1996) polyhedral mass properties (local-origin shifted); `vcad` Nov 2026 frozen-tessellation B-rep AD (for contrast — ours differentiates exact moving intersections!).

## Steps

1. **Spike `3-0`:**
   - Prototype $M_1$ and $M_2$ Hadamard face integrals in local origin frame (exact rational / compensated double check) and verify central finite differences converge at $O(h^2)$ down to `< 1e-12` across rotated boxes, overlapping spheres, UTM `1e7`, and n-ary CSG.
   - Measure untouched-triangle fast path + `SkipMeshOutput` on `benchmarks/BooleanVolumeGradient`; verify gradient overhead drops below 20%.
2. **Implementation PR (gated on `3-0`):**
   - Implement untouched-triangle fast path (triangles with 1 unsplit kept piece compute exact gradient directly from input vertex triples without `BigInteger` point materialization) + deterministic parallel block reduction (bit-identical across thread counts).
   - Implement `ComputeFirstMoment`, `ComputeSecondMoment`, weighted linear depth integral, `SkipMeshOutput`, and `PullbackSE3` / `PullbackParameters`.
   - Add an end-to-end procedural inverse-design test (e.g., optimize $SE(3)$ pose of a ballast/cutout solid via L-BFGS / gradient descent to hit target volume + CoM + principal inertia alignment — proving zero-noise convergence across topology-changing cuts!).
   - Add `Similarity.All()`, reuse, determinism, flush-contact kink reporting, and hand-mutant tests.

## Acceptance

- **Spike `3-0` gate:** $M_1$ & $M_2$ central-difference agreement `< 1e-11` relative on smooth configurations across `Similarity.All()`; untouched-triangle fast path cuts gradient overhead to `< 20%` of boolean time.
- **Implementation:**
  - Existing `BooleanVolumeGradient` tests remain bit-identical.
  - $SE(3)$ translation & rotation gradients match central finite differences to `< 1e-11` on boxes, cylinders, and spheres (including rotated UTM frames).
  - End-to-end optimization test converges cleanly across a crossing/topology event using `SkipMeshOutput = true`.
  - Serial == parallel bit-identical; full local CI, `dotnet pack`, and independent review green.

## Sources and licences

Mirtich (J. Graphics Tools 1996, polyhedral mass properties) and Hadamard shape derivative literature (papers only); internal `BooleanVolumeGradient.cs`; no external code ports.

## Budget and stop rule

- Spike `3-0`: 1–2 days. Stop the untouched-triangle fast path if measured speedup is `< 30%` of gradient time (focus on `SkipMeshOutput` + moments only).
- Implementation: 1–2 M sessions after `3-0` green.
