using System;

namespace Meshing2Acceptance
{
    public static partial class Oracle
    {
        /// <summary>Source coverage lookup: grid (default) or the previous scan. Reference/exact-coordinate runs retain the scan.</summary>
        public const string SourceIndexVariable = "G3_ORACLE_SOURCE_INDEX";

        /// <summary>Validate the source lookup setting before a command starts its self-checks or geometry work.</summary>
        public static void ValidateSourceIndexSettings() => SourceIndexSetting();

        static string SourceIndexSetting()
        {
            string configured = Environment.GetEnvironmentVariable(SourceIndexVariable);
            string mode = configured?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(mode)) return "grid";
            if (mode == "grid" || mode == "scan") return mode;
            throw new InvalidOperationException(SourceIndexVariable + "='" + configured + "' accepts grid or scan");
        }

        // Same per-thread diagnostic pattern as ImageWorkForTests. Counts source candidates
        // actually visited by coverage queries, including the early Sources inference pass.
        internal static long SourceWorkForTests => Run.SourceWorkForTests;
        internal static long SourceGridWorkForTests => Run.SourceGridWorkForTests;

        sealed partial class Run
        {
            readonly bool indexSourceCoverage;
            [ThreadStatic] internal static long SourceWorkForTests;
            [ThreadStatic] internal static long SourceGridWorkForTests;
            const int SourceGridQueryWorkLimit = 4096;
            BoxGrid sourceCoverGrid;

            void EnsureSourceBoxes()
            {
                if (sBox != null) return;
                // The existing Filters/Marks envelope, unchanged. Sources inference needs it
                // before Filters; later callers reuse these same boxes.
                double reach = double.NaN;
                if (tube2.Bounds(out _, out double t2hi)) reach = Math.Sqrt(t2hi) * (1 + 1e-12) + 1e-300;
                sBox = new double[4 * sources.Count];
                for (int k = 0; k < sources.Count; ++k) SegmentBox(sources[k].a, sources[k].b, reach, sBox, 4 * k);
            }

            bool ScanSourceCovers(int a, int b, bool countWork)
            {
                foreach (var s in sources) {
                    if (countWork) ++SourceWorkForTests;
                    if (WithinTube(s, v[a]) && WithinTube(s, v[b])) return true;
                }
                return false;
            }

            bool SourceCovers(int a, int b)
            {
                if (!indexSourceCoverage || !Fast) return ScanSourceCovers(a, b, true);
                if (sources.Count == 0) return false;
                double ax0 = 0, ax1 = 0, ay0 = 0, ay1 = 0, bx0 = 0, bx1 = 0, by0 = 0, by1 = 0;
                bool aKnown = v[a].Bounds(false, out ax0, out ax1) && v[a].Bounds(true, out ay0, out ay1);
                if (!aKnown) return ScanSourceCovers(a, b, true);
                bool bKnown = v[b].Bounds(false, out bx0, out bx1) && v[b].Bounds(true, out by0, out by1);
                EnsureSourceBoxes();
                if (sourceCoverGrid == null) sourceCoverGrid = new BoxGrid(sBox, sources.Count);
                // A full candidate list can be quadratic where the old Any scan
                // stops at its first source (dense/unknown boxes, or a cluster
                // beside a far outlier). Bound the extra enumeration work and
                // retain the scan's early exit whenever the grid is unhelpful.
                var candidates = sourceCoverGrid.QueryBounded(ax0, ax1, ay0, ay1, SourceGridQueryWorkLimit, out int gridWork);
                SourceGridWorkForTests += gridWork;
                if (candidates == null) return ScanSourceCovers(a, b, true);
                if (CheckFastPathForTests)
                    BoxGrid.CheckSuperset(sBox, sources.Count, ax0, ax1, ay0, ay1, candidates, "the sources covering an edge");
                bool covers = false;
                foreach (int k in candidates) {
                    ++SourceWorkForTests;
                    int at = 4 * k;
                    // Coverage requires BOTH endpoints within the same source tube. The
                    // source box widened by an upper bound of the tube must meet each
                    // endpoint's closed interval box. Unknown boxes never exclude a source.
                    // Grid candidates stay in source order; both exact leaf tests stay intact.
                    if (!double.IsNaN(sBox[at]) &&
                        (ax1 < sBox[at] || ax0 > sBox[at + 1] || ay1 < sBox[at + 2] || ay0 > sBox[at + 3]
                         || (bKnown && (bx1 < sBox[at] || bx0 > sBox[at + 1] || by1 < sBox[at + 2] || by0 > sBox[at + 3])))) continue;
                    var s = sources[k];
                    if (WithinTube(s, v[a]) && WithinTube(s, v[b])) { covers = true; break; }
                }
                if (CheckFastPathForTests && covers != ScanSourceCovers(a, b, false))
                    FastPathDiffers("the source covering an edge");
                return covers;
            }
        }
    }
}
