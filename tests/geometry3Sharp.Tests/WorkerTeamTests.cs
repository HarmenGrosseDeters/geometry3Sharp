using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// WorkerTeam (core/WorkerTeam.cs), the thread team of MeshAlphaWrap's round-based carving: every index runs exactly once
    /// per loop, over many short loops in a row; the result of independent bodies does not depend on the team size; an
    /// exception of a body reaches the caller unwrapped and the team stays usable; Dispose ends the workers; teams that run
    /// at the same time (more threads than cores) do not wait for descheduled workers.
    /// </summary>
    public class WorkerTeamTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void For_EveryIndexOncePerLoop(int threads)
        {
            using (var team = new WorkerTeam(threads)) {
                Assert.Equal(threads, team.Threads);
                foreach (int n in new[] { 0, 1, 2, 3, 17, 300, 10000 }) {
                    var hits = new int[n];
                    for (int loop = 0; loop < 200; ++loop)
                        team.For(n, i => Interlocked.Increment(ref hits[i]));
                    Assert.All(hits, h => Assert.Equal(200, h));
                }
            }
        }

        // independent bodies (each writes its own slot): the same values for every team size and every run
        [Fact]
        public void For_SameResultForEveryTeamSize_AndRun()
        {
            Func<int, double[]> run = threads => {
                var outp = new double[5000];
                using (var team = new WorkerTeam(threads)) {
                    for (int loop = 0; loop < 50; ++loop)
                        team.For(outp.Length, i => outp[i] = Math.Sin(outp[i] + i * 0.001 + loop));
                }
                return outp;
            };
            double[] reference = run(1);
            foreach (int threads in new[] { 2, 4, 8, 4 })
                Assert.Equal(reference.Select(BitConverter.DoubleToInt64Bits), run(threads).Select(BitConverter.DoubleToInt64Bits));
        }

        [Fact]
        public void For_ExceptionReachesTheCaller_TeamStaysUsable()
        {
            using (var team = new WorkerTeam(4)) {
                var e = Assert.Throws<InvalidOperationException>(() => team.For(1000, i => { if (i == 637) throw new InvalidOperationException("637"); }));
                Assert.Equal("637", e.Message);
                // (thrown on a worker or on the caller: the same type, not wrapped)
                for (int k = 0; k < 20; ++k)
                    Assert.Throws<ArgumentException>(() => team.For(64, i => { if (i % 13 == 5) throw new ArgumentException(); }));
                int sum = 0;
                team.For(100, i => Interlocked.Add(ref sum, i));
                Assert.Equal(4950, sum);
            }
        }

        [Fact]
        public void Dispose_EndsTheWorkers()
        {
            var team = new WorkerTeam(4);
            team.For(100, i => { });
            Assert.Equal(3, team.AliveWorkers);
            // (also a worker that blocked between loops)
            Thread.Sleep(200);
            team.Dispose();
            Assert.Equal(0, team.AliveWorkers);
            team.Dispose();
            Assert.Throws<ObjectDisposedException>(() => team.For(10, i => { }));
            var single = new WorkerTeam(1);
            Assert.Equal(0, single.AliveWorkers);
            int n = 0;
            single.For(5, i => n++);
            Assert.Equal(5, n);
            single.Dispose();
        }

        // four teams of four threads at the same time on this machine's cores: the loops end without waiting for workers that
        // are not scheduled (the caller waits only for chunks that were taken); before, a round waited until every worker had
        // run, and a test of the wrap took minutes instead of seconds in a parallel test run
        [Fact]
        public void For_ManyTeamsAtTheSameTime_NoStall()
        {
            var sw = Stopwatch.StartNew();
            Parallel.For(0, 4, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, t => {
                using (var team = new WorkerTeam(4)) {
                    var a = new double[400];
                    for (int loop = 0; loop < 3000; ++loop)
                        team.For(a.Length, i => a[i] += Math.Sqrt(i + loop));
                }
            });
            Assert.True(sw.ElapsedMilliseconds < 4000, $"{sw.ElapsedMilliseconds} ms");
        }
    }
}
