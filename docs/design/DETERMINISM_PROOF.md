# A proof method for determinism: study and options

Hard problem 11 ([research.md](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)), must-win
item of [ROADMAP.md](../ROADMAP.md) "Depth over breadth". [DETERMINISM.md](DETERMINISM.md) measures: 50 cases on six
configurations, and its section 6 says what that is worth: "absence of a difference is evidence on these inputs, not a
proof". This note asks what a proof can rest on, maps every known source of nondeterminism to a rule a tool can check,
and proposes where to start. It is a study: nothing is built, no library code changed. The code facts below come from
reading and `grep` on master 58644af (line counts are grep counts, so approximate); the findings of section 5 are
candidates from that reading; section 5.1 has what the probes and the workflow run of #786 made of them.

## 1. Summary

- **What "proof" can honestly mean here.** Not a mechanized proof: there is no formal semantics of C#, RyuJIT or the BCL
  to prove against (C has one, CompCert; .NET has none). What is reachable is a **machine-checked reduction**: a tool
  proves that inside a scope every dependence on the environment (schedule, hash values, tie order, libm, clock, core
  count, runtime-specific code) passes through a short, enumerated list of places; each place carries a written argument
  of one of five fixed forms (section 4.3) and a test that attacks exactly that argument. The claim then is: "the result
  is bit-identical, given the listed assumptions about the runtime (section 4.1) and the N listed obligations". Today the
  claim is "we saw no difference on 50 cases".
- **The property is noninterference**: the result must not depend on the environment. That is a 2-safety hyperproperty
  (it speaks about pairs of runs), which is why a test of single runs cannot show it, and why the established proof
  methods are information-flow type systems, deterministic-by-construction parallel languages and relational
  verification (section 3). The first two transfer to this code base as an analyzer; the third does not.
- **The exact code is the right first target, for a reason beyond its bans**: an exact predicate or an exact decision
  has a result fixed by mathematics. However it was computed (which filter stage, which thread, which cache state, which
  random ray), the value is the same. This "unique answer" principle discharges most of the environment dependence of
  the exact pipeline at once, and the correctness tests that already exist are its evidence. Floating-point operations
  (Remesher, Reducer, contouring) have no such principle: there the whole execution order must be shown fixed.
- **Recommendation** (section 7): first an **environment adversary** (option B: perturb every unspecified choice in
  tests; one PR, finds real dependences in the whole library and is the upper-bound check for the rest), then the
  **closed-world analyzer with an obligations ledger** (option A) in three stages: exact kernel, exact 2D / tree building
  blocks, exact Boolean (after 3.0). Deterministic ids by construction (C) and a type-level discipline (D) only where an
  obligation of A cannot be argued. Dynamic race detection (E) and formal verification (F) are not recommended.
- **Found on the way** (section 5): the survey named concrete candidates that the measurement could not see, among them
  platform libm in `TerrainSlope*` (inside the exact scope, outside G3E0004 and outside the harness), `Math.Min / Max`
  on signed zeros and out-of-range `double -> int` casts (both changed between .NET Framework and .NET), hash values
  used for sharding, two enumerations of dictionary values where the order picks the result, and 18 `#if NET` blocks in
  which net48 and net10 run different code.

## 2. The claim to prove

For an operation `F`, an input `x` (as bits) and two executions `e1`, `e2`: `F(x, e1)` and `F(x, e2)` are bit-identical.
The executions may differ in what DETERMINISM.md calls the levels:

| level | executions differ in | sources (section 5) |
|---|---|---|
| L0-S | schedule, thread count, core count | parallel loops, locks, `Interlocked`, `Environment.ProcessorCount` |
| L0-R | run, process | hash seeds, addresses, clock, static state left by earlier calls, uninitialized memory |
| L1 | runtime and OS on x64 (net48 CLR, net10 CoreCLR; Windows, Linux) | JIT, BCL behaviour, libm, `#if NET` code |
| L2 | architecture (arm64), text I/O | NaN bits, vector width, parsing and formatting |

Three parts of the claim need a definition before anything can be proven; the measurement never needed them:

- **The observable result.** Result mesh (vertex coordinates, triangle order and orientation, groups), status codes and
  the documented result properties. Not observable: timings (`PhaseMilliseconds`, `PhaseSeconds`), allocation counters,
  `PredicateStats`. They depend on the environment by nature, and several of them are public. Each operation in a proof
  scope lists its observable members; everything else is marked diagnostic (an attribute the analyzer reads).
- **The input domain.** Finite coordinates. NaN payloads and the sign of a generated NaN differ between x64 and arm64 by
  hardware, so no bit-identity claim covers a NaN in the result; operations in scope reject non-finite input first (most
  already do).
- **Completed runs.** Out of memory, stack overflow and cancellation (`CancellationToken` in `ExactMeshBoolean`,
  `ExactMeshArrangement`, `SnapRounding2`) are outside the claim: "if both runs complete, the results are identical".
  A wall-clock budget that changes a result would break the claim; none was found in the exact scope (the `Stopwatch`
  uses in `TerrainSlope*` feed only `PhaseSeconds`), and rule R9 keeps it that way.

## 3. Prior art

How others establish determinism, by what the argument rests on (papers and standards only; no code was read).

**By construction in the language (static, type-level).**
- Bernstein 1966 (conditions for parallel execution): two tasks commute if neither writes what the other reads or
  writes. Every "disjoint writes" argument is this.
- Deterministic Parallel Java (Bocchino et al., OOPSLA 2009): a type and effect system with regions; the compiler proves
  that parallel tasks have non-interfering effects, with reviewed `commutative` annotations for the operations it cannot
  prove. The closest analogue to `G3E0002` plus its allow list, and the model for option A: sound core, enumerated
  escapes.
- Internal determinism (Blelloch, Fineman, Gibbons, Shun, PPoPP 2012): an algorithm is internally deterministic if its
  trace, not only its result, is the same for every schedule; they show it is affordable with "deterministic
  reservations" (reserve by priority = iteration index, then commit), including Delaunay refinement. This is the recipe
  for option C. Their distinction external / internal determinism is the one between the exact code (result fixed,
  trace free) and the floating-point operations (trace must be fixed).
- LVars (Kuper, Newton, FHPC 2013) and the Par monad (Marlow, Newton, Peyton Jones 2011): shared state restricted to
  monotone, order-insensitive updates is deterministic by construction. The analogue here: concurrent insert into a set,
  followed by an order-insensitive read.
- Commutativity analysis (Rinard, Diniz, TOPLAS 1997): a compiler proves that operations commute. Shows the limit: it
  works for separable integer updates, not for floating-point sums.
- Parametricity (Reynolds 1983; Wadler, "Theorems for free!", 1989): code that treats a type abstractly (only equality,
  no order, no arithmetic) cannot depend on which values represent it. Applied to ids: if schedule-assigned ids are an
  abstract type, the result is invariant under any renaming of them, for free. The basis of option D.

**As an information-flow property.**
- Noninterference type systems (Volpano, Smith, Irvine 1996; survey Sabelfeld, Myers 2003): label the sources "high",
  the result "low", reject flows from high to low, with explicit, audited declassification. Determinism is the same
  shape with "environment" as high. Constant-time cryptography is verified this way at scale (ct-verif, Almeida et al.,
  USENIX Security 2016), which shows that a 2-safety property can be enforced on real code if the rule set is closed.
- Hyperproperties (Clarkson, Schneider 2010), self-composition (Barthe, D'Argenio, Rezk 2004), product programs (Barthe,
  Crespo, Kunz 2011): the general proof method, verify the program run twice. Needs a program logic and a prover for the
  language; nothing of the kind exists for C# at this size. Not transferable (option F).

**Floating point across compilers and machines.**
- IEEE 754-2008 / 2019 clause 11 ("Reproducible floating-point results"): results are reproducible if the program uses
  only the reproducible operations (`+ - * /`, `sqrt`, `fma`, conversions, comparisons), no value-changing optimization,
  and fixed evaluation widths; the transcendental functions are recommended, not required, to be correctly rounded. This
  is the standard's own form of rule R1.
- Monniaux 2008 ("The pitfalls of verifying floating-point computations"): the catalogue of what breaks it: x87 extended
  precision, double rounding, contraction to FMA, compiler reassociation, library functions.
- Boldo, Jourdan, Leroy, Melquiond 2015 ("Verified compilation of floating-point computations", CompCert): what a real
  proof needs, a compiler proven to preserve IEEE semantics. .NET has no such compiler; ECMA-335 I.12.1.3 even permits
  intermediate results at higher precision. So the JIT stays in the trusted base, validated by probes (section 4.1).
- CORE-MATH (Sibidanov, Zimmermann, Glondu 2022): correctly rounded libm, hence identical everywhere; already in the
  library as `CoreMath` (#617).
- Reproducible summation (Demmel, Nguyen 2013 / 2015; Collange, Defour, Graillat, Iakymchuk 2015): a sum that is
  independent of the order, at 2 to 8 times the cost. The alternative the library already uses is cheaper: a partition
  that does not depend on the thread count (`PartitionedReduction`), which fixes the order instead.
- Practice in deterministic simulation (lockstep games, physics engines with a cross-platform mode, blockchain
  runtimes): either no floating point at all (fixed point, or floats banned by a validator), or exactly the rules above
  (no libm, no FMA, no reassociation) enforced by build flags. Sources here are documentation, not papers.

**Unspecified library behaviour.**
- NonDex (Shi, Gyori, Legunsen, Marinov, ICST 2016): code that assumes a deterministic implementation of a
  nondeterministic specification (iteration order of a hash set, order of equal elements after a sort). They model 31
  such Java APIs and run tests under implementations that explore the specified freedom; a follow-up finds 275 wrong
  assumptions in 200 projects. The model for option B: sample the environment adversarially, not the platforms.

**Checked at run time.**
- Determinacy-race detection (Feng, Leiserson, "Efficient detection of determinacy races in Cilk programs", 1997): for
  one input, one instrumented serial run proves that every schedule of a fork-join program gives the same result if no
  two logically parallel accesses conflict. A real per-input proof over all schedules. It needs instrumented memory
  accesses (no such tool for .NET), and it flags every lock and `Interlocked` as a race, which is exactly where the
  exact Boolean needs an argument anyway (option E).
- Deterministic multithreading runtimes (Kendo, Olszewski et al. 2009; CoreDet, Bergan et al. 2010): the runtime forces
  one schedule. Makes runs repeatable on one machine, does not make the result independent of the core count, and is not
  available to a library.
- Systematic schedule exploration (CHESS, Musuvathi et al., OSDI 2008): enumerates schedules up to a bound; sampling
  with a coverage argument, used for bug finding.
- Certifying algorithms (McConnell, Mehlhorn, Näher, Schweitzer 2011): a result with a certificate a simple checker
  verifies. One run cannot certify determinism, but it can certify a **canonical form**: if the result is defined as the
  canonical representative of a mathematically unique object, determinism follows from correctness. The basis of option G.

**Roslyn practice.** Analyzers that enforce determinism of a marked scope exist in production: the Durable Task
analyzer (rules `DURABLE0001` and following: no `DateTime.Now`, `Guid.NewGuid`, threads or I/O in orchestrator code).
They are ban lists, as `BannedSymbols.txt` is. No published closed-world (allow-list) determinism analyzer for .NET was
found (searched: "Roslyn determinism analyzer", "deterministic subset C#", "smart contract determinism validator C#").

## 4. What a proof can rest on

### 4.1 The trusted base, made explicit

No option removes these; a proof states them and guards them with probes that run on every platform the claim names:

| assumption | guard |
|---|---|
| T1. The JIT evaluates `double` `+ - * /`, `sqrt`, comparisons and conversions as IEEE 754 binary64, one rounding per operation, no excess precision, no contraction (ECMA-335 allows more precision; x64 SSE2 and arm64 code generation do not use it) | `probe.arith-chain` of the harness, extended to the operator patterns of the scope; the claim is per runtime version, a new runtime runs the probes before the claim extends to it |
| T2. Integer, shift and `BigInteger` arithmetic follow the language and are mathematically exact | the exact tests; `BigInteger` results are defined by their value |
| T3. The allow-listed BCL members compute their specified result | one line of justification per member (section 4.2, R0) and a probe where the specification changed between runtimes |
| T4. The process runs in the default floating-point environment (round to nearest, subnormals on); a host can change it (flush-to-zero in audio and game hosts, x87 precision control on 32-bit) | optional run-time guard: four operations at the start of an operation, a status code if they fail (fits "clear result codes") |
| T5. The analyzer and Roslyn | mutants of the rules (`ExactCodeAnalyzerTests` pattern) |

### 4.2 The four bases and what each covers

| basis | covers | does not cover | fit here |
|---|---|---|---|
| **Static check, closed world** (allow list of APIs and constructs in a scope; the present rules are ban lists) | L1 for sequential code completely, given T1 to T3; all "ambient" sources (clock, random, culture, hash of strings, core count); makes the list of escapes finite | whether an allowed escape is harmless; anything semantic (is this comparer a total order) | high: extends the existing analyzer project and `.editorconfig` scoping |
| **Dataflow argument per operation** (taint from environment sources to the observable result, with declassification points) | L0: which environment-dependent values can reach the result, and through which normalization | the correctness of each declassification (that the sort really normalizes); precision across calls is limited without annotations | medium: Roslyn has a dataflow framework; realistic as local rules plus attributes, not as whole-program analysis |
| **Type-level discipline** (`Unordered<T>`, abstract ids, diagnostic-only types) | the same as dataflow, but checked by the compiler everywhere, and with a free theorem for ids (renaming invariance) | cost: rewrites of hot exact code; performance to be re-measured; cannot express "total order" | selective: only where a dataflow obligation is too hard to argue |
| **Checked run time** (environment adversary; canonical-form checker; environment guard T4) | attacks every obligation and every assumption directly; canonical form proves order independence per run | adversary is sampling (of the environment, far denser than platforms); a canonical form covers order, not content | high as the evidence layer under the static part; not a proof alone |

No single basis suffices. The static check gives completeness of the list, the arguments give the reasons, the adversary
gives evidence for each reason. That combination is option A with B.

### 4.3 The five argument forms

Reading the exact Boolean's allow list (`core/ExactCodeAllowList.cs`) and its code comments shows that every existing
determinism argument is one of five forms. A proof method fixes these forms, so that an obligation is a choice of form
plus its premises, each premise checkable:

| form | statement | premises | checked by |
|---|---|---|---|
| **P1 disjoint writes** (Bernstein) | iteration `i` writes only slot `i` or objects it created, and reads nothing written in the loop | captured variables: read-only, or written only at index `i` | analyzer for the simple shapes (assignment to `a[i]`, locals); review for the rest |
| **P2 exact commutative fold** | the combination is associative and commutative on exact values: integer sum and count, min / max under a total order, or / and | operand type is integer or a total-order key; no floating-point `+` | analyzer (types) |
| **P3 collect, then canonicalize** | a bag filled in any order is sorted by a total order on content before any order-sensitive use | the comparer is total on distinct elements; the multiset does not depend on the environment | analyzer: the sort dominates every ordered use; property test: comparer returns 0 only for identical elements; adversary: shuffle before sort |
| **P4 unique answer** | the value is defined by the input mathematically (an exact predicate, a winding number, a canonical representative), so the way it was computed cannot change it | the function is exact and total (no failure or fallback path that depends on the environment); correctness tests against an oracle exist | attribute `[UniqueResult]`, the analyzer treats the return value as clean whatever happens inside (filters, caches, `ThreadStatic` workspaces, seeded random rays); correctness tests are the evidence |
| **P5 renaming invariance** | ids assigned in schedule order (`Interlocked.Increment`) reach the result only through uses that are invariant under a permutation of the ids | ids are used for equality and lookup only; any iteration in id order ends in P2 or P3 | review today; type-level (option D) or removed by construction (option C); adversary: permute the id assignment |

P4 is why the exact code is the first target. P5 is the hard one and the only form whose premise no cheap tool checks:
`ExactMeshArrangement` assigns point ids by `Interlocked.Increment` in a locked sharded index, and the allow list
argues in prose that segments and points are later sorted by `ComparePoints` and the canonical point of a cluster is
chosen by `PointKey`, not by id.

## 5. Sources of nondeterminism and the rule for each

"Exact" and "library" are line counts of a `grep` in the 23 files of the exact scope (21 785 lines) and in the
library (447 files, 169 406 lines). A rule marked *list* is a closed allow list; *ban* is expressible with the present
tooling; *flow* needs dataflow or an attribute.

| # | source | level | where it shows here (exact / library) | rule a tool can check | residue |
|---|---|---|---|---|---|
| R0 | any BCL member with a platform-dependent or unspecified result | L1 | unknown until listed | *list*: in a proof scope only allow-listed external members may be called; each entry has a one-line reason why its result is specified | the reasons are reviewed, not proven (T3) |
| R1 | transcendental functions (platform libm, 1 ulp) | L1 | 16 / 232 lines; in the exact scope all in `TerrainSlope*` (`Tan`, `Cos`, `Acos`, `Atan2`, `Pow`) | *ban* (G3E0004 exists; scope it to every proof scope); `CoreMath` or exact constants instead; `Math.Pow(2, -30)` becomes a literal | none once banned |
| R2 | `float`, x87 on 32-bit | L1 (x86) | 0 / 1347 | *ban* (G3E0001) | 32-bit stays outside the claim for float code (owner decision C4) |
| R3 | FMA, hardware intrinsics, `Vector<T>` (its width is the machine's) | L1, L2 | 0 uses of vector types in exact code (the 16 `System.Numerics` hits are `BigInteger`) | *ban* (G3E0003, RS0030; add `System.Numerics.Vector*`) | JIT contraction is T1 |
| R4 | `Math.Min / Max` of doubles: the result for `(+0, -0)` and NaN was changed in .NET Core 3.0 to follow IEEE 754-2008 (confirmed, 5.1) | L1 | 237 / 1200 lines | *list*: not on the allow list for `double`; a library `MinExact / MaxExact` with a stated result for zeros instead, or an argument that the sign of zero cannot reach the result | a `-0.0` in a result is a different bit pattern; DETERMINISM.md 6a calls Min / Max "IEEE-exact everywhere", which this narrows |
| R5 | `double -> int / long` casts out of range or of NaN: unspecified in C#; x64 .NET Framework gives `0x80000000`, arm64 and .NET 9 and later saturate (confirmed, 5.1) | L1, L2 | not counted (needs the semantic model) | *flow*: a cast needs a dominating range check, or a helper with a stated result | range facts beyond simple guards need review |
| R6 | conditional compilation: `#if NET` runs different code on net48 and net10 | L1 | 18 / 61 blocks (`BitLength`, `TrailingZeroCount`, `TryWriteBytes`, generated predicates) | *list*: every block in scope is registered with a differential test of the two branches; the code already compiles the portable branch everywhere "so that the tests on .NET check it"; the analyzer requires the registration | equivalence is tested (exhaustive only for small domains) |
| R7 | hash values: `string`, `ValueTuple`, `HashCode.Combine` are seeded per process on .NET; so is `BigInteger.GetHashCode` (used by `ExactPoint3.GetHashCode`) on .NET 10 (5.1) | L0-R, L1 | 15 / 110 lines; a hash value picks the shard in `ExactMeshArrangement` (`key.GetHashCode() % Shards`); `ValueTuple` keys in `SnapRounding2` | *flow*: a hash value may only go into a hash container or an index of a lookup-only structure; never into an order, a count that is observable, or the result | shard choice is P4 (lookup result unique) with P5 for the ids |
| R8 | enumeration order of `Dictionary` / `HashSet` (unspecified; in practice insertion order until an element is removed, and not promised equal across runtimes) | L0-R, L1 | about 157 / 542 containers created; in the Boolean, two enumerations of `.Values` feed the result: `fans.Values`, where the first fan keeps the vertex and the others get a copy (`ExactMeshBoolean.cs:4356`), and `groups.Values` (`ExactMeshArrangement.cs:1031`) | *flow*: `foreach` over a hash container (or `Keys`, `Values`, LINQ over it) is an error in scope unless the body is P2, the result goes through P3, or the site is on the allow list | the two sites above are obligations today: correct if the insertion order is fixed and the runtime keeps it, which no specification says |
| R9 | clock, `Stopwatch`, `DateTime`, `Guid`, `Environment.*` (core count, tick count), unseeded `Random` | L0 | 9 / 70 lines; `Environment.ProcessorCount` chooses outer or inner parallelism in `ExactMeshBoolean.each` | *flow*: values from these sources may only reach members marked diagnostic, or select between branches registered as equivalent | the equivalence of the branches (P1: independent bodies) |
| R10 | seeded `System.Random`: deterministic today, but the documentation reserves the right to change the sequence between versions | L1 | 4 uses in the Boolean (ray directions for winding numbers, a test permutation) | *list*: not allowed; the harness's SplitMix64 as a library generator; or P4 where the answer does not depend on the ray | none with an own generator |
| R11 | sort: order of equal keys (`Array.Sort`, `List.Sort` are unstable; the implementations differ between runtimes), `BinarySearch` among duplicates | L1 | 39 / 153 calls; the code handles several by hand (`fix_ties`, "the order among equal values does not matter", comparers ending in `a.CompareTo(b)`); open by reading: `Array.Sort(sortLo, order)` in `merge_points` (ties of `lo`), `Array.Sort(sizes, order)` (only the scheduling order) | *flow* plus *list*: a sort in scope uses a comparer registered as total (property test: `Compare(a, b) == 0` only for identical elements), or a keys-and-items sort is followed by a tie repair, or the site is on the allow list as P2 / P4 | totality of a comparer is a tested property |
| R12 | parallel loops: floating-point reduction, shared writes | L0-S | about 30 / 160 call lines through `gParallel`; 17 allow-list entries with prose reviews | G3E0002 stays; an entry names its form (P1, P2, P3, P5) per loop instead of prose; the analyzer checks P1 and P2 shapes itself and leaves only P3 / P5 for review | P5 |
| R13 | schedule-assigned ids and locked shared lists | L0-S | 34 `Interlocked` lines, 14 `lock` in the exact scope | *flow*: the result of `Interlocked.Increment / Add` and the order of a list appended under a lock are environment values; allowed sinks as for R8 | P5; the largest open obligation |
| R14 | state that survives a call: `static` and `[ThreadStatic]` fields, reused workspaces | L0-R | 5 `ThreadStatic` in exact code (`AdaptivePredicates.Workspace`, stacks, test flags) | *flow*: a static in scope is read-only after initialization, or a workspace whose content is overwritten before it is read, or diagnostic; the reuse test of CLAUDE.md is the evidence | "overwritten before read" is review |
| R15 | uninitialized memory: `[SkipLocalsInit]` with `stackalloc`, `ArrayPool`, `GC.AllocateUninitializedArray` | L0-R | 9 `SkipLocalsInit` (`FixedPoint3`, generated predicates) | *list*: each such method is registered with the argument that every limb is written before it is read; adversary: fill the stack buffer with a pattern first in a test build | the argument is review; a definite-assignment check for buffers is possible in the generator, which emits these methods |
| R16 | addresses and object identity (`RuntimeHelpers.GetHashCode`, default `Object.GetHashCode`, reference comparison as order) | L0-R | 0 direct uses | *ban*, and R0 keeps a class without `GetHashCode` out of hash keys | none |
| R17 | text: `double.Parse`, `ToString`, culture | L1, L2 | 7 / 260 lines | *ban* in scope; I/O is its own scope with C2 / C3 of DETERMINISM.md | readers and writers are not part of the first proofs |
| R18 | NaN payload and sign, signed zero from `0 * -x` | L2 | not countable | input gate (finite), and `-0.0` normalized where a coordinate enters the result, or stated as it is | a full proof needs range reasoning; stated as an assumption |
| R19 | environment-dependent failure: exceptions in a parallel loop (which one surfaces), cancellation, out of memory | L0 | the Boolean stores per-item exceptions in slots | *flow*: the exception rethrown is chosen by index, not by time; cancellation and OOM outside the claim | by definition (section 2) |

Two readings of the table:

- Half of the rules (R0 to R6, R10, R16, R17) are sequential and closed-world. With them, **L1 for sequential code needs
  no argument per operation at all**: a scope that passes the allow list computes the same function on every runtime
  that satisfies T1 to T3. This is the cheapest part and the one the measurement is weakest on (six configurations).
- The others (R7 to R9, R11 to R15, R19) are flows from an environment value to the result. The tool does not prove
  them harmless; it proves that the sites are **all** on the list, each with a form of section 4.3.

### 5.1 Results of the probes and the schedule adversary (#786, 2026-11-13)

Option B, first piece, is merged: the loop-order hook of `gParallel`, the harness command `adversary`, the probes and
`OrderAdversaryTests`. Measured in determinism run 43379590599 (linux x64, linux arm64, windows net10, .NET Framework 4.8
x64 and as a 32-bit process; .NET 10.0.15), 61 cases.

| rule | question | result | follow-up |
|---|---|---|---|
| R12, R13 | does a result depend on the order in which a parallel loop runs its items | no: 61 of 61 cases the same backwards and under seeded permutations of every `gParallel` loop, on linux net10 and windows net48; inside every runtime all cases are identical between parallel, serial, two runs and 1 to 8 processors | hash order, sort ties and libm are not perturbed yet (they need choke points at the call sites: with option A) |
| R1 | platform libm in `TerrainSlope*` | **real**: `TerrainSlopeDesign` with a Ratio slope and a libm-free input differs in 20 of 15302 result values between windows net10 and windows net48, and between linux net10 and windows net10 (linux net10 and windows net48 agree). Cause, by moving each of the 15 reached call sites by one ulp: `Cos / Sin` of the fan directions (`TerrainSlopeDesign.cs:1716`), which define the planes given to the exact kernel; the other sites feed decisions and bounds and did not flip | #802 (owner decision: libm rule for `TerrainSlope*`, the one line only, or outside L1) |
| R4 | `Math.Min / Max` on zeros | confirmed: `Min(-0, +0)` and `Max(+0, -0)` differ (.NET Framework returns the second operand, .NET the IEEE 754-2019 result); with a NaN operand both agree. Six sites of the exact scope can pass the zero to a result value | #803 |
| R5 | `double -> integer` casts | confirmed: 63 of 150 probed casts of NaN and out-of-range values differ (.NET saturates, NaN gives 0; .NET Framework gives the minimum value). The audit found one site that a valid input drives out of range (`TerrainSlopeDesign.cs:1090`) | #804, #808; the audit of all remaining sites: [CAST_AUDIT_804.md](CAST_AUDIT_804.md) |
| R7 | hash values | confirmed: `BigInteger` (so `ExactPoint3`), `ValueTuple` and `string` hashes differ between any two runs on .NET 10 and are fixed on .NET Framework; `double.GetHashCode` differs between the runtimes for 1 of 8 probed values. No Boolean result depends on them on any configuration | #789: `ExactBooleanDeterminismTests.PointHashes_DoNotAffectTheResult` perturbs the hashes of the arrangement's point indexes and of its merge of equal points (two salts, and every point in one shard; serial and parallel; a choice of the representative of equal points that mixes the hash value with the id fails it). Enumeration order is no channel: `Dictionary` / `HashSet` enumerate their entries in insertion order (with the free list of removals) on both runtimes, whatever the hashes (an implementation fact, R8); the channel is a use of the hash value itself, and nothing iterates the shards. This is the stage 2 ledger entry of R7 for `ExactMeshArrangement`. Not perturbed, lookup-only by inspection (ids handed out by `Count` in insertion order, never enumerated): the `BigInteger` tuple keys of `ExactPlanarSimplify` (`groups`, `planes`), the `ValueTuple` plane buckets and the `Vector3d` index of `ExactMeshArrangement` |
| R8 | enumeration order of `Dictionary` / `HashSet` | the same on both runtimes after inserts, after removals, after refills, after set operations and `TrimExcess`, also for keys with seeded hashes; different only for a `HashSet` emptied by `Remove` and refilled | none; the rule stays (the order is unspecified), the two `.Values` sites of the Boolean rest on behaviour that held here |
| R10 | seeded `System.Random` | the same sequence on both runtimes | none; the rule stays (not promised) |
| R11 | order of equal keys after the unstable sorts, `BinarySearch` among duplicates | the same on both runtimes for 16 to 20000 elements and six overloads | none; the rule stays |
| L2 | linux arm64 against linux x64 | identical on all cases | none |

What the adversary is worth, by mutation: of six normalizing sorts removed from the exact Boolean one at a time, the
order adversary detects two, the same two that the existing repeated-run tests detect; it does so in one serial process
instead of by parallel repeats, and not more often. The four others (`ExactMeshArrangement.cs` 987, 2509, 2516,
`ExactMeshBoolean.cs` 4363) survive every determinism test and the harness: redundant on the inputs, or not reached by
them. They belong to the stage 2 ledger.

### 5.2 Stage 0: the exact kernel as a closed world (#859)

Option A, stage 0, is built: the analyzers G3D0001-9 (`tools/analyzers/G3ExactCodeAnalyzers/DeterminismAnalyzers.cs`)
are errors in the fourteen kernel files of section 6.1 (with `math/ExactLimbSigns.cs`, the stack-limb signs that `ExactMeshBoolean` calls, added after #871, and `math/ExactLimbPrepared.cs`, their prepared operands, added after #1131; like `ExactLimbSigns` it is checked by unit tests and the dirty-stack test, with no `DeterminismHarness` case) (`.editorconfig`; generated code is analyzed too, so a kernel file
marked as generated stays in), and the allow list with its registrations is
`DeterministicApis.txt` at the repository root.

| rule | checks | registration |
|---|---|---|
| G3D0001 (R0) | every member that the kernel calls, reads, constructs or applies and that is declared outside the fourteen files (the BCL, and geometry3Sharp's own `Vector3d`, `MathUtil`, ... in other files): methods, properties, fields, events, constructors, user-defined operators and conversions (also implicit ones and those of `foreach` elements), attributes, `lock`, `foreach`, `using`, patterns; constructs the compiler lowers to hidden framework calls (string concatenation, equality and formatting, `decimal`, tuples, `await`, `dynamic`, ranges, delegate combination, `new T()`), calls whose target is chosen at run time (delegate and function-pointer invocations) and operations the public operation tree does not model (kind None, except `stackalloc`, pointer and fixed-buffer access and `fixed` over arrays) under pseudo ids that no entry can allow | one line per member with the reason why its result is specified on every runtime |
| G3D0002 (R4, R7, R13, R14) | a restricted entry is used only where allowed: `[sites]` entries (`Math.Min / Max` of doubles, `(double)BigInteger`, `Vector3d.ToString`, `[ThreadStatic]`, `Interlocked`, `Volatile`, `Monitor`) only in registered members; `[hash]` entries, and every `GetHashCode` call (also of the kernel's own types, whose dispatch the analyzer does not follow), only inside a `GetHashCode` override or an `IEqualityComparer<T>.GetHashCode` implementation | a `site` line per member with its justification |
| G3D0005 (R6) | every `#if` / `#elif` block, counted per member | an `if` line with the count and the test that checks both branches |
| G3D0006 (R5) | every conversion of a `double` to an integer type or enum (also nullable, and `foreach` element conversions), counted per member | a `cast` line with the count and the guard |
| G3D0007 | no `#pragma warning disable` of these rules (or of all), of RS0030 or G3E, no `#define`, `#undef`, `#line` in the kernel; reported again at the start of the scope's first file, where no pragma reaches | none |
| G3D0008 (R15) | every `[SkipLocalsInit]` that reaches a kernel member (on the method, its property, a containing type, the module, a local function or lambda) | a `skiplocalsinit` line, on the symbol that carries the attribute, with the argument that every element is written before it is read |
| G3D0009 | the list is exact: an entry or registration that the kernel does not use, a malformed line, or a geometry3Sharp member whose declaration no longer has the pinned hash, fails the build (`[NET]` entries, their sites and the `[SkipLocalsInit]` registrations only where `NET` is defined) | |

**Ledger** (stages 0 and 1; it is generated from `DeterministicApis.txt` and checked by `DeterminismScopeAnalyzerTests`, so it is stated once, in section 5.4). Stage 0 as merged (#871): 116 external members (37 of them geometry3Sharp members declared outside the kernel, 11 only on the `#if NET` side), 9 restricted to registered sites (21 sites), 1 restricted to `GetHashCode` implementations; 36 conditional-compilation blocks in 30 members; 2 `double` to integer conversions in 1 member(s); 18 `[SkipLocalsInit]` members.

**Claim.** On every runtime that satisfies T1 to T5 (section 4.1), every operation of the fourteen kernel files computes a
function of its arguments alone: no value it returns depends on the schedule, the process, the platform's libm, the
clock, the culture, a hash seed or what an earlier call left in memory, given the reasons of the listed entries and the
obligations below. With P4 (the kernel's predicates and exact constructions are defined by their inputs mathematically,
and their correctness tests compare with oracles), every predicate returns the sign its specification defines. The
reasons of the 37 geometry3Sharp members declared outside the kernel (`Vector2d`, `Vector3d`, `Triangle3d`, `Index3i`,
`MathUtil.IsFinite`, `JitHints`, `PredicateStats`, `ExactMeshArrangement.Det4`) are a review of their declarations; the rules do not analyze those
files (nor does G3E0001-4), but each entry pins the text of the declaration it reviewed (`[body:h]`, an FNV-1a hash of
its tokens, comments and line breaks, the indentation between tokens ignored, a disabled `#if` branch tokenized as if
it were active; the same on every runtime, target and line-ending convention): a changed declaration fails the build (G3D0009) until it is reviewed again and the tag updated, and a
pinned declaration may use geometry3Sharp members outside the kernel only if they are listed and pinned too (closure;
it brought in `Index3i.a / b / c` and `PredicateStats.counters / Kind.Count`). Not covered by the pin: the containing
type (struct or class, layout attributes), what the names bind to (usings, overload resolution), implicit enum values,
and build switches (`G3_PREDICATE_STATS`). What carries the claim:

- T1 (IEEE 754 binary64, no contraction): `probe.arith-chain`; the kernel's own code uses `+ - * /`, `Math.Sqrt`,
  `Math.Abs`, comparisons and bit reinterpretation (G3D0001 lists no libm member, no FMA, no `float`, which G3E0001-4
  also ban in these files).
- T2 (integer and `BigInteger` arithmetic exact): the exact tests. `(double)BigInteger` is *not* correctly rounded: it
  truncates toward zero to 53 bits, on .NET 10 and .NET Framework 4.8 (x64 and 32-bit) alike, measured on 1238 values
  with ties, sticky bits beyond the top 64 bits and the overflow boundary (`probe.bigint-double`, determinism run
  43505530553, the same trace hash on all five configurations); `new BigInteger(double)` truncates exactly on both and
  throws `OverflowException` for NaN and infinities (`probe.double-bigint`). Its values are therefore the same bits on
  both runtimes. Its four sites are registered: none assumes round-to-nearest or a half-ulp bound (`ExactDyadic.Quotient`
  rounds the exact value itself and converts at most 54 bits; the documented bounds of `ApproxVector3d`, 4 * 2^-52, and of
  `ExactPoint3.ToDouble`, below 1 ulp, hold under truncation; `FixedPoint3.DoubleOf` checks the conversion by converting back).
- T3 (the allow-listed members compute their specified result): the reason on each line of the list; a probe where the
  specification changed between runtimes (`Math.Min / Max`: `probe.minmax-zero`; the two conversions above).
- T4 (default floating-point environment): as in section 4.1.
- T5 (the analyzer and Roslyn): the rule tests of `DeterminismScopeAnalyzerTests`; mutants checked by hand, not by a
  mutation shard: in a kernel file an unlisted call, an unregistered `#if`, an unguarded cast and an unregistered
  `[SkipLocalsInit]` each fail the build, and a kernel file dropped from the `.editorconfig` section fails the build (its
  entries become unused) and the scope test; in the analyzer, dropping user-defined conversions, the hash rule, the
  `long` target of the cast rule or the `#if` count is caught by the library build or the rule tests.

Obligations the claim names (review, not a tool check): the per-thread workspace of `AdaptivePredicates` is written
before it is read (R14); `FixedPointStore.Add` hands out slots by `Interlocked.Increment`, a schedule-assigned id when
`ExactMeshArrangement` adds concurrently (R13, P5): no kernel predicate reads it, its consumers are stage 2; its `lock`
guards the growth of the chunk table only, and a slot is written by the one thread that got its index; the hash of
`ExactPoint3` is seeded per process on .NET (R7) and reaches hash containers only, whose consumers are stages 1 and 2.
The two branches of the generated `__dec` / `__bits` are checked against one oracle on each runtime (the net48 job), not
against each other (the generator inlines the portable loop); the other seven `#if NET` members are compared with their
portable twins value by value on .NET (`KernelPortableBranchTests`), the eighteen `[SkipLocalsInit]` members run on a stack
filled with patterns against a zeroed one (`SkipLocalsInitKernelTests`). Found while building it: the hand-made list of 72
members missed the two implicit `int` / `long` to `BigInteger` conversions and the `lock` of `FixedPointStore.Add`, and
did not count the 31 members of geometry3Sharp outside the kernel; no `Math.Min / Max` site of the kernel lets the sign
of a zero reach a value (interval bounds go through `make`, which replaces a zero bound by -2^-1074 / +2^-1074; the other
sites compare, or take `Math.Abs` values). The only kernel change: `trailing_zeros` and `leading_zeros` became internal,
so that the branch test can call them.

### 5.3 Measurement: the exact kernel and its allow list on net48 and net10.0 (2026-11-15)

**Cases.** The harness cases `kernel.*`, `ledger.*` and `boolean.rounding.*` (`tools/DeterminismHarness/Kernel.cs`, README "Kernel
and allow list") run:
- the kernel's operations: the predicates and their exact stages, `ExactPoint3`, `FixedPoint3`, `ExactLimbSigns`, the triangle, ray
  and slope predicates, the intervals;
- the members of its allow list (`DeterministicApis.txt`) on edge inputs;
- Booleans whose rounding meets coplanar and near contacts.

**Inputs.** All inputs are procedural, from a splitmix64 stream. The point sets of the predicates come in exponent classes from subnormal
(2^-1060) to 2^900, as random, nearly degenerate (a point moved by a few ulps off a line or plane), small-integer (exact degeneracies, no
exponent) and repeated sets. The other cases have their own classes:
- bit lengths for `ExactPoint3`, `FixedPoint3` and `ExactLimbSigns`, with exact zeros and steps of one;
- exponents -100..100 plus `double.Epsilon` and -0 for the intervals.

**Runs.** The `compare` job of `determinism.yml` prints the hash and value count per case and configuration (`measurement_rows.py`). The
table below summarizes them with the per-section counts of `compare --brief` and the analysis.
- Round 1: run 43719418661 (head 41b734a).
- Round 2 (head 21b83f6): new input classes (every kind in every exponent class), the inputs in the trace, and `kernel.limbsigns.path`
  split off (compare job 132841141622).
- Rounds 3 and 4: runs 43726025267 and 43732107965. Only the trace sections changed, so the hashes are those of round 2.
- Round 5 (run 43734524750) and round 6 (run 43736653039, head 76925c4: only the filter flag changed): after the review, `kernel.limbsigns` with true zero sides and degenerate sets,
  `boolean.rounding.*` and `kernel.slope` with their inputs in the trace, and the filter's agreement with the exact test
  (`/filter-wrong`).

**Configurations.** The columns are Linux and Windows net10.0, Windows net48 64-bit and 32-bit (x87, the legacy JIT, run serially only),
and Linux arm64 net10.0. Each comparison is between serial runs. In addition:
- serial equals parallel on Linux and Windows net10.0 and on net48 64-bit;
- the processor counts 1, 2, 3 and 8 (Linux x64) and 1 and 3 (arm64) give the same results;
- the one exception is `probe.hash-seeded`, by design.

| case | net10 linux / windows | net48 x64 | net48 x86 | arm64 | explanation |
|---|---|---|---|---|---|
| `kernel.predicates.double`, `.adaptive`, `kernel.exactpoint`, `kernel.fixedpoint`, `kernel.limbsigns`, `kernel.slope`, `kernel.intervals`, `ledger.biginteger`, `probe.bigint-double`, `probe.double-bigint` | = | = | = | = | the same bits in every class of each case (the classes above) |
| `boolean.*.exact`, `.saferounding`, `boolean.box-sphere`, `boolean.rounding.*` | = | = | = | = | the inputs and the Boolean results agree, including the rounding and the status. The rounding cases are coplanar contacts at 2^-30, 1 and 2^30, with and without `SafeRounding`, and a near contact |
| `kernel.limbsigns.path` | = | differs | differs | = | which branch ran: the stack limbs are `#if NET` only, and .NET Framework takes the BigInteger expression. The signs (`kernel.limbsigns`) agree, as registered (G3D0005) |
| `kernel.triangles` | = | = | differs (34 values) | = | only `TriangleTriangleFilter.BeyondShared` at 2^900 (section `/filter`): *Undecided* on one runtime, a certain answer on the other. The filter projects along the dominant axis of A's floating-point normal (`by_projection`). At 2^900 the cross product overflows to infinity or NaN with SSE2; in an x87 register it may stay finite (inferred, not isolated by a probe), so another axis is chosen. Every later decision is an exact `Orient2d`, and a projection that separates along any axis proves the 3D separation, so both answers are valid. `/filter-wrong` (1 where a certain answer of the filter is not the exact test's) is 0 on every configuration, net48 x86 included (round 6), and all the exact tests agree |
| `kernel.ray` | = | = | differs (37 values) | = | the second point of the ray (`ExactRayTriangle.SecondPoint`, a double computation, `/secondpoint`), for subnormal inputs only: 22 values in 12 sets. With SSE2 only `direction * 2^m` rounds there. The difference is consistent with x87 carrying the unrounded product into the sum and rounding twice when it stores the result (not isolated by a probe). A different second point is a different ray, so its exact signs may differ, and they do only there: the 15 differing signs are those of 3 of these 12 sets (`repeat` sets #407, #1295 and #1391 of run 43732107965, 5 signs each: a ray through a vertex, where any change of direction changes the answer). No sign differs in a set whose second point agrees, and the exact predicates agree on x86 for every subnormal input (`kernel.predicates.*`) |
| `ledger.math` | = | 2 values | 2 values | 979 values | net48: `Math.Min / Max` of opposite zeros (`min-max-zeros`, R4, #803; the kernel's result values use `MathUtil.MinExact / MaxExact`). arm64: only the bits of NaN results (`sqrt-nan` 977, `arith-nan` 2). ARM's default NaN has no sign bit and x64's has one, and IEEE 754 leaves the payload and sign open. NaN results are outside the claim's input domain (section 2, R18) |
| `probe.cast-range`, `probe.minmax-zero` | = | differ | differ | = | the measured runtime facts of R5 and R4 (#804, #803) |
| `boolean.union.libm-input` | windows differs from linux | = linux | differs | = linux | the input is made with the platform libm (`Math.Sin`), so the input differs; libm is R1 |

Conclusions:
1. **For the same double inputs, the exact kernel gives the same results on every runtime and processor measured.** That covers every
   sign, every exact value and every Boolean result, in the input classes above. The differences are:
   - a branch flag, by design;
   - a filter's *Undecided*;
   - a double approximation that defines a ray;
   - NaN bits;
   - the known `Min` / `Max` and cast facts of R4 and R5, which the allow list already restricts.
2. **32-bit .NET Framework (x87, R2, T4) is a different floating-point machine at the edges of the range.** Its precision control is 53
   bits there, so in the normal range it rounds as SSE2 does. Intermediate overflow and gradual underflow behave differently.
   - Inside the kernel this reaches only values whose role is approximate: a filter's decision to defer, and the ray's second point.
   - Results that take such a value as their *input* can differ for subnormal or near-overflow coordinates on net48 x86. These are the
     winding ray of the Boolean, and the public ray queries that define their ray by `SecondPoint` (`DMeshAABBTree3`, `IntrRay3Triangle3`).
   - The stage 1 and 2 obligations of these consumers have to show that the result does not depend on the ray chosen, or state the input
     range. Owner decision C4 keeps 32-bit outside the claim for float code; whether it also stays outside for subnormal and
     near-overflow coordinates is an owner decision.
3. **No timing**: speed on net48 is the owner's local comparison (#929).

### 5.4 Stage 1: the exact building blocks (ExactTriangulation2, ExactPlanarSimplify, SnapRounding2, TriangleBoxTree)

Option A, stage 1, is built: the same analyzers G3D0001-9 and the same list `DeterministicApis.txt` now cover four more
pieces of the exact pipeline (2 450 lines, with sorts, hash containers, delegates and one parallel build): `mesh_ops/ExactTriangulation2.cs`,
`mesh_ops/ExactPlanarSimplify.cs`, `comp_geom/SnapRounding2.cs` (with `SnapGrid`) and the class `TriangleBoxTree` of
`mesh_ops/ExactMeshArrangement.cs`.

**Scope of a class.** A scope is a file list in `.editorconfig`, and `TriangleBoxTree` shares its file with the rest of
`ExactMeshArrangement` (stage 2, held by the Exact Boolean 3.0 work). The file is in the `.editorconfig` section, and the line
`type ExactMeshArrangement.cs ; TriangleBoxTree` of the list restricts the rules there to the positions inside that type declaration
(nested types included). Three consequences, each with a rule test: a member of the file outside the type that the class
calls is external like any other (listed, pinned or assumed); the forbidden directives of G3D0007 (a `#pragma warning disable`,
`#define`, `#line` before the type reaches into it) are checked in the whole file; a `type` line whose type is not in the file is an
unused line (G3D0009). No line of `ExactMeshArrangement.cs` changed.

**Analyzer changes** (`DeterminismAnalyzers.cs`; the rule tests are in `DeterminismScopeAnalyzerTests`):

| change | why stage 1 needs it |
|---|---|
| `type` lines (above) | the class inside a stage 2 file |
| tuple literals of up to 7 elements are the constructor of their `ValueTuple` type, and the elements (also named ones) its `ItemN` fields, which are listed like other members; longer tuples stay a pseudo id | `SnapRounding2` is written with value tuples (about 70 literals); before, any tuple was an error that no entry could allow |
| `construct <member> ; <delegate-invocation or string-concatenation> ; n ; why` | a delegate call has a callee that the caller chooses (`ExactTriangulation2`'s predicates, the visitors of the box tree), a concatenation formats text; both were errors without an entry. They are registered by count per member, like the casts |
| tag `[form]` on an entry: every site line (and every delegate-invocation line) must start with `P0` to `P5`, the tests in brackets, then the reason; `DeterminismScopeAnalyzerTests` checks that the tests exist | an entry names its argument form (section 4.3) and the tests that attack it. `P0` is not a form of 4.3: the source is a list or an array in index order, and the argument is its type |
| tag `[assumed]` on an entry for a member of this assembly outside the scope: the signature is listed, the body is neither pinned nor analyzed | the calls of `ExactPlanarSimplify` and `ExactTriangulation2` into the rest of `ExactMeshArrangement` (`ComparePoints`, `Orient`, `InCircle`, the input triangles): pinning those bodies would fail the build at every change of the 3.0 work, and they are stage 2 obligations anyway (S2-1 to S2-5 below) |

**How the rules R7 to R15 stand in these four pieces** (the 153 external members that the first build reported are on the list; the
rest of this table is what the registrations argue):

| rule | finding | discharge |
|---|---|---|
| R7 hash values | the containers of `SnapRounding2` are keyed by value tuples, whose hash is seeded per process on .NET; `ExactTriangulation2` and `ExactPlanarSimplify` use `int` and `long` keys (unseeded, `EdgeKeyComparer`), except the `BigInteger`-tuple dictionaries `groups` and `planes` of `ExactPlanarSimplify`, which are seeded but only looked up (ids are handed out by `Count`). No `GetHashCode` call is in the scope (G3D0002) | a hash value only places an entry; every enumeration is registered (R8). Cross-process evidence: the harness cases `stage1.snaprounding.*` |
| R8 enumeration | five members enumerate a hash container: `AddCrossings`, `IndexHot` and `Build` (`OrderBy` / `ThenBy` over unique tuple keys, a total order, **P3**), `Collapsed` and `ExactPlanarSimplify.fan` (an and-fold over the elements, **P2**) | `DeterminismStage1Tests.SnapRounding2_InsertionOrder_DoesNotChangeTheHotPixels` renumbers the points and reorders the segments (another insertion order into every container) and requires the same extra hot pixels, in the same order, and the same edges; the analyzer rejects a bare `source.Keys` (hand mutant: it fails the build with the unlisted `KeyCollection.GetEnumerator`), and the test fails on an `OrderBy` without its second key |
| R9 clock, environment, `Random` | none in the four pieces, but one test hook: `TriangleBoxTree.permute_for_tests` uses `Random(seed)` when `PermutationSeedForTests` is not 0 (a static that only tests write) | registered as a test hook; `BoxTreeOrderTests` show that no result depends on the permutation it makes |
| R11 sorts | `SnapRounding2.RouteAll` (`CompareMet`: entry, exit, then the pixel, a total order), `ExactPlanarSimplify.Run` (`ComparePoints`), `TriangleBoxTree` (`sort_codes`, `fix_ties`, `sort_codes_parallel`, `ranks`) | `CompareMet` was a lambda; it is now an `internal static` method with the same text (the one code change of the stage, output unchanged) so that `SnapRounding2_RouteOrder_IsATotalOrder` can test totality and sort-order invariance; the box tree sorts end in the order (code, box index) by `fix_ties`, `ranks` gives equal centres one rank (`BoxTreeParallelBuildTests`); `ComparePoints` is S2-1 |
| R12 parallel loops | four members of `TriangleBoxTree` (`#ctor`, `morton_order`, `sort_codes_parallel`, `build_parallel`), each in `core/ExactCodeAllowList.cs` with prose | restated as forms on the `site gParallel.ForRange` lines: **P1** (each iteration writes its own block or its own `Nodes`), **P3** (`sort_codes_parallel`: runs sorted apart, merged into disjoint ranges by a total order); the test is `BoxTreeParallelBuildTests.ParallelTree_SameAsSerialTree_NodeForNode` (serial = parallel, node for node, six input kinds with ties, far boxes, NaN) |
| R13 ids | `TriangleBoxTree` uses box indices; `ExactTriangulation2` and `ExactPlanarSimplify` receive point ids, which the arrangement assigns in schedule order | no ordered use of an id in the three pieces (read: no `<`, `Min`, `Max` or sort on an id; the removal order is `ComparePoints`; `EdgeKey` puts the smaller id first but is a set key only; the flip queue keeps the direction of the triangle, not of the key). **P5** holds there by reading; attacked by `OrderAdversaryTests.ExactBoolean_*` and `ExactBooleanDeterminismTests`; the adversary of the ids themselves is stage 2 |
| R14 state | 5 `[ThreadStatic]` fields of the box tree (a test counter, the traversal stacks and the pool of result lists) | registered with the argument that each is cleared or bounded before it is read (R14) |
| R4 `Math.Min` / `Max` | 7 site lines | each registered: compared only, or operands that are never `-0` (`MaxDisplacement`, `segment_hits`) |
| R5, R6 | 6 `double` to integer conversions in 5 members (the ledger's 8 in 6 include 2 of stage 0); one `#if NETSTANDARD2_0` block (a capacity hint) | `cast` and `if` lines with the guard; `ExactTriangulation2_CapacityHint_ChangesNothing` |
| P4 delegates and interface calls | `ExactTriangulation2` calls the `orient` and `incircle` callbacks of its caller (16 calls); the box tree calls the visitors of its callers, as delegates (`ActionVisitor`, `FuncVisitor`, `Query`) and through the struct visitors of its generic methods (`IIndexVisitor`, `IPairVisitor`: a call of a member of an interface declared in this assembly is a counted `construct:interface-invocation`, since its implementations, eg `ExactMeshBoolean.CrossVisit`, lie outside the scope) | `P4` lines: the triangulation is a function of the signs; `ExactTriangulation2_EquivalentCallbacks_GiveTheSameTriangulation` gives other formulas, delayed callbacks and a second instance. What a caller's callback computes is that caller's obligation (S2-2, S2-3, S2-6) |


**Ledger** (generated from `DeterministicApis.txt`, checked by `DeterminismScopeAnalyzerTests`; stages 0 and 1): 439 external members (194 of them geometry3Sharp members declared outside the scope: 95 pinned, 96 assumed as obligations of stage 2; 12 only on the `#if NET` side), 69 restricted to registered sites (254 sites, 149 of them with an argument form and its tests), 1 restricted to `GetHashCode` implementations; 41 conditional-compilation blocks in 33 members; 15 `double` to integer conversions in 13 member(s); 19 `[SkipLocalsInit]` members; 82 delegate calls in 24 members and 66 string concatenations in 10 members, registered; 0 file restricted to a named type.

**Claim.** On every runtime that satisfies T1 to T5, `ExactTriangulation2`, `ExactPlanarSimplify`, `SnapRounding2` and `TriangleBoxTree`
compute functions of their arguments (and, for the first two, of the values of the callbacks and of the stage 2 members listed
below) alone: no result depends on the schedule, the process, a hash seed, the order of a hash container, the order of equal
keys in a sort, the platform's libm or the clock, and the parallel build of the box tree equals the serial one node for node.
Checked: the allow list, the registered sites, constructs, casts and `#if` block, and the test named in each site line.
Assumed: T1 to T5; the reasons of the geometry3Sharp members outside the scope (82 pinned declarations, and 3 implicit constructors that have no declaration to pin); the six assumed stage 2
members (S2-1 `ComparePoints` is a total order on the distinct points of an arrangement, S2-2 `Orient` and S2-3 `InCircle`
return the exact signs (until #1464 the flips of `ExactPlanarSimplify` called `InCircleFiltered`, which returns 0 where its filter
does not decide and so is not defined by the points alone: its verdict follows the stored approximation of a point), S2-4 `InputTriangle.P` is the exact plane (lazily cached, the same value on every thread), S2-5 the input
triangle lookups, S2-6 the callbacks that callers pass to the box tree queries compute what the caller's own argument says).

**Open items.**
- *O1, the text of `SnapRounding2.Diagnostics`: done (owner 2026-11-17).* The messages concatenated a long or an int (the pixel in the `Restore` message, a label, a ring index, a count) with the current culture, so a minus sign followed the process's culture (`sv-SE` writes U+2212); a `Vector2d` already prints invariantly (`ToString` uses `CultureInfo.InvariantCulture`), and no double is concatenated. Every number now goes through `SnapRounding2.I`, which uses `CultureInfo.InvariantCulture`, so the text is the same under every culture; only the diagnostic string changed, under non-English cultures only. `SnapRounding2Tests.Diagnostics_AreCultureInvariant` compares the text under the invariant, `de-DE`, `fr-FR` and `sv-SE` cultures. The one-line diagnostic of `ConstrainedDelaunayMesh2` (not `SnapRounding2`) does not vary with the culture (its doubles use `R` with `InvariantCulture`, its int counts are never negative). The `ExactTriangulation2` exception messages keep the current culture (outside this item).
- *O2, the test knobs of `TriangleBoxTree`.* `ParallelBuildFrom`, `PermutationSeedForTests` and `PermutedTrees` are `internal static` and mutable. The
  analyzer has no rule for mutable statics yet (G3D0007 of section 6.2 is not built: the number there is another rule); the
  results are the same for every value of the two (the tests above), so a stray write would change speed and the order of
  the leaves only.
- *O3, `SnapRounding2.Origin`* is a public dictionary keyed by value tuples: a consumer outside the scope that enumerates it is not covered
  (the library's own consumer, `ConstrainedDelaunayMesh2`, only looks entries up).
- *O4, an equivalent mutant.* In `merge_runs` the index tie-break `o[j] < o[i]` can be removed without a test failing: the runs are
  ranges of box indices, so the left run holds the smaller ones. It stays as a guard; the order of the whole sort is attacked at
  `fix_ties` instead (removing it fails 8 of the 12 cases of `ParallelTree_SameAsSerialTree_NodeForNode`).
- *O6, element equality inside generic containers.* `Dictionary`, `HashSet` and `List.Contains` call `Equals` and `GetHashCode` of the key or element
  type, which the analyzer does not see (eg `List<(Vector2d, Vector2d)>.Contains` in `SnapRounding2.Build` runs `Vector2d.Equals`; the `Origin`
  keys are `Vector2d` pairs, and on .NET Framework `double.GetHashCode` differs for -0 and +0 although they are equal; the keys are `long * w`,
  never -0). Hashes only place entries; `Equals` of the used types is a field comparison. Also: `TypeScopes` is keyed by the file name (a second
  scope file with the same name would be restricted too), and `[assumed]` is not tied to a stage 2 file; both are reviewed by the list diff.
- *O5, runs on the other platforms.* The 12 harness cases `stage1.*` (`tools/DeterminismHarness/Stage1.cs`) are in the
  workflow `determinism.yml`, which runs every case; its first result for these cases (Windows, net48, arm64) is not part of this
  note. On linux x64, net10.0 locally: the parallel run equals the serial run and a second process (other hash seeds) on all 12, and
  the order adversary with three seeds changes none.

**Hand mutants** (not a mutation shard; the incremental mutation run of the CI covers the touched exact files): `source.Keys` without
`OrderBy` in `SnapRounding2.Build`: the library build fails (G3D0001); `OrderBy` without `ThenBy`: `SnapRounding2_InsertionOrder_*` fails on 6 of 6
inputs; `CompareMet` without its last key: `SnapRounding2_RouteOrder_IsATotalOrder` fails; `fix_ties` removed from `sort_codes`:
`ParallelTree_SameAsSerialTree_NodeForNode` fails; the type scope dropped in the analyzer: the library build fails (the whole of
`ExactMeshArrangement.cs` is then in the scope).

### 5.5 Stage 2: the arrangement and the Boolean as a whole (ExactMeshArrangement, ExactMeshBoolean)

The scope section of `.editorconfig` now names `mesh_ops/ExactMeshArrangement.cs` as a whole (the `type` line of `TriangleBoxTree` is gone) and
`mesh_ops/ExactMeshBoolean.cs`; `DeterministicApis.txt` has 423 external members, 69 restricted entries with 248 site lines (144 with a form and
its tests), 72 registered delegate calls and 57 string concatenations (ledger line in 5.4, generated). Plan #1241, owner decisions of
2026-11-22: the rounding stage is a named assumption (decision 1a); `Math.Min / Max` of doubles are replaced, not argued.

**What changed in the code** (each output-identical except the one that fixes a schedule dependence, the `Coplanar` sort: the harness cases `boolean.*`, `stage1.*`, `kernel.*`, `repair`, `alphawrap`, `solidify` and the
new `stage2.*` have the same traces as master, and the suites of the exact classes pass):

| change | why |
|---|---|
| 128 `Math.Min / Max` of doubles in 61 members became `MathUtil.MinExact / MaxExact` | R4: the result for `(+0, -0)` differs between .NET Framework and .NET Core; the two agree bit for bit on .NET Core (`MathUtilMinMaxExactTests`), so no result changes there |
| the three `GetHashCode` calls outside a hash implementation: `PointKey.Mixed()`, the hash of `ExactIdComparer.GetHashCode`, `ExactPoint3.ShardBits()` for the shard of `AddPoint` | R7: a hash value may only reach a hash container. The shard of a rational point now comes from the low bits of its canonical components (unseeded) instead of the seeded hash; `PointHashes_DoNotAffectTheResult` varies the shard choice, `ExactPoint3ShardBitsTests` pin the bits |
| `delegate == null` became `is null` (4 places), one interpolated string and one `string.Format` with a `params` array became explicit | the analyzer counts and cannot register `delegate-combination` and `interpolated-string`; `string.Format` binds one overload on every target |
| **defect found by the site review:** the `Coplanar` list of a triangle without points and segments is sorted before the early return of `prepare_triangle` | the pair loop filled it in schedule order, and `coplanar_stack` turns it into the order of the solid-angle sum of `front_generalized_winding`; `ExactArrangementCoplanarOrderTests` fails on the old code (reversed loop order gives 6,5,4,3,2,1). Identical on the harness cases, and deterministic where master followed the schedule |

**Assumed members** (82; the signature is listed, the body is neither pinned nor analyzed): `MeshRoundingRepair` (36: the rounding stage of the
Boolean with `SafeRounding`, floating-point code with its own sorts and containers; a stage of its own), `ExactMeshEdgeGroups` (10) and
`ExactMeshResultBuilder.TryBuild` (integer code: a stable radix sort and a union-find that roots each class at its smallest member; it could
be brought into the scope cheaply), the mesh structure `DMesh3` (25 members, `DSubmesh3`, `DMeshAABBTree3`'s constructor: pinning a
declaration requires listing every member it uses, which pulls in `DVector`, `RefCountVector`, `SmallListSet`), `PhaseClock` (clock values reach
only the phase dictionaries; reviewed, no flow rule), `DMeshAABBTree3.FastWindingNumber` and `MathUtil.TriSolidAngle`.

**Open obligations** (named; each is in a site line or an assumed member, none is claimed as sound):

- **O-RANDOM, `Random` on open meshes.** `far_points` draws the 64 far points of an open mesh from `Random(Seed + 7919 (mesh + 1))`. For closed
  meshes the winding number is the same for every non-degenerate ray (P4, tests vary `Seed`); for open meshes the Antipodal value near a
  half-integer can snap with one ray and not with another ("one Seed gives one result"), so the result is a function of the sequence of the BCL
  `Random`, which R10 does not allow. No test covers a near tie. Registered as an open obligation in three site lines; the fix (an own
  generator, `mix(ulong)` exists) changes the rays and so the output for open inputs once. Owner decision 2026-11-22: a follow-up PR switches open
  meshes to an own SplitMix64 generator, after measuring the golden-master and pinned-counter impact; this PR only registers the obligation.
- **O-ATAN2, the floating-point winding number reaches the exact result of open meshes**: `FastWindingNumber` and `TriSolidAngle` end in
  `Math.Atan2`; `front_generalized_winding` sums them and thresholds `ceil(w - 0.5)`. Tier 2 behaviour (F4 of the research note); only the
  Antipodal path avoids it.
- **O-IDS, P5.** The premise "ids are used for equality and lookup only; every ordered use ends in a total order" is by reading, per site. The
  attack is `OrderAdversaryTests` and the harness `adversary` (the loops run backwards and in seeded permutations, which changes the order
  in which `new_id` hands out ids; `boolean.*`, `stage1.*` and `stage2.*` give identical traces). No hook permutes the ids themselves: `new_id`
  is a counter over dense slot arrays, so a permutation needs an indirection (a design change).
- **O-SORTS.** The tie order of equal `lo` in `merge_points` (`Array.Sort(sortLo, order)`) and the normalizing sorts `Segments.Sort`, `fan.Sort`
  (and the sort of `merge_sorted`, which is not redundant) are argued, but no test fails when they are removed (5.1: four mutants that survive).
  Test sketches: a hook that re-permutes runs of equal `lo`; a bowtie whose fan triangles are appended in descending order.
- **O-PERPATCH.** `open_winding_per_patch` (the L1 loop, `TracedOpenWinding = false`) is tested serial against parallel in the given order
  only; the order adversary runs the default configuration.
- **O-DIAG.** "Reaches only diagnostics" (`PhaseClock`, the `Counts`, `PreDecidedPairs` and similar counters) is a review argument; there is no
  flow rule and no test with a hostile clock.
- **O-EXC.** Which exception surfaces from a parallel loop, and whether it is wrapped, is schedule dependent in every loop but `each`
  (R19, outside the claim).
- **O-SHEET.** The sheet role (#1223, merged from master into this PR) is in the scope as far as `prepare_sheet`, `label_sheets` and the sheet
  arms of `classify` are concerned (serial loops over the pieces in index order; the sort of `prepare_sheet` has a symmetric reader at every tie);
  `ExactMeshCells` (`Build`, `Label` and the fields of its results) is assumed: it is serial and uses integers and exact signs, but its code is
  not under the proof (the padded box of `MayNest` is computed from `ExactPoint3.ToDouble`). Tested serial against parallel and over repeated
  runs by `ExactBooleanSheetTests.Determinism_*`; no order adversary runs the sheet role.
- **F2** is registered as P4 with the perturbation tests of the libm calls (`ExactMeshBooleanTestsAntipodalLibmPerturbation`).

**Claim.** On every runtime that satisfies T1 to T5, `ExactMeshArrangement` and `ExactMeshBoolean` compute, for the exact result (and not for the
assumed members above), functions of their arguments, up to the open obligations: no result depends on the schedule, the process, a hash
seed, the order of a hash container, the order of equal keys in a sort, the sign of a zero in a minimum or maximum, the clock or the BCL
version, except through the open obligations above (O-RANDOM, O-ATAN2, O-IDS, O-SORTS, O-PERPATCH, O-DIAG, O-EXC, O-SHEET) and the assumed members.
Checked: the allow list, the registered sites, constructs, casts and `#if` blocks, and that the named tests exist. Attacked by test: P1 to P5 per
site by the tests named in the site lines, the harness cases and the loop-order adversary.

## 6. Proposal

### 6.1 Which operations first

| stage | scope | size | forms needed | what it proves | when |
|---|---|---|---|---|---|
| 0 | the exact kernel: `math/Exact*`, `AdaptivePredicates`, `FixedPoint3`, `math/Generated`, `ExactTriangleIntersection`, `TriangleTriangleFilter` | about 5 200 lines, sequential, no containers in results | R0 to R6, R15; every public predicate is P4 | L0 and L1 of every predicate and exact construction; the base of all later P4 arguments | now; the files are stable apart from generated code |
| 1 | exact building blocks: `ExactTriangulation2`, `ExactPlanarSimplify`, `TriangleBoxTree`, `SnapRounding2` | about 1 600 lines plus the tree (a class inside `ExactMeshArrangement.cs`, about 750 lines); sorts, containers, one parallel build | plus R7, R8, R11, R12 with P1 to P3 | L0 and L1 of the 2D triangulation and the box tree (serial build equals parallel build is already argued in the allow list) | built: section 5.4 |
| 2 | `ExactMeshArrangement`, `ExactMeshBoolean` | about 8 200 lines, 17 allow-list entries | all, P5 included | L0 and L1 of the exact Boolean, as a ledger of obligations | after Exact Boolean 3.0: the files are held by its sessions and change weekly |
| 3 | one floating-point operation without P4 (suggestion: `MarchingCubes`, then `Reducer`) | | P1, P2 only: internal determinism | L0, and L1 if libm-free | only if the owner wants a claim beyond the exact code |
| not now | `TerrainSlope*` (libm, section 5), the geodesic family (`CoreMath` in place, but linear solvers and long floating-point chains), readers and writers | | | | each needs a fix or a decision first |

The stage 0 claim reads: "On every runtime satisfying T1 to T4, every predicate of the exact kernel returns the sign
defined by its specification; it is therefore the same on all of them. Checked: allow list (n members), 18 registered
`#if NET` equivalences, 9 registered uninitialized buffers. Assumed: T1 to T5." This is small, true to what is checked,
and the first statement about determinism in this library that does not rest on a sample of inputs.

### 6.2 Tooling

1. **Rules `G3D0001` and following** in `tools/analyzers/G3ExactCodeAnalyzers` (one more analyzer class, scoped by
   `.editorconfig` like G3E0004; a scope is a file list, so stages are config changes):
   - `G3D0001` allow list of external members (R0; file `DeterministicApis.txt`, one member and one reason per line;
     first filled by listing what the scope calls today, then reviewed line by line).
   - `G3D0002` enumeration of hash containers (R8), `G3D0003` sorts without a registered total comparer or tie repair
     (R11), `G3D0004` environment values reaching a non-diagnostic member (R7, R9, R13; local dataflow inside a method,
     attributes `[Diagnostic]`, `[UniqueResult]`, `[EnvironmentValue]` at method boundaries), `G3D0005` unregistered
     `#if` (R6), `G3D0006` `double -> integer` cast without a guard (R5), `G3D0007` mutable statics (R14),
     `G3D0008` uninitialized buffers (R15).
   - Each rule has an allow list in the style of `ExactCodeAllowList.cs`; an entry names the form (P1 to P5) and its
     test. The ledger is the union of these entries, printed by a small tool into the design note of the scope, so the
     claim and its open obligations are generated, not written by hand.
2. **Environment adversary** (option B; the "determinism test generator"). Choke points in the library, switched by an
   internal test hook as `PermutationSeedForTests` and `gParallel.SerialOnThisThread` are today:
   - schedule: `gParallel` runs a loop forward, backward, in a seeded permutation, and with one item per task;
   - ids: the id allocator of the arrangement hands out ids in a seeded permutation (attacks P5 directly);
   - hash order: an internal enumeration helper yields a hash container's elements in a seeded permutation (R8);
   - ties: an internal sort wrapper shuffles before sorting (R11);
   - libm: `Math.Sin` and the others behind a wrapper that moves the result by one ulp at random; a result that changes
     depends on libm. This finds the dependence on one machine, where the measurement needs three operating systems;
   - memory: stack buffers and pooled arrays prefilled with a pattern (R15).
   The generator part: for every public operation with `Compute()` the harness runs base against each perturbation and
   compares bits (the harness has the cases, the trace format and `compare`). The analyzer makes the adversary
   complete in a scope: rule `G3D0001` forbids the raw API, so every use goes through a choke point.
3. **Probes for T1, T3, T4** in `tools/DeterminismHarness`: `Math.Min / Max` on signed zeros and NaN, out-of-range
   casts, `Random(seed)` sequence, `BigInteger.GetHashCode`, dictionary enumeration after removals, sort of equal keys
   at sizes above the insertion-sort threshold. They settle the "to confirm" entries of section 5 in one workflow run.

### 6.3 Cost

| piece | size | tier | notes |
|---|---|---|---|
| probes (6.2 item 3) | tools only, one small PR | S / M | one `determinism.yml` run; decides R4, R5, R7, R10 |
| adversary, schedule / tie / hash-order / libm hooks, run over the harness cases | one PR; library hooks are internal and off by default | M (L where it touches exact files: review) | no behaviour change; the hooks cost a branch per loop or sort, to be measured in the Boolean benchmark (expected below noise; if not, the hooks compile only into a test configuration) |
| analyzer rules `G3D0001` to `0008` with tests and mutants | one PR, about the size of the present analyzer file twice (247 lines today) plus tests | L (tooling for exact code) | `G3D0004` is the uncertain part: a first version is intra-procedural with attributes; precision is measured as the number of allow-list entries it forces in stage 0 and 1 |
| stage 0 on (scope config, allow list, registrations, fixes) | one PR | L | expected fixes: few (the kernel has no containers and no libm); mutation shards of the kernel files are touched only if code changes |
| stage 1 on | one PR | L | expected: comparers registered, some `foreach` rewritten |
| stage 2 on | one PR after 3.0 | L | the 17 allow-list entries restated as forms; the P5 obligations decide whether option C or D is needed; the two `.Values` sites and `merge_points` ties get a fix or an argument |

Run time cost of the claim itself: none (static), except the optional guard T4 (four floating-point operations per
`Compute`).

### 6.4 What stays unproven

- The trusted base T1 to T5: JIT, BCL, hardware, analyzer. Guarded by probes per runtime version, not proven. A new
  .NET version or a new architecture is outside the claim until its probes ran.
- The obligations: P3 (comparer total), P4 (exactness: this is the correctness of the exact code itself, with its
  error-bound reviews and oracle tests), P5 (renaming invariance), R14 / R15 (overwritten before read), R6 (branch
  equivalence). Each is reviewed and attacked by a test, not derived by a tool. The method's gain is that they are few,
  listed, and typed; its honesty is that the ledger is part of the claim.
- Inputs: NaN and infinities; the sign of zero where it is not normalized.
- Failure paths: out of memory, stack depth, cancellation.
- Everything outside a scope: floating-point operations until stage 3, libm users without `CoreMath`, text I/O.

## 7. Options for the owner, ranked

How others solve it: section 3; in one line, the working combinations in the literature are a restricted language with
audited escapes (DPJ, constant-time verification) and adversarial exploration of unspecified behaviour (NonDex); full
proofs exist only below a verified compiler (CompCert), which .NET does not have.

| rank | option | what the owner gets | cost | leaves open |
|---|---|---|---|---|
| 1 | **B. Environment adversary + probes** (6.2 items 2, 3) | within one or two PRs: every unspecified choice perturbed on all harness cases, on one machine; the "to confirm" findings of section 5 decided; the number of real dependences in the library known, which is the upper-bound check for A | two small to medium PRs | sampling, though of the environment, not of platforms; no completeness outside a scope |
| 2 | **A. Closed-world analyzer + obligations ledger**, stages 0, 1, then 2 | a stated, generated claim per scope: assumptions, checked rules, numbered obligations; L1 for sequential exact code without per-operation arguments; regressions become build errors | one analyzer PR (L), one PR per stage | section 6.4 |
| 3 | **G. Canonical result order** for the exact Boolean (vertices and triangles in an order defined by content; checked per run by a small verifier) | order independence by definition: every upstream dependence that only permutes the result disappears; shrinks the P5 obligations of stage 2 | one PR after 3.0; `O(n log n)` at the end, to be measured; the result order changes once (not an API break, but visible to golden masters) | dependences in content (which diagonal, which representative) |
| 4 | **C. Deterministic ids by construction** in the arrangement (collect per item, number by prefix sums; deterministic reservations) | P5 disappears where applied: ids are a function of the input | per phase, with a benchmark; the ROADMAP already lists "deterministic round-based parallel" as a future item (owner 2026-11-12, option B) | none for the phases converted |
| 5 | **D. Type-level discipline** (`Unordered<T>`, abstract id type) | P5 and R8 checked by the compiler everywhere, with a renaming theorem | invasive in 8 000 lines of hot exact code; performance re-measured; only after 3.0 | P3, P4 |
| 6 | **E. Determinacy-race detection or schedule exploration** | a per-input proof over all schedules for lock-free fork-join code | an IL instrumentation tool that does not exist for .NET; flags the Boolean's intended races, which need P5 anyway | everything but L0-S |
| 7 | **F. Formal verification** (relational logic, or a port of the kernel to a verified language) | a real proof of the kernel | out of proportion: no C# semantics, no verified JIT; person-years | the JIT, still |

Recommended: B now (cheap, decides open facts, useful for the whole library), A stage 0 and 1 next, then A stage 2
together with G after Exact Boolean 3.0, with C for the phases whose P5 obligation cannot be argued. D, E, F not now.

**Decisions asked of the owner** (answered: section 8):
1. Is "machine-checked reduction to listed assumptions and obligations" (section 1) the accepted meaning of "proof" for
   certification use, or is a stronger form wanted for the kernel (then F for a small part, with its cost)?
2. The observable result (section 2): timings and counters are diagnostic and outside the claim. Agreed?
3. Go for B (probes first, then the adversary)?
4. Go for A stage 0 after B's numbers?
5. The candidates of section 5 that concern released behaviour (`TerrainSlope*` libm, `Math.Min / Max` zeros, casts):
   confirm by probe first (part of B), then one issue each if real.

## 8. Owner decision (2026-11-13) and plan

Relayed by the coordinator on #776: (1) "proof" means the machine-checked reduction to listed assumptions and
obligations of section 1; (2) timings and counters are diagnostic and outside the claim; (3) option B first (probes,
then the environment adversary); (4) option A stage 0 after B's numbers; (5) the candidates of section 5 are confirmed
by probe first, then one GitHub issue for each that is real. Options G, C, D wait for stage 2; E and F are not pursued.
The state of each step is in its PR.

## 9. Sources

Papers and standards (approach only; no code used): Bernstein, "Analysis of programs for parallel processing", IEEE
Trans. EC 1966. Bocchino et al., "A type and effect system for Deterministic Parallel Java", OOPSLA 2009. Blelloch,
Fineman, Gibbons, Shun, "Internally deterministic parallel algorithms can be fast", PPoPP 2012. Kuper, Newton, "LVars:
lattice-based data structures for deterministic parallelism", FHPC 2013. Marlow, Newton, Peyton Jones, "A monad for
deterministic parallelism", Haskell 2011. Rinard, Diniz, "Commutativity analysis", TOPLAS 1997. Reynolds, "Types,
abstraction and parametric polymorphism", 1983; Wadler, "Theorems for free!", FPCA 1989. Volpano, Smith, Irvine, "A
sound type system for secure flow analysis", JCS 1996; Sabelfeld, Myers, "Language-based information-flow security",
IEEE JSAC 2003. Almeida et al., "Verifying constant-time implementations", USENIX Security 2016. Clarkson, Schneider,
"Hyperproperties", JCS 2010. Barthe, D'Argenio, Rezk, "Secure information flow by self-composition", CSFW 2004. Barthe,
Crespo, Kunz, "Relational verification using product programs", FM 2011. IEEE 754-2019, clause 11. ECMA-335, I.12.1.3.
Monniaux, "The pitfalls of verifying floating-point computations", TOPLAS 2008. Boldo, Jourdan, Leroy, Melquiond,
"Verified compilation of floating-point computations", JAR 2015. Sibidanov, Zimmermann, Glondu, "The CORE-MATH project",
ARITH 2022. Demmel, Nguyen, "Fast reproducible floating-point summation", ARITH 2013. Collange, Defour, Graillat,
Iakymchuk, "Numerical reproducibility for the parallel reduction on multi- and many-core architectures", Parallel
Computing 2015. Shi, Gyori, Legunsen, Marinov, "Detecting assumptions on deterministic implementations of
non-deterministic specifications", ICST 2016. Feng, Leiserson, "Efficient detection of determinacy races in Cilk
programs", SPAA 1997. Olszewski, Ansel, Amarasinghe, "Kendo", ASPLOS 2009. Bergan et al., "CoreDet", ASPLOS 2010.
Musuvathi et al., "Finding and reproducing Heisenbugs in concurrent programs", OSDI 2008. McConnell, Mehlhorn, Näher,
Schweitzer, "Certifying algorithms", Computer Science Review 2011. Microsoft documentation: Durable Task Roslyn
analyzer; `System.Random` remarks (sequence not guaranteed across versions); .NET Core 3.0 breaking changes
(`Math.Min / Max`); .NET 9 floating-point to integer conversions.

Citations were written from memory and spot-checked by search for NonDex, DPJ, internal determinism, ECMA-335 I.12.1.3,
the Durable Task analyzer and the reproducible-summation line; the remaining bibliographic details (venue, year) should
be checked before they are copied elsewhere. The runtime facts of R4, R5 and R7 are measured (5.1).
