# FPTaylor installation and smoke test

This is a test-only installation and tool smoke test, not a certificate of a geometry predicate or its
error bound. FPTaylor analyzes a mathematical expression in its specified ranges and rounding model; its
result does not establish that a C# implementation, exceptional values, subnormals or every input range match
that model. Model-to-source correspondence and bounds review remain required for a predicate pilot.

The installer builds pinned official sources into a user-owned directory. On Linux x86-64 it needs `curl`,
`tar`, `sha256sum`, `make`, a C toolchain, Python 3 and network access. The portable `simple_interval` build
and the `bb-eval` optimizer are used; trigonometric functions and constraints are not covered by this smoke.
The default `bb` optimizer does not build successfully with this portable configuration (incompatible
Interval interfaces), so `run.sh` explicitly selects `bb-eval`.

```sh
JOBS=4 bash tools/FPTaylorPilot/install.sh "$PWD/../fptaylor-local"
base="$PWD/../fptaylor-local/FPTaylor-b5a77cae348400f21f83512210d9f43c4bffb381"
bash tools/FPTaylorPilot/run.sh "$base" tools/FPTaylorPilot/smoke.txt
python3 -m unittest discover -s tools/FPTaylorPilot -p 'test_*.py' -v
```

Use a fresh installation directory. Downloaded archives are verified by SHA-256 before extraction; the
script refuses to overwrite existing source directories. Build failures stop the installer. The runner uses
an isolated temporary working directory, returns nonzero on a failed tool or absent, duplicate, non-finite or
negative exact absolute-error result, and prints the full report. It accepts exactly one expression with an
absolute error result. The included example checks binary64 `x*x` over `[1,2]`; it is a setup check only.

Sources: [FPTaylor](https://github.com/soarlab/FPTaylor) b5a77cae (MIT),
[OCaml](https://github.com/ocaml/ocaml) 4.14.2 and [Num](https://github.com/ocaml/num) 1.5
(both LGPL-2.1 with the OCaml linking exception). These are downloaded and built locally; they are not
shipped in the library, the NuGet package, or this repository. FPTaylor itself follows Solovyev et al.,
[*Rigorous Estimation of Floating-Point Round-off Errors with Symbolic Taylor Expansions*](https://soarlab.org/2015/04/fm2015-sjrg/), FM 2015.

Local validation (2026-11-08, Linux x86-64): the pinned archives were checksum-verified, OCaml built and
installed, then the remaining installer commands built Num and FPTaylor. The steps were run in two pieces
because the installer was edited while the first build was running. The resulting installation ran
`run.sh` on `smoke.txt` successfully. All four Python tests pass; a mutation that converts the reported bound
through binary `float` is caught by the negative-underflow test. Library targets, both net48 test-project
builds and both API-validating package builds pass. Independent review found the underflow issue; the fix
and subsequent delta were reviewed with no remaining findings.

## First comparison: one Orient3dLPI leaf

`orient3dlpi_ux.txt` models just `ux = s.x - r.x` in `indirect.spec`, with binary64 inputs
`s.x` in `[1,2]` and `r.x` in `[0,0.5]`. Under round-to-nearest-even and the generator's existing leaf
rule, the rounded `ux` lies in `[0.5,2]`; its computed magnitude is `|ux|`, and the rule gives
`|error| <= 2^-53 |ux| <= 2^-52`. The gate and the rest of the expression graph are not modeled.
The FPTaylor result below is a separate absolute bound for this restricted range; a tighter result on this
range does not establish that the generator's relative rule holds in general.

```sh
python3 tools/FPTaylorPilot/compare_ux.py "$base"
```

On FPTaylor b5a77cae with `bb-eval`, this returned `0x1p-53`, which is below the conservative
`2^-52` upper bound derived from the generator rule. The comparison reads FPTaylor's hexadecimal result
as an exact rational, fails when it exceeds the derived bound, and checks the model and relevant source
snippets for drift. It is an initial **single-leaf, bounded-domain** check, not a proof of Orient3dLPI,
its filter gate, other inputs, or all binary64 exceptional and underflow cases.

## Orient3dLPI `nx`: a composed filter expression

`orient3dlpi_nx.txt` models the operations `uy = s.y-r.y`, `uz = s.z-r.z`, `wy = t.y-r.y`,
`wz = t.z-r.z`, then `nx = uy*wz - uz*wy`, with each operation rounded to binary64. The model preserves the
shared coordinates `r.y` and `r.z`; all coordinates `s.y`, `s.z`, `t.y`, `t.z` are in `[1,2]` and `r.y`,
`r.z` are in `[0,0.5]`. The model includes inputs outside the generator's gate; the generator comparison
applies to the subset satisfying it. No sign decision or complete predicate is modeled.

Let `u = 2^-53`. Each leaf magnitude is at most 2, each rounded product magnitude at most 4, and the
computed sum of their magnitudes at most 8. On gated inputs, the generator's product rule gives
`P = (u + 2u + u²)/(1-u)`, and its subtraction rule gives `C = (u+P)/(1-u)`. Therefore its absolute bound
for this domain is `8C = 288230376151711744/81129638414606663681390495662081`. FPTaylor returned the
upper bound `0x1.0080000000001p-49` (about `1.78e-15`), below the generator bound (about `3.55e-15`).
These are bounds for this domain, not timings or a general proof of the filter.

```sh
python3 tools/FPTaylorPilot/compare_nx.py "$base"
```

The check preserves the tool result as an exact rational, requires it not to exceed `8C`, and fails on
changes to the modeled input, source expressions, or generator rules. The input includes potentially
subnormal coordinates; no claim is made for an ungated execution of the real filter. The other cross-product
components, gate, sign decision and complete predicate remain outside this check.

## Further Orient3dLPI components: ny, nz and the denominator d

Issue #625 extends the comparisons to the other cross-product components and the line-plane denominator
of `Orient3dLPI` (`indirect.spec`). Each model is one expression under the same restricted domain as above:
`p`, `q`, `s`, `t` coordinates in `[1,2]` and `r` coordinates in `[0,0.5]`, with each operation rounded to
binary64 as in the spec's leaves.

- `orient3dlpi_ny.txt`, `orient3dlpi_nz.txt`: `ny = uz*wx - ux*wz` and `nz = ux*wy - uy*wx`. Both have
  the structure of `nx`, so the generator bound is the same `8C` as for `nx`.
- `orient3dlpi_d.txt`: the full chain `nx, ny, nz`, then `lp = nx*prx + ny*pry + nz*prz`, `lq` likewise with
  `q - r`, and `d = lq - lp`. Magnitudes over the domain: leaves at most 2, `|nx|,|ny|,|nz| <= 8`, each
  product of a component and a leaf at most 16, `lp` and `lq` at most 48, `d` at most 96. Applying the
  generator's relative rules to this tree gives the absolute bound `96 C_d`, where
  `C_d = 9u + O(u^2)` and `96 C_d` is about `864.0000000000005 u` (`u = 2^-53`).

```sh
python3 tools/FPTaylorPilot/compare_lpi_components.py "$base" ny nz d
python3 -m unittest tools/FPTaylorPilot/test_compare_lpi_components.py
```

| Expression | FPTaylor bound (hex) | FPTaylor (decimal) | Generator bound (decimal) | Ratio | Result |
|---|---|---|---|---|---|
| `ny` | `0x1.0080000000001p-49` | 1.78e-15 | 3.55e-15 | 0.501 | pass |
| `nz` | `0x1.0080000000001p-49` | 1.78e-15 | 3.55e-15 | 0.501 | pass |
| `d` | `0x1.c480000000001p-46` | 2.51e-14 | 9.59e-14 | 0.262 | pass |

The run was on Linux x86-64 in a Podman container (Ubuntu 24.04), since the owner's machine has no WSL
distribution; `install.sh` and `run.sh` were run unchanged there. `d` took about 9 minutes in the tool,
so it is run on demand, not in CI. The leaves `prx`..`qrz` (the `ux` form, same ranges) were not run
separately. As for the earlier comparisons, each result is a bounded-domain, single-expression check
(restricted ranges, no gate, no sign decision), not a proof of the filter.
