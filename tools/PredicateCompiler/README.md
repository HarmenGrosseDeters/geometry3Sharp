# PredicateCompiler (experiment)

Generates `math/Generated/GeneratedPredicates.cs` from `predicates.spec`. Each predicate becomes:
- a floating-point filter whose error bound is derived by machine;
- exact expansion stages B, C and D;
- a BigInteger fallback.

The error model and the rules are in the header of `Compiler.cs`. The design, the results and the next steps are in
`docs/design/PREDICATE_COMPILER.md`.

    dotnet run --project tools/PredicateCompiler -- tools/PredicateCompiler/predicates.spec math/Generated/GeneratedPredicates.cs

`GeneratedPredicatesTests.GeneratedFile_IsUpToDate` fails when the checked-in file differs from the generator's output.

Spec format: `predicate Name(inputs)`, then definitions `x = expression` (`+`, `-`, `*` and parentheses over inputs
and earlier names), then `sign expression`. The top of the formula must be a sum or a difference, and the degree may
be at most 6. Differences of two inputs are the leaves of the exact stages. The comment block right before a
predicate is copied into the output.
