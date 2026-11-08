using System.Collections.Generic;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>The covered, non-slow M3 catalogue in constrained-quality mode.
    /// Explicitly conforming cases retain their separate O2 contract and are excluded.
    /// Fresh input/expectation objects prevent mode changes leaking into the default suite.</summary>
    public static class ConstrainedGoldenCases
    {
        public static List<Mesh2Case> Ci() => Catalogue.Rows()
            .Where(r => r.Case != null && r.Case.Milestone == Milestone.M3 &&
                        r.State == CaseState.Covered && !r.Slow && !r.Case.Input.Conforming)
            .Select(r => {
                var original = r.Case;
                var input = TextFormat.ReadInput(TextFormat.WriteInput(original.Input));
                input.ConstrainedQuality = true;
                // WithGlobalDelaunayLimit with the unchanged limit serves as a clone, so that AngleGuaranteed below does not
                // change the M3 case. The copy is shallow (MemberwiseClone): Removed and Kept are shared with the original,
                // which is harmless because neither list is modified here.
                var expect = original.Expect.WithGlobalDelaunayLimit(original.Expect.GlobalDelaunayLimit);
                // #394 already validates these small-angle rows with quality enabled.
                // The golden gate must not silently skip their angle checks.
                if (original.Id.StartsWith("f-38/", System.StringComparison.Ordinal)) expect.AngleGuaranteed = true;
                return new Mesh2Case {
                    Id = original.Id, Generator = original.Generator + " mode(constrained)",
                    Milestone = Milestone.M3, Input = input, Expect = expect, Ties = original.Ties
                };
            }).ToList();
    }
}
