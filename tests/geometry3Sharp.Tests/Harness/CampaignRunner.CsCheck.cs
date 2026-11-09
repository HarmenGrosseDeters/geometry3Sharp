using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CsCheck;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The runner of the seeded CsCheck campaigns (net10.0 only, like every .CsCheck.cs file): case i of a campaign
    /// comes from its own seed (campaign, i), so a campaign is reproducible and its cases are independent. A failing
    /// seed is shrunk deterministically (ExactBooleanPropertyTests.Shrink) and reported with the replay command.
    /// Environment (the same names as in ExactBooleanPropertyTests):
    ///   G3_PROPERTY_SAMPLES=n               n cases per campaign instead of the CI default
    ///   G3_PROPERTY_CAMPAIGN=name G3_PROPERTY_REPLAY=seed   one case of the named campaign
    ///   G3_PROPERTY_FIRST=i                 the first case index (to continue or split a large campaign)
    ///   G3_PROPERTY_ALL=1                   go on after a failure and list every failing seed (no shrinking), to triage
    /// Campaign classes carry [Trait("Category", "Campaign")]; the Stryker test filters (tests/mutation) name the test
    /// classes they run and never select that category.
    /// </summary>
    public static class CampaignRunner
    {
        public const string Category = "Campaign";

        public static int Samples(int normal)
        {
            string env = Environment.GetEnvironmentVariable("G3_PROPERTY_SAMPLES");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : normal;
        }

        static int First()
        {
            string env = Environment.GetEnvironmentVariable("G3_PROPERTY_FIRST");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 0;
        }

        /// <summary> runs the property on the cases; property returns "" or the reason it fails; a failure is shrunk and reported </summary>
        public static void Run<T>(ITestOutputHelper log, string testClass, string name, Gen<T> gen, Func<T, string> property,
            int samples, uint campaign, int shrinkIterations = 200)
        {
            void Check(T t) { string r = property(t); if (r != "") throw new Exception(r); }
            var watch = Stopwatch.StartNew();
            string replay = Environment.GetEnvironmentVariable("G3_PROPERTY_CAMPAIGN") == name ? Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") : null;
            string[] seeds = string.IsNullOrEmpty(replay)
                ? Enumerable.Range(First(), Samples(samples)).Select(i => new PCG(campaign, (ulong)i).ToString()).ToArray()
                : new[] { replay };
            log.WriteLine($"{name}: CsCheck campaign {campaign}, {seeds.Length} samples");
            bool all = Environment.GetEnvironmentVariable("G3_PROPERTY_ALL") == "1";
            var failures = new System.Collections.Generic.List<string>();
            foreach (string seed in seeds) {
                string failure = null;
                T original = default(T);
                try { gen.Sample(t => { original = t; Check(t); }, seed: seed, iter: 1, threads: 1, print: t => t.ToString()); }
                catch (CsCheckException e) { failure = e.InnerException?.Message ?? e.Message; }
                if (failure == null) continue;
                if (all) {
                    failures.Add($"FAIL {seed}: {original}\n    {failure.Replace("\n", "\n    ")}");
                    continue;
                }
                string shrunk = ExactBooleanPropertyTests.Shrink(gen, property, seed, shrinkIterations).Report;
                throw new Xunit.Sdk.XunitException($"{name} fails for seed {seed} (campaign {campaign}):\n  {original}\n  {failure}\n" +
                    $"Shrunk ({shrinkIterations} tries, not necessarily minimal): {shrunk}\n" +
                    $"Replay: G3_PROPERTY_CAMPAIGN=\"{name}\" G3_PROPERTY_REPLAY=<seed> dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter \"FullyQualifiedName~{testClass}\"");
            }
            if (failures.Count > 0)
                throw new Xunit.Sdk.XunitException($"{name}: {failures.Count} of {seeds.Length} cases fail:\n" + string.Join("\n", failures));
            log.WriteLine($"{name}: passed in {watch.ElapsedMilliseconds} ms");
        }
    }
}
