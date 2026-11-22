# DeterminismReach

Static reachability of the sources of cross-runtime differences from the public operations
([DETERMINISM_CROSS_RUNTIME_2026-11.md](../../docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md) section 4). The
[harness](../DeterminismHarness/README.md) measures the operations it has a case for; this tool reads the compiled library
(Mono.Cecil, MIT) and says for every public type which sources its public methods can reach at all.

```sh
dotnet build tools/DeterminismHarness -c Release -f net10.0      # builds the library next to the harness
dotnet build tools/DeterminismReach -c Release
R="dotnet tools/DeterminismReach/bin/Release/net10.0/DeterminismReach.dll"
B=tools/DeterminismHarness/bin/Release/net10.0
$R self-test
$R report $B/geometry3Sharp.dll --docs docs/OPERATIONS.md --docs docs/guides --harness $B/DeterminismHarness.dll --tsv reach.tsv
$R report $B/geometry3Sharp.dll --why ExactMeshBoolean libm     # the shortest call path to a source
```

**Sources** (the rule numbers of [DETERMINISM_PROOF.md](../../docs/design/DETERMINISM_PROOF.md) section 5): `libm` (the
transcendental functions of `System.Math` / `MathF`, R1), `coremath` (the correctly rounded `g3.CoreMathPort.CoreMath`, which
is not followed further), `fma` (R3), `simd` (`System.Runtime.Intrinsics`, `Vector<T>`, R3), `float` (a conversion to or a
constant of `float`, R2), `parallel` (R12), `ids` (`Interlocked`, R13), `cpu-count`, `clock`, `random` (R9, R10), `hash-enum`
(enumeration of a `Dictionary` / `HashSet`, R8), `sort` (the unstable sorts, R11), `text` (parsing and formatting of doubles,
R17), `minmax` (`Math.Min / Max` of doubles, R4).

**Graph.** An edge is a call, a constructor call or a method whose address is taken (a lambda or delegate that is created
counts as called). `static` follows a virtual or interface call only to the method named at the call site: a lower bound.
`cha` also follows it to every override and implementation in the library (class hierarchy analysis): an upper bound for
code inside the library, which counts implementations an operation is never given (every `ImplicitFunction3d` for
`MarchingCubes`). Neither follows a delegate passed in from outside the library, or reflection. "Reaches" says that a path
exists, not that a result depends on the source: the harness and a reading of the path decide that.

**Operations.** The public types whose name appears in backticks in the given documentation files; `--harness` marks the
types the harness assembly references.

**Self-test.** `self-test` analyzes the tool's own assembly, whose fixtures (`SelfTest.cs`) have a known reachability, and
exits with 1 on a difference. Checked with hand mutants: dropping the edges of `ldftn` or `newobj`, the class hierarchy
edges, explicit interface implementations, inherited interface slots, the terminal types, the `float` opcodes, the
parameter type of `Min / Max` or the member filter of the hash containers each fails it.
