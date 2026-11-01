# PredicateCompiler (bounds of the fixed-width predicates)

Derives the bit bounds of the exact predicates of `ExactMeshArrangement` on fixed-width points (`FixedPoint3`,
`FixedArith`) from their formulas in `predicates.spec`: for every intermediate value a proven magnitude bound (a
polynomial in `m = 2^255 - 1`, the bound of every component), its limbs, and the limbs `FixedArith` takes for it.
The rules are in the header of `Compiler.cs`; the design is in `docs/design/FIXED_WIDTH_EXACT_POINTS.md`, sections 5
and 13.

    dotnet run --project tools/PredicateCompiler -- --table tools/PredicateCompiler/predicates.spec
    dotnet run --project tools/PredicateCompiler -- --doc tools/PredicateCompiler/predicates.spec docs/design/FIXED_WIDTH_EXACT_POINTS.md

The second command rewrites the generated table block of the design doc. `GeneratedPredicatesTests` fails when the
block differs from the derivation, when a value exceeds its bound on inputs at `±m`, when the spec's sign differs from
the kernel's, or when the kernels' scratch constants are below the derived scratch.

The value bounds know nothing of a runtime; the limbs taken are a model of `FixedArith` in a separate pass. Further
passes over the same expression graph (a floating-point filter and expansion stages as in #221, predicates on
implicit points whose inputs are expressions of a construction's inputs) can be added beside them.

It is a review tool, not a code generator: generating the kernels as well (`math/Generated/FixedPredicates.cs`,
branch `claude/predicate-compiler-fixed-spike`) was measured without a gain (section 13 of the design doc).

Spec format: `ipredicate Name(a, b, ...) bound b` (the points; every input component has `|v| ≤ m = 2^b - 1`,
`b = 255` for `FixedPoint3`), definitions `x = expression` (`+`, `-`, `*` and parentheses
over the inputs and earlier names), then `sign expression`. The inputs are `p.x`, `p.y`, `p.w` of a point `p` (the
components on the projection axes i and j, and W). Write the operations in the kernel's order: a value written twice
is computed once, as in the kernel.
