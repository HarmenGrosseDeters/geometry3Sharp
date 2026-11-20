# Local Setup

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

## 5. Local setup

The CI steps in CLAUDE.md need the .NET 10 SDK. In a fresh Linux container (Ubuntu or Debian, as in Claude
Code cloud sessions, where the Microsoft download host may be blocked), the distribution package is enough:

```
apt-get update && apt-get install -y dotnet-sdk-10.0
dotnet tool restore            # Stryker.NET, for the mutation tests (tests/mutation/README.md)
```

As an environment setup script, these two lines make every session start with a working SDK. The net48 build
needs no Mono or Windows: it takes its reference assemblies from the `Microsoft.NETFramework.ReferenceAssemblies`
package, which is restored like any other. Running the net48 tests needs Windows (the `netstandard` job in CI).

Culture-sensitive regressions are exercised explicitly with `de-DE` and `en-US`, independently of the host's
locale. The Triangle CLI fixture formats hole seeds invariantly, and `TriangleWriters_AreCultureIndependent`
checks the production `.node` / `.poly` / `.ele` writers and read-back. The production writers already used
invariant formatting; the German-locale failure was in the test fixture. Remesher pinned findings also use
invariant decimal formatting. The generated-file comparison normalizes CRLF to LF on both sides, including
verbatim strings embedded in the generator's Windows checkout; it still checks all non-line-ending content.

```powershell
dotnet test tests\g3cli.Tests -c Release --filter FullyQualifiedName~TrianglePolyCliTests
dotnet test tests\geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~PinnedFailures_AreCultureIndependent|FullyQualifiedName~GeneratedFile_IsUpToDate"
```

Two sessions running the CI steps in the same checkout overwrite each other's build output and package
directory. Run them one after another, or in separate `git worktree`s.
