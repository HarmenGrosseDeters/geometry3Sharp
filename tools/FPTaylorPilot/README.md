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

## Further Orient3dLPI and Orient3dTPI expressions (issue #625)

These comparisons extend the two above to the other leaves and composed expressions of `indirect.spec`.
Each model is one expression under a restricted domain: points in `[1,2]`, `r` (LPI) in `[0,0.5]`, and the
Orient3d point `c` (or `o` in the TPI model) in `[1,2]`. Each operation is rounded to binary64, as in the spec.
The generator bound is the relative rule of `Compiler.cs` applied to the expression tree, times the magnitude
of the expression over that domain; the derivations are in `compare_filter_expressions.py`.

- LPI leaf `prx = p.x - r.x`: magnitude at most 2, bound `2u`.
- `ny`, `nz`: the same structure as `nx`, bound `8C` as in the `nx` comparison.
- `d = lq - lp` with the full chain `nx, ny, nz`, `lp = nx*prx + ny*pry + nz*prz`, `lq` likewise with `q - r`.
  Magnitudes: `|nx|,|ny|,|nz| <= 8`, `lp, lq <= 48`, `d <= 96`. Bound `96 C_d` (about `864u`).
- LPI coordinate numerator `Nx = pcx*lq - qcx*lp` with `pcx = p.x - c.x`, `|pcx| <= 1`: magnitude 96,
  bound `96 C` (about `1056u`).
- TPI leaf `u1_x = b1.x - a1.x`: magnitude at most 1, bound `u`.
- TPI `n1x = u1_y*w1_z - u1_z*w1_y`: magnitude at most 2, bound `2 C_nx` (about `8u`).
- TPI `D1 = n1x*a1cx + n1y*a1cy + n1z*a1cz` with `a1cx = a1.x - c.x`: magnitude 6, bound `6 C` (about `48u`).

```sh
python3 tools/FPTaylorPilot/compare_filter_expressions.py "$base"          # all expressions
python3 tools/FPTaylorPilot/compare_filter_expressions.py "$base" d coord_x  # a subset
python3 -m unittest tools/FPTaylorPilot/test_compare_filter_expressions.py
```

| Expression | FPTaylor bound (hex) | FPTaylor (decimal) | Generator bound (decimal) | Ratio | Result |
|---|---|---|---|---|---|
| LPI `prx` (leaf) | `0x1p-53` | 1.11e-16 | 2.22e-16 | 0.500 | pass |
| LPI `ny` | `0x1.0080000000001p-49` | 1.78e-15 | 3.55e-15 | 0.501 | pass |
| LPI `nz` | `0x1.0080000000001p-49` | 1.78e-15 | 3.55e-15 | 0.501 | pass |
| LPI `d` | `0x1.c480000000001p-46` | 2.51e-14 | 9.59e-14 | 0.262 | pass |
| LPI `Nx` (coordinate) | `0x1.16f8000000001p-45` | 3.10e-14 | 1.17e-13 | 0.264 | pass |
| TPI leaf `u1_x` | `0x1p-54` | 5.55e-17 | 1.11e-16 | 0.500 | pass |
| TPI `n1x` | `0x1.0100000000001p-51` | 4.46e-16 | 8.88e-16 | 0.502 | pass |
| TPI `D1` | `0x1.3000000000001p-49` | 2.11e-15 | 5.33e-15 | 0.396 | pass |

The run was on Linux x86-64 in a Podman container (Ubuntu 24.04), since the owner's machine has no WSL
distribution; `install.sh` and `run.sh` ran unchanged there. The `d` run took about 9 minutes and `Nx` about
17 minutes (the full set about 35 minutes), so these are on-demand checks, not CI.

Not covered: the full determinant `t1 - t2 + t3` and the sign decision of `Orient3dLPI` and `Orient3dTPI`,
the other TPI planes and the other cross-product components of the TPI model, and any input outside the
restricted ranges above. As for the earlier comparisons, each result is a bounded-domain, single-expression
check, not a proof of the filter.
