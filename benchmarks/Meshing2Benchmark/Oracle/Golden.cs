using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>One row of a golden file (schema: docs/design/MESHING2D_GOLDEN_FORMAT.md, section 5).</summary>
    public sealed class GoldenRow
    {
        public string Case, Milestone, Producer, Generator, InputSha256, Status;
        public long Vertices, Triangles, Subsegments, ConstructedVertices, BadTriangles;
        public bool Unique, SteinerLimitReached;
        public string TrianglesFp, SubsegmentsFp, VerticesFp, IdentityFp, Area2, MinAngleSin2, Reason;

        public const int MinAngleDigits = 12;

        public static GoldenRow From(Mesh2Case c, string producer, Mesh2Output o, OracleReport report, string reason)
        {
            return new GoldenRow {
                Case = c.Id, Milestone = c.Milestone.ToString(), Producer = producer, Generator = c.Generator,
                InputSha256 = TextFormat.InputSha256(c.Input), Status = o.Status.ToString(),
                Vertices = o.Triangles.SelectMany(t => t).Distinct().Count(),
                Triangles = o.Triangles.Length, Subsegments = o.Subsegments?.Count ?? 0,
                ConstructedVertices = report.ConstructedVertices, BadTriangles = o.BadTriangles?.Length ?? report.BelowAngle,
                Unique = report.Unique, SteinerLimitReached = o.SteinerLimitReached,
                TrianglesFp = Fingerprint.Triangles(o), SubsegmentsFp = Fingerprint.Subsegments(o), VerticesFp = Fingerprint.Vertices(o),
                IdentityFp = o.IdentityRules ? Fingerprint.Identity(o) : null,
                Area2 = report.Area2.ToString(), MinAngleSin2 = report.MinSin2?.ToDecimal(MinAngleDigits), Reason = reason
            };
        }

        public MiniJson.Obj ToJson()
        {
            var o = new MiniJson.Obj();
            o.Put("case", Case); o.Put("milestone", Milestone); o.Put("producer", Producer); o.Put("generator", Generator);
            o.Put("input_sha256", InputSha256); o.Put("status", Status); o.Put("vertices", Vertices); o.Put("triangles", Triangles);
            o.Put("subsegments", Subsegments); o.Put("constructed_vertices", ConstructedVertices); o.Put("unique", Unique);
            o.Put("triangles_fp", TrianglesFp); o.Put("subsegments_fp", SubsegmentsFp); o.Put("vertices_fp", VerticesFp);
            if (IdentityFp != null) o.Put("identity_fp", IdentityFp);
            o.Put("area2", Area2); o.Put("min_angle_sin2", MinAngleSin2); o.Put("bad_triangles", BadTriangles);
            o.Put("steiner_limit_reached", SteinerLimitReached); o.Put("reason", Reason);
            return o;
        }

        public static GoldenRow FromJson(MiniJson.Obj o)
        {
            string S(string k) => (string)o[k];
            long L(string k) => o[k] == null ? 0 : (long)o[k];
            bool B(string k) => o[k] != null && (bool)o[k];
            return new GoldenRow {
                Case = S("case"), Milestone = S("milestone"), Producer = S("producer"), Generator = S("generator"), InputSha256 = S("input_sha256"),
                Status = S("status"), Vertices = L("vertices"), Triangles = L("triangles"), Subsegments = L("subsegments"),
                ConstructedVertices = L("constructed_vertices"), Unique = B("unique"), TrianglesFp = S("triangles_fp"), SubsegmentsFp = S("subsegments_fp"),
                VerticesFp = S("vertices_fp"), IdentityFp = S("identity_fp"), Area2 = S("area2"), MinAngleSin2 = S("min_angle_sin2"),
                BadTriangles = L("bad_triangles"), SteinerLimitReached = B("steiner_limit_reached"), Reason = S("reason")
            };
        }

        /// <summary>The fields that differ, between what is recorded and what was computed. Producer and reason are not values.</summary>
        public IEnumerable<string> Differences(GoldenRow other)
        {
            var a = ToJson(); var b = other.ToJson();
            foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key))) {
                if (key == "producer" || key == "reason") continue;
                string x = Convert.ToString(a[key]), y = Convert.ToString(b[key]);
                if (x != y) yield return key + ": recorded " + x + ", now " + y;
            }
        }
    }

    public sealed class RemovedRow { public string Case, Reason; }

    /// <summary>A golden file: the rows of one milestone, and the rows removed so far with the reason.</summary>
    public sealed class GoldenFile
    {
        public List<GoldenRow> Rows = new List<GoldenRow>();
        public List<RemovedRow> Removed = new List<RemovedRow>();

        public static GoldenFile Parse(string text)
        {
            var root = (MiniJson.Obj)MiniJson.Parse(text);
            if ((long)root["format"] != 1) throw new FormatException("unknown golden format");
            var file = new GoldenFile();
            foreach (var row in (List<object>)root["rows"]) file.Rows.Add(GoldenRow.FromJson((MiniJson.Obj)row));
            foreach (var row in (List<object>)root["removed"]) {
                var o = (MiniJson.Obj)row;
                file.Removed.Add(new RemovedRow { Case = (string)o["case"], Reason = (string)o["reason"] });
            }
            return file;
        }

        public string Write()
        {
            var root = new MiniJson.Obj();
            root.Put("format", 1L);
            root.Put("rows", Rows.Select(r => (object)r.ToJson()).ToList());
            root.Put("removed", Removed.Select(r => {
                var o = new MiniJson.Obj(); o.Put("case", r.Case); o.Put("reason", r.Reason); return (object)o;
            }).ToList());
            return MiniJson.Write(root);
        }
    }

    public static class GoldenProcess
    {
        /// <summary>
        /// Recomputes every row and says what differs. A case that fails the oracle is reported as such; a golden value never
        /// excuses an invalid result. Used by the CI test and by the benchmark's "golden check".
        /// </summary>
        public static List<string> Check(GoldenFile file, IEnumerable<Mesh2Case> cases, IMeshing2Candidate candidate)
        {
            var problems = new List<string>();
            var byId = cases.ToDictionary(c => c.Id);
            foreach (var row in file.Rows) {
                if (!byId.TryGetValue(row.Case, out var c)) { problems.Add(row.Case + ": recorded, but no such case exists"); continue; }
                var output = candidate.Run(c.Input);
                var report = Oracle.Check(c.Input, output, c.Expect);
                if (!report.Ok) { problems.Add(row.Case + ": fails the oracle: " + report); continue; }
                var now = GoldenRow.From(c, candidate.Name, output, report, null);
                foreach (var difference in row.Differences(now)) problems.Add(row.Case + ": " + difference);
            }
            foreach (var c in byId.Values)
                if (file.Rows.All(r => r.Case != c.Id)) problems.Add(c.Id + ": a case without a recorded row");
            return problems;
        }

        /// <summary>
        /// Writes new values. Refuses unless every case passes the oracle and the differential the caller supplies, and every new
        /// or changed row has a reason. An update without a reason counts as a regression (ACCEPTANCE_STRATEGY section 4).
        /// </summary>
        public static List<string> Update(GoldenFile file, IEnumerable<Mesh2Case> cases, IMeshing2Candidate candidate,
            Func<Mesh2Case, Mesh2Output, OracleReport, string> differential, IDictionary<string, string> reasons)
        {
            var problems = new List<string>();
            var rows = new List<GoldenRow>();
            var caseList = cases.ToList();
            foreach (var c in caseList) {
                var output = candidate.Run(c.Input);
                var report = Oracle.Check(c.Input, output, c.Expect);
                if (!report.Ok) { problems.Add(c.Id + ": fails the oracle, not recorded: " + report); continue; }
                string difference = differential?.Invoke(c, output, report);
                if (difference != null) { problems.Add(c.Id + ": fails the differential, not recorded: " + difference); continue; }
                var old = file.Rows.FirstOrDefault(r => r.Case == c.Id);
                var row = GoldenRow.From(c, candidate.Name, output, report, old?.Reason);
                if (old == null || row.Differences(old).Any()) {
                    if (!reasons.TryGetValue(c.Id, out string reason) || string.IsNullOrWhiteSpace(reason)) {
                        problems.Add(c.Id + ": " + (old == null ? "new row" : "changed: " + string.Join("; ", row.Differences(old))) + ", without a reason");
                        continue;
                    }
                    row.Reason = reason;
                }
                rows.Add(row);
            }
            var removed = new List<RemovedRow>(file.Removed);
            foreach (var old in file.Rows)
                if (caseList.All(c => c.Id != old.Case)) {
                    if (!reasons.TryGetValue(old.Case, out string reason) || string.IsNullOrWhiteSpace(reason))
                        problems.Add(old.Case + ": removed, without a reason");
                    else removed.Add(new RemovedRow { Case = old.Case, Reason = reason });
                }
            if (problems.Count == 0) { file.Rows = rows; file.Removed = removed; }
            return problems;
        }

        /// <summary>
        /// The gate on a pull request: compared with the base branch's file, every changed or new row states a new reason and
        /// every removed row is listed with one. The reviewer sees the reasons in the diff; a machine sees that they exist.
        /// </summary>
        public static List<string> VerifyChanges(GoldenFile before, GoldenFile after)
        {
            var problems = new List<string>();
            foreach (var row in after.Rows) {
                var old = before.Rows.FirstOrDefault(r => r.Case == row.Case);
                if (old == null) {
                    if (string.IsNullOrWhiteSpace(row.Reason)) problems.Add(row.Case + ": new row without a reason");
                    continue;
                }
                var differences = row.Differences(old).ToList();
                if (differences.Count > 0 && (string.IsNullOrWhiteSpace(row.Reason) || row.Reason == old.Reason))
                    problems.Add(row.Case + ": changed without a new reason: " + string.Join("; ", differences));
            }
            foreach (var old in before.Rows)
                if (after.Rows.All(r => r.Case != old.Case) && !after.Removed.Any(r => r.Case == old.Case && !string.IsNullOrWhiteSpace(r.Reason)))
                    problems.Add(old.Case + ": removed without a reason");
            return problems;
        }
    }

    /// <summary>How two rows of one case compare, for each milestone (GOLDEN_FORMAT section 6). Either side may be at fault.</summary>
    public static class RowComparison
    {
        /// <summary>M3 only: "triangle count within a stated factor" (ACCEPTANCE_STRATEGY section 3).</summary>
        public const double M3CountFactor = 2.0;

        public static List<string> Compare(GoldenRow ours, GoldenRow other)
        {
            var d = new List<string>();
            if (ours.InputSha256 != other.InputSha256) { d.Add("different input: " + ours.InputSha256 + " and " + other.InputSha256); return d; }
            if (ours.Status != other.Status) d.Add("status " + ours.Status + " and " + other.Status);
            if (ours.Status != "Success" || other.Status != "Success") return d;
            if (ours.Milestone == "M3") {
                double lo = Math.Min(ours.Triangles, other.Triangles), hi = Math.Max(ours.Triangles, other.Triangles);
                if (hi > lo * M3CountFactor) d.Add("triangle count " + ours.Triangles + " and " + other.Triangles + ", beyond the factor " + M3CountFactor);
                return d; // minimum angle, area bound and protected segments are judged per row by the oracle, not against each other
            }
            bool sameVertices = ours.ConstructedVertices == 0 && other.ConstructedVertices == 0;
            if (ours.Unique != other.Unique) d.Add("unique: " + ours.Unique + " and " + other.Unique);
            if (ours.Unique && other.Unique && sameVertices) {
                if (ours.TrianglesFp != other.TrianglesFp) d.Add("the triangulation is unique, but the triangles differ");
                if (ours.Subsegments > 0 && other.Subsegments > 0 && ours.SubsegmentsFp != other.SubsegmentsFp) d.Add("the triangulation is unique, but the subsegments differ");
            } else {
                if (ours.Triangles != other.Triangles) d.Add("triangle count " + ours.Triangles + " and " + other.Triangles);
            }
            if (sameVertices && ours.Area2 != other.Area2) d.Add("area " + ours.Area2 + " and " + other.Area2);
            return d;
        }
    }
}
