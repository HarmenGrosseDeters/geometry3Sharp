# PredicateCompiler (bounds of the fixed-width predicates)

Derives the bit bounds of the exact predicates of `ExactMeshArrangement` on fixed-width points (`FixedPoint3`,
`FixedArith`) from their formulas in `predicates.spec`: for every intermediate value a proven magnitude bound (a
polynomial in `m = 2^255 - 1`, the bound of every component), its limbs, and the limbs `FixedArith` takes for it.
The rules are in the header of `Compiler.cs`; the design is in `docs/design/FIXED_WIDTH_EXACT_POINTS.md`, sections 5
and 13.

    dotnet run --project tools/PredicateCompiler -- --table tools/PredicateCompiler/predicates.spec
    dotnet run --project tools/PredicateCompiler -- --doc tools/PredicateCompiler/predicates.spec docs/design/FIXED_WIDTH_EXACT_POINTS.md
    dotnet run --project tools/PredicateCompiler -- --code tools/PredicateCompiler/indirect.spec math/Generated/IndirectPredicates.cs
    dotnet run --project tools/PredicateCompiler -- --doc tools/PredicateCompiler/indirect.spec docs/design/INDIRECT_PREDICATES.md
    dotnet run --project tools/PredicateCompiler -- --verify-filters tools/PredicateCompiler/indirect.spec
    dotnet run --project tools/PredicateCompiler -- --code tools/PredicateCompiler/bisector.spec math/Generated/BisectorPredicates.cs BisectorPredicates
    dotnet run --project tools/PredicateCompiler -- --doc tools/PredicateCompiler/bisector.spec docs/design/BISECTOR_PREDICATES.md
    dotnet run --project tools/PredicateCompiler -- --verify-filters tools/PredicateCompiler/bisector.spec

The second command rewrites the generated table block of the design doc. `--verify-filters` runs an offline
reverse-mode Symbolic Taylor error estimator (`SymbolicTaylorVerify.cs`, Solovyev et al. 2018) with exact
IEEE-754 binary64 binade `half-ulp` bounds directly on every `inputs xyz` predicate's parsed DAG over bounded boxes,
performing a global box-max consistency check (`sup_box |e~ - e| <= C * M_max(box)`) plus a sampled pointwise check
against exact rationals on concrete points without external tools. This is a global consistency check plus sample
evidence rather than a pointwise proof of `|e~ - e| <= C * m~(x)` (which `tools/Z3Pilot/filter_rules.py` proves
inductively across the full range gate). `GeneratedPredicatesTests` fails when the
block differs from the derivation, when a value exceeds its bound on inputs at `±m`, when the spec's sign differs from
the kernel's, when the kernels' scratch constants are below the derived scratch, or when filter checks fail.

The value bounds know nothing of a runtime; the limbs taken are a model of `FixedArith` in a separate pass. Further
passes over the same expression graph (a floating-point filter and expansion stages as in #221, predicates on
implicit points whose inputs are expressions of a construction's inputs) can be added beside them.

`bisector.spec` holds the bisector predicates of the restricted Voronoi diagram (docs/design/BISECTOR_PREDICATES.md),
generated into the class `BisectorPredicates`: the optional last argument of `--code` names the class (default
`IndirectPredicates`); `GeneratedPredicatesBisectorTests` checks the file, the table and the argument.

`indirect.spec` holds the indirect predicates (docs/design/INDIRECT_PREDICATES.md): `--code` generates their exact
stage (`FixedArith` within the bound, `BigInteger` beyond); `GeneratedPredicatesIndirectTests` fails when the
checked-in file or the doc's table differs from the output. For `predicates.spec` the tool stays a review tool: generating the kernels as well (`math/Generated/FixedPredicates.cs`,
branch `claude/predicate-compiler-fixed-spike`) was measured without a gain (section 13 of the design doc).

Spec format: `ipredicate Name(a, b, ...) bound b [inputs xyz]` (the points; every input component has
`|v| ≤ m = 2^b - 1`, `b = 255` for `FixedPoint3`; `inputs xyz`: the coordinates of integer points instead of the
projected `x, y, w`), definitions `x = expression` (`+`, `-`, `*` and parentheses
over the inputs and earlier names), then `sign e1, e2, ...` (the product of their signs). With `inputs xyz`, an optional line `expansion none` skips the expansion stage explicitly (the filter, then the exact
stage on FixedArith; without it a predicate whose expansion would be short enough is refused, since that stage is not
generated). With `inputs xyz`, an optional line `prepare a, b` before the sign line (a proper subset of the points) also generates the prepared form (INDIRECT_PREDICATES.md section 12). The inputs are `p.x`, `p.y`, `p.w` of a point `p` (the
components on the projection axes i and j, and W). Write the operations in the kernel's order: a value written twice
is computed once, as in the kernel.

## The monomial pass (`Monomials.cs`, `doubles.spec`)

Exact sign predicates on double coordinates of any exponent (docs/design/EXACT_FALLBACK_MONOMIALS.md): the stage of
`ExactPredicates.Orient2d`, `Orient3d` and `InCircle` beyond the range of the expansions.

    dotnet run --project tools/PredicateCompiler -- --dtable tools/PredicateCompiler/doubles.spec
    dotnet run --project tools/PredicateCompiler -- --dcode tools/PredicateCompiler/doubles.spec math/Generated/DoublePredicates.cs
    dotnet run --project tools/PredicateCompiler -- --ddoc tools/PredicateCompiler/doubles.spec docs/design/EXACT_FALLBACK_MONOMIALS.md

Spec format: `dpredicate Name(a, b, ...) inputs xy|xyz`, definitions and one `sign` line as for `ipredicate`; the inputs
are the double coordinates `p.x`, `p.y` (, `p.z`). The formula must be a homogeneous polynomial whose monomials have the
coefficient +1 or -1 after merging (a determinant with a column of ones). The pass expands it into its monomials,
derives the limbs of the two accumulators (the rules are in the header of `Monomials.cs`) and generates a kernel
without floating-point arithmetic. `GeneratedDoublePredicatesTests` fails when the checked-in file or the doc's table
differs from the output, when the limb rule fails for an exponent span, or when a kernel's sign differs from the
BigInteger determinant.
