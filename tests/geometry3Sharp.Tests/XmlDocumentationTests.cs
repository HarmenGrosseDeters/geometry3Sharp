using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The XML documentation file ships in the package (IDE tooltips). A summary with a bare '&lt;' (eg "a &lt;= b") is
    /// badly formed XML: the compiler warns CS1570 and drops the whole comment of that member from the file, with a
    /// "Badly formed XML comment ignored" marker instead (found by the browser demos, docs/research/DOGFOODING_DEMOS_2026-11.md).
    /// </summary>
    public class XmlDocumentationTests
    {
        /// <summary>
        /// the XML file next to the library: on .NET Framework the test runner can shadow-copy the assembly (Location is then a
        /// cache folder without the XML), so the original location (CodeBase) and the test output folder are tried too
        /// </summary>
        static string FindXml()
        {
            var candidates = new System.Collections.Generic.List<string>();
            var asm = typeof(DMesh3).Assembly;
#if NETFRAMEWORK
            try { candidates.Add(Path.ChangeExtension(new System.Uri(asm.CodeBase).LocalPath, ".xml")); } catch (System.Exception) { }
#endif
            if (!string.IsNullOrEmpty(asm.Location))
                candidates.Add(Path.ChangeExtension(asm.Location, ".xml"));
            candidates.Add(Path.Combine(System.AppContext.BaseDirectory, "geometry3Sharp.xml"));
            return candidates.FirstOrDefault(File.Exists);
        }

        [Fact]
        public void ShippedXmlDocumentation_HasNoBadlyFormedComments()
        {
            string xml = FindXml();
            Assert.True(xml != null, "the XML documentation file (geometry3Sharp.xml) is not next to the library or in the test output folder");
            string text = File.ReadAllText(xml);
            var members = Regex.Matches(text, "Badly formed XML comment ignored for member \"([^\"]*)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.True(members.Count == 0, "badly formed XML comments (CS1570): " + string.Join(", ", members));
        }
    }
}
