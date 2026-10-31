using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>
    /// What exists to be judged. This one file is what session 2 changes when a milestone arrives: register the candidate, list its
    /// features. The catalogue test then fails until every row waiting for those features has been moved to "covered".
    /// </summary>
    public static class CandidateRegistry
    {
        public static IMeshing2Candidate For(Milestone milestone)
        {
            switch (milestone) {
                case Milestone.M1: return new Prototype200Candidate();
                default: return null;
            }
        }

        public static readonly HashSet<string> Features = new HashSet<string> { Feature.Kernel };

        /// <summary>
        /// Public library types that mean a milestone has arrived. If one appears while its feature is not registered, a test fails:
        /// the gates would otherwise keep judging the prototype and nobody would notice.
        /// </summary>
        public static readonly Dictionary<string, string> ProductionTypes = new Dictionary<string, string> {
            { "g3.DelaunayMesh2", Feature.Api },
            { "g3.ConstrainedDelaunayMesh2", Feature.Cdt },
            { "g3.QualityMesh2", Feature.Refine },
        };
    }
}
