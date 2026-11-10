using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Meshing2Acceptance;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The mesh of seed s of <see cref="StarPolygonFuzz"/> at M3 (snap grid, 20 degrees as the generator sets it) and its oracle report,
    /// computed once for the tests that judge all 3000 seeds the same way (QualityMesh2KeepsChainsApartTests, QualityMesh2OracleRestoreTests):
    /// each of them ran the mesher and the oracle on every seed itself, 25 s of work twice. The tests build their own input with
    /// <c>StarPolygonFuzz.Make</c> for what they need it for (it is deterministic); the cache keeps only the status and the report.
    /// Thread safe: classes run in parallel, and a seed is computed by whichever asks first.
    /// </summary>
    internal static class StarFuzzCache
    {
        internal sealed class Result { public Mesh2Status Status; public OracleReport Report; public int Triangles, Vertices; }

        static readonly ConcurrentDictionary<int, Lazy<Result>> Results = new ConcurrentDictionary<int, Lazy<Result>>();

        internal static Result Get(int seed) => Results.GetOrAdd(seed, s => new Lazy<Result>(() => Compute(s), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        internal static Result Compute(int seed)
        {
            var input = StarPolygonFuzz.Make(seed);
            var o = new QualityMesh2Candidate().Run(input);
            return new Result { Status = o.Status, Triangles = o.Triangles?.Length ?? 0, Vertices = o.VX?.Length ?? 0, Report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 }) };
        }

        /// <summary> all seeds below n, in parallel (a test then takes the wall time of the work on every core, not of one) </summary>
        internal static void Prime(int n) => Parallel.For(0, n, s => Get(s));
    }
}
