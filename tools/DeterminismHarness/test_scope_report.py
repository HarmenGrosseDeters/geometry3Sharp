# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
# python3 -m unittest discover -s tools/DeterminismHarness -p 'test_scope_report.py'
import unittest

import scope_report as sr

P = "/w/mesh_ops/ExactMeshBoolean.cs"
LOG = [
    P + "(10,5): error G3D0001: Proof scope: 'M:System.Double.IsNaN(System.Double)' is not on the allow list DeterministicApis.txt (add it) [/w/g.csproj]",
    P + "(11,5): error G3D0001: Proof scope: 'M:System.Double.IsNaN(System.Double)' is not on the allow list DeterministicApis.txt (add it) [/w/g.csproj]",
    # the same diagnostic again, as in msbuild's summary
    P + "(11,5): error G3D0001: Proof scope: 'M:System.Double.IsNaN(System.Double)' is not on the allow list DeterministicApis.txt (add it) [/w/g.csproj]",
    P + "(12,5): error G3D0001: Proof scope: 'M:g3.DMesh3.GetVertex(System.Int32)' is not on the allow list DeterministicApis.txt (add it) [/w/g.csproj]",
    P + "(13,5): error G3D0001: Proof scope: 'P:System.Collections.Generic.KeyValuePair`2.Key' is not on the allow list DeterministicApis.txt [/w/g.csproj]",
    P + "(14,5): error G3D0001: Proof scope: 'construct:interpolated-string' is not on the allow list DeterministicApis.txt [/w/g.csproj]",
    P + "(15,5): error G3D0001: Proof scope: 'construct:delegate-invocation (2 in 'M:g3.X.a(System.Int32)', registered: 0)' is not on the allow list DeterministicApis.txt [/w/g.csproj]",
    P + "(16,5): error G3D0001: Proof scope: 'construct:delegate-invocation (2 in 'M:g3.X.a(System.Int32)', registered: 0)' is not on the allow list DeterministicApis.txt [/w/g.csproj]",
    P + "(20,5): error G3D0002: Proof scope: 'M:System.Math.Max(System.Double,System.Double)' is a restricted entry: 'M:g3.X.a' needs a 'site' line with the justification [/w/g.csproj]",
    P + "(21,5): error G3D0002: Proof scope: 'M:System.Math.Max(System.Double,System.Double)' is a restricted entry: 'M:g3.X.a' needs a 'site' line with the justification [/w/g.csproj]",
    P + "(22,5): error G3D0002: Proof scope: 'M:System.Math.Max(System.Double,System.Double)' is a restricted entry: 'M:g3.X.b' needs a 'site' line with the justification [/w/g.csproj]",
    P + "(23,5): error G3D0002: Proof scope: 'M:System.Object.GetHashCode' is a hash value (seeded per process on .NET for some types): only inside a GetHashCode implementation [/w/g.csproj]",
    "/w/mesh_ops/ExactMeshArrangement.cs(30,5): error G3D0006: Proof scope: 2 conversion(s) of a double to an integer type in 'M:g3.Y.c', registered: 0 (a 'cast' line) [/w/g.csproj]",
    "/w/comp_geom/SnapRounding2.cs(1,1): error G3D0009: Proof scope: DeterministicApis.txt line 146: 'M:g3.Z.d' is not used by the scope [/w/g.csproj]",
    "/w/x.cs(1,1): warning CS0168: something else [/w/g.csproj]",
    "Build FAILED.",
]


class ScopeReportTests(unittest.TestCase):
    def test_owner(self):
        self.assertEqual("bcl:System.Double", sr.owner("M:System.Double.IsNaN(System.Double)"))
        self.assertEqual("bcl:System.Collections.Generic.KeyValuePair`2", sr.owner("P:System.Collections.Generic.KeyValuePair`2.Key"))
        self.assertEqual("g3:DMesh3", sr.owner("M:g3.DMesh3.GetVertex(System.Int32)"))
        self.assertEqual("construct", sr.owner("construct:interpolated-string"))
        self.assertEqual("bcl:System.Collections.Generic.Dictionary`2.Enumerator", sr.owner("M:System.Collections.Generic.Dictionary`2.Enumerator.MoveNext"))
        self.assertEqual("bcl:System.Array", sr.owner("M:System.Array.Sort``1(``0[])"))
        self.assertEqual("bcl:System.Random", sr.owner("T:System.Random"))

    def test_parse_counts_unique_diagnostics_members_and_sites(self):
        r = sr.parse(LOG)
        self.assertEqual(13, r["diagnostics"])      # (the repeated line once; the warning and the text line not at all)
        self.assertEqual({"G3D0001": 7, "G3D0002": 4, "G3D0006": 1, "G3D0009": 1}, dict(r["per_rule"]))
        # (the unused allow-list line is reported at the scope's first file: not a finding of that file)
        self.assertEqual({"ExactMeshBoolean.cs": 11, "ExactMeshArrangement.cs": 1}, dict(r["per_file"]))
        self.assertEqual(4, len(r["members"]))
        self.assertEqual(2, r["members"]["M:System.Double.IsNaN(System.Double)"])
        # two uses in one member are one site
        self.assertEqual({"M:g3.X.a", "M:g3.X.b"}, r["sites"]["M:System.Math.Max(System.Double,System.Double)"])
        self.assertEqual(1, r["hash_sites"])
        self.assertEqual({"M:g3.Y.c": 2}, r["casts"])
        self.assertEqual({146}, r["unused"])
        self.assertEqual([], r["other"])
        # a counted construct is one registration per member, whatever the number of its occurrences
        self.assertEqual({("delegate-invocation", "M:g3.X.a(System.Int32)"): 2}, r["constructs"])

    def test_render(self):
        text = sr.render(sr.parse(LOG), members=True)
        self.assertIn("13 diagnostics: G3D0001 7, G3D0002 4, G3D0006 1, G3D0009 1", text)
        self.assertIn("unlisted external members (G3D0001): 4 (5 uses): bcl 2, g3 1, construct 1", text)
        self.assertIn("restricted entries without a site line (G3D0002): 1 entries, 2 member sites; GetHashCode outside a hash implementation: 1", text)
        self.assertIn("counted constructs without a registration (G3D0001): delegate-invocation 2 in 1 members", text)
        self.assertIn("double to integer conversions (G3D0006): 2 in 1 members", text)
        self.assertIn("allow-list lines no longer used (G3D0009): 1", text)
        self.assertIn("   1  M:g3.DMesh3.GetVertex(System.Int32)", text)


if __name__ == "__main__":
    unittest.main()
