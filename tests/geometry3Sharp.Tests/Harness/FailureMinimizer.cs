using System;
using System.IO;
using System.Text.RegularExpressions;
using g3;
using Xunit.Sdk;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Couples MeshMinimizer to the defect and fuzz tests (docs/QA.md 2.9): with G3_MINIMIZE=1, a failing check is
    /// minimized and the failure message gets the minimal case as C# code (also written to MeshMinimizer.ReproDirectory()).
    /// </summary>
    public static class FailureMinimizer
    {
        public static bool Enabled => Environment.GetEnvironmentVariable("G3_MINIMIZE") == "1";

        /// <summary>
        /// the options of one minimization: G3_MINIMIZE_SECONDS the time budget (default 120), G3_MINIMIZE_ROUND=0 keeps
        /// the coordinates (rounding makes a case readable, but can turn it into a degenerate one with the same message)
        /// </summary>
        public static MinimizeOptions Options() => new MinimizeOptions() {
            TimeBudget = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("G3_MINIMIZE_SECONDS"), out int s) && s > 0 ? s : 120),
            SimplifyCoordinates = Environment.GetEnvironmentVariable("G3_MINIMIZE_ROUND") != "0"
        };

        /// <summary>
        /// "the same error": the exception type and the first line of its message with every number replaced by '#'
        /// (counts and ids change while the input shrinks)
        /// </summary>
        public static string Signature(Exception e) => e.GetType().FullName + ": " + Signature(e.Message);

        public static string Signature(string message)
        {
            string first = (message ?? "").Split('\n')[0].Trim();
            return Regex.Replace(first, @"[-+]?\d+(\.\d+)?([eE][-+]?\d+)?", "#");
        }

        /// <summary>
        /// Runs check on make(). If it throws and minimization is enabled, the mesh is minimized while check throws an
        /// exception with the same Signature, and an XunitException with the original message and the C# repro is thrown.
        /// </summary>
        public static void Check(string name, Func<DMesh3> make, Action<DMesh3> check) => Check(name, make, check, Enabled, null);

        public static void Check(string name, Func<DMesh3> make, Action<DMesh3> check, bool enabled, MinimizeOptions options)
        {
            if (enabled == false) { check(make()); return; }
            Exception failure;
            try { check(make()); return; } catch (Exception e) { failure = e; }
            string signature = Signature(failure);
            MeshMinimizer.Result r = MeshMinimizer.Minimize(make(), m => {
                try { check(m); return false; } catch (Exception e) { return Signature(e) == signature; }
            }, options ?? Options());
            string code = MeshMinimizer.ToCSharp(r.Mesh, "Minimized" + MeshMinimizer.Identifier(name),
                $"\"{name}\" minimized by MeshMinimizer ({r}):\n{signature}");
            string path = MeshMinimizer.WriteRepro(name, r.Mesh, code);
            throw new XunitException($"{failure.Message}\n\nminimized ({r}), written to {path}:\n{code}");
        }

        /// <summary>
        /// For a failing reader input: data minimized while check returns a failure with the same Signature, as a C#
        /// expression (also written to MeshMinimizer.ReproDirectory() as name.extension). Returns the report text.
        /// </summary>
        public static string BytesReport(string name, string extension, byte[] data, string failure, Func<byte[], string> check, bool text, MinimizeOptions options = null)
        {
            string signature = Signature(failure);
            DeltaDebug.BytesResult r = DeltaDebug.MinimizeBytes(data, d => Signature(check(d)) == signature, text, options ?? Options());
            if (r.Reproduced == false)
                return "not reproduced by the minimizer";
            string path = Path.Combine(MeshMinimizer.ReproDirectory(), MeshMinimizer.Identifier(name) + "." + extension);
            File.WriteAllBytes(path, r.Data);
            return $"minimized to {r.Data.Length} of {data.Length} bytes ({r.Evaluations} evaluations{(r.BudgetExhausted ? ", stopped" : "")}), written to {path}: "
                + DeltaDebug.ToCSharp(r.Data);
        }
    }
}
