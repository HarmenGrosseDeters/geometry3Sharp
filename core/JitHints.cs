// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System.Runtime.CompilerServices;

namespace g3
{
    internal static class JitHints
    {
        /// <summary>
        /// MethodImplOptions.AggressiveOptimization (.NET Core 3.0 and later; not part of the netstandard2.0 enum, and
        /// ignored by the .NET Framework): the method is compiled fully optimized at its first call. For the loops of
        /// the exact boolean pipeline: with tiered compilation they otherwise start as unoptimized code and are
        /// replaced only after many iterations, which made a single boolean of a mesh with 10k-50k triangles 2-3
        /// times slower than the optimized code (benchmarks/BooleanComparison/README.md, profile). The generated
        /// code computes the same IEEE results (no contraction to FMA), only faster.
        /// </summary>
        internal const MethodImplOptions Optimize = (MethodImplOptions)512;
    }
}
