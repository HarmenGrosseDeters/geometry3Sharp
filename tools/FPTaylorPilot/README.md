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
