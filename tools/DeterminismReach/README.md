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
counts as called). A method that touches a type of the library (a call, a field, a conversion, a parameter, a generic
argument) also reaches what the runtime and the BCL call on it: its static constructor, and its implementations of virtual
members declared outside the library (`IEnumerator.MoveNext` of an iterator, `IComparable.CompareTo`, `Equals`,
`GetHashCode`, `ToString`, `Dispose`). The roots of a type are its public and protected methods, the inherited ones
included. `static` follows a virtual or interface call only to the method named at the call site. `cha` also follows it
to every override and implementation in the library (class hierarchy analysis), which counts implementations an operation
is never given (every `ImplicitFunction3d` for `MarchingCubes`). "Reaches" says that a path exists, not that a result
depends on the source: the harness and a reading of the path decide that.

**Limits.** `static` is a lower bound and `cha` an upper bound only up to these: a delegate passed in from outside the
library and reflection are not followed; `hash-enum` sees `GetEnumerator`, `Keys`, `Values` and `CopyTo` of a
`Dictionary` / `HashSet`, not an enumeration by LINQ or through `IEnumerable<T>`; `text` sees `Parse` and `ToString` of
`double` / `float`, not `string.Format` or interpolation; `float` sees conversions to and constants of `float`, not
arithmetic on `float` fields; `parallel` sees `Parallel`, `ThreadPool`, `Task.Run` and thread constructors, not PLINQ. A
"does not reach" is therefore evidence, to be confirmed by a harness case, and a check built on it (a list of types that
may not gain a libm edge) guards against new direct uses, not against every path.

**Operations.** The public types whose name appears in backticks in the given documentation files; `--harness` marks the
types the harness assembly references.

**Self-test.** `self-test` analyzes the tool's own assembly, whose fixtures (`SelfTest.cs`) have a known reachability, and
exits with 1 on a difference. Checked with hand mutants: dropping the edges of `ldftn` or `newobj`, the class hierarchy
edges, explicit interface implementations, inherited interface slots, static constructors, the callbacks of outside
interfaces (also explicit ones), inherited roots, the terminal types, `conv.r4` or `ldc.r4`, the parameter type of
`Min / Max` or the member filter of the hash containers each fails it. Not killed: dropping the generic arguments of a
generic method call (the fixtures reach the type through a parameter as well).
