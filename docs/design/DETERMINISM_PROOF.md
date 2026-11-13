# A proof method for determinism: study and options

Hard problem 11 ([research.md](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)), must-win
item of [ROADMAP.md](../ROADMAP.md) "Depth over breadth". [DETERMINISM.md](DETERMINISM.md) measures: 50 cases on six
configurations, and its section 6 says what that is worth: "absence of a difference is evidence on these inputs, not a
proof". This note asks what a proof can rest on, maps every known source of nondeterminism to a rule a tool can check,
and proposes where to start. It is a study: nothing is built, no library code changed. The code facts below come from
reading and `grep` on master 58644af (line counts are grep counts, so approximate); the findings of section 5 are
candidates from that reading and are **not confirmed by a run**.

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
| R4 | `Math.Min / Max` of doubles: the result for `(+0, -0)` and NaN was changed in .NET Core 3.0 to follow IEEE 754-2008 (documented breaking change; **to confirm with a probe**) | L1 | 237 / 1200 lines | *list*: not on the allow list for `double`; a library `MinExact / MaxExact` with a stated result for zeros instead, or an argument that the sign of zero cannot reach the result | a `-0.0` in a result is a different bit pattern; DETERMINISM.md 6a calls Min / Max "IEEE-exact everywhere", which this narrows |
| R5 | `double -> int / long` casts out of range or of NaN: unspecified in C#; x64 .NET Framework gives `0x80000000`, arm64 and .NET 9 and later saturate (**to confirm with a probe**) | L1, L2 | not counted (needs the semantic model) | *flow*: a cast needs a dominating range check, or a helper with a stated result | range facts beyond simple guards need review |
| R6 | conditional compilation: `#if NET` runs different code on net48 and net10 | L1 | 18 / 61 blocks (`BitLength`, `TrailingZeroCount`, `TryWriteBytes`, generated predicates) | *list*: every block in scope is registered with a differential test of the two branches; the code already compiles the portable branch everywhere "so that the tests on .NET check it"; the analyzer requires the registration | equivalence is tested (exhaustive only for small domains) |
| R7 | hash values: `string`, `ValueTuple`, `HashCode.Combine` are seeded per process on .NET; whether `BigInteger.GetHashCode` (used by `ExactPoint3.GetHashCode`) is stable across runtimes was not verified | L0-R, L1 | 15 / 110 lines; a hash value picks the shard in `ExactMeshArrangement` (`key.GetHashCode() % Shards`); `ValueTuple` keys in `SnapRounding2` | *flow*: a hash value may only go into a hash container or an index of a lookup-only structure; never into an order, a count that is observable, or the result | shard choice is P4 (lookup result unique) with P5 for the ids |
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

## 6. Proposal

### 6.1 Which operations first

| stage | scope | size | forms needed | what it proves | when |
|---|---|---|---|---|---|
| 0 | the exact kernel: `math/Exact*`, `AdaptivePredicates`, `FixedPoint3`, `math/Generated`, `ExactTriangleIntersection`, `TriangleTriangleFilter` | about 5 200 lines, sequential, no containers in results | R0 to R6, R15; every public predicate is P4 | L0 and L1 of every predicate and exact construction; the base of all later P4 arguments | now; the files are stable apart from generated code |
| 1 | exact building blocks: `ExactTriangulation2`, `ExactPlanarSimplify`, `TriangleBoxTree`, `SnapRounding2` | about 1 600 lines plus the tree (a class inside `ExactMeshArrangement.cs`, about 750 lines); sorts, containers, one parallel build | plus R7, R8, R11, R12 with P1 to P3 | L0 and L1 of the 2D triangulation and the box tree (serial build equals parallel build is already argued in the allow list) | after stage 0 |
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

**Decisions asked of the owner:**
1. Is "machine-checked reduction to listed assumptions and obligations" (section 1) the accepted meaning of "proof" for
   certification use, or is a stronger form wanted for the kernel (then F for a small part, with its cost)?
2. The observable result (section 2): timings and counters are diagnostic and outside the claim. Agreed?
3. Go for B (probes first, then the adversary)?
4. Go for A stage 0 after B's numbers?
5. The candidates of section 5 that concern released behaviour (`TerrainSlope*` libm, `Math.Min / Max` zeros, casts):
   confirm by probe first (part of B), then one issue each if real.

## 8. Sources

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
be checked before they are copied elsewhere. Not verified at all: the behaviour of `BigInteger.GetHashCode` across
runtimes (R7), and the two runtime changes of R4 and R5 beyond their documentation (probes, 6.2 item 3).
