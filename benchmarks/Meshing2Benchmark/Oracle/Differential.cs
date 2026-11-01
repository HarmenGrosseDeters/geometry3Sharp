using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshing2Acceptance
{
    public enum DiffOutcome
    {
        /// <summary>Unique triangulation, the same triangle set.</summary>
        SameTriangles,
        /// <summary>Cocircular ties: both valid by the oracle, the choice among ties may differ.</summary>
        BothValid,
        /// <summary>The candidate passes the oracle, the reference fails it: the reference is at fault here.</summary>
        ReferenceInvalid,
        /// <summary>The reference threw on this input; nothing to compare with.</summary>
        ReferenceThrew,
        /// <summary>The candidate fails the oracle.</summary>
        CandidateInvalid,
        /// <summary>Both pass the oracle and differ although the result must be unique: one of the two has a bug in the oracle's blind spot.</summary>
        Differ
    }

    public sealed class DiffResult
    {
        public string Case; public DiffOutcome Outcome; public string Detail;
        public bool IsFailure => Outcome == DiffOutcome.CandidateInvalid || Outcome == DiffOutcome.Differ;
        public override string ToString() => Case + ": " + Outcome + (Detail == null ? "" : ": " + Detail);
    }

    /// <summary>The M1 differential (ACCEPTANCE_STRATEGY section 3). General position: the same triangle set. Cocircular ties: both valid.</summary>
    public static class Differential
    {
        public static DiffResult Compare(Mesh2Case c, IMeshing2Candidate candidate, IMeshing2Candidate reference)
        {
            var ours = candidate.Run(c.Input);
            var ourReport = Oracle.Check(c.Input, ours, c.Expect);
            if (!ourReport.Ok) return new DiffResult { Case = c.Id, Outcome = DiffOutcome.CandidateInvalid, Detail = ourReport.ToString() };
            Mesh2Output theirs;
            try { theirs = reference.Run(c.Input); }
            catch (Exception e) { return new DiffResult { Case = c.Id, Outcome = DiffOutcome.ReferenceThrew, Detail = e.GetType().Name + ": " + e.Message }; }
            var theirReport = Oracle.Check(c.Input, theirs, c.Expect);
            if (!theirReport.Ok) return new DiffResult { Case = c.Id, Outcome = DiffOutcome.ReferenceInvalid, Detail = theirReport.ToString() };
            if (ourReport.Unique != theirReport.Unique)
                return new DiffResult { Case = c.Id, Outcome = DiffOutcome.Differ, Detail = "the oracle calls one unique, the other tied" };
            if (!ourReport.Unique) {
                if (ours.Triangles.Length != theirs.Triangles.Length) return new DiffResult { Case = c.Id, Outcome = DiffOutcome.Differ, Detail = "triangle counts " + ours.Triangles.Length + " and " + theirs.Triangles.Length };
                return new DiffResult { Case = c.Id, Outcome = DiffOutcome.BothValid };
            }
            if (Fingerprint.Triangles(ours) != Fingerprint.Triangles(theirs)) return new DiffResult { Case = c.Id, Outcome = DiffOutcome.Differ, Detail = "different triangle sets" };
            return new DiffResult { Case = c.Id, Outcome = DiffOutcome.SameTriangles };
        }
    }
}
