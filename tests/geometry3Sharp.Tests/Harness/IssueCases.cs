namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Shared rule of the *IssueCasesTests classes (docs/QA.md 2.11): their KnownFailures lists, and the specific wrong
    /// outcomes they pin, are taken on net10. On .NET Framework (the net48 CI job) the last bits of transcendental
    /// functions and some casts differ, so a listed case may fail with other numbers or not at all: there the lists are
    /// not strict, as in RobustnessSweepTests. Unlisted cases are checked on both.
    /// </summary>
    public static class IssueCases
    {
#if NETFRAMEWORK
        public static readonly bool Strict = false;
#else
        public static readonly bool Strict = true;
#endif
    }
}
