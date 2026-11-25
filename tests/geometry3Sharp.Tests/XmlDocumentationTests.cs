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
        [Fact]
        public void ShippedXmlDocumentation_HasNoBadlyFormedComments()
        {
            string dll = typeof(DMesh3).Assembly.Location;
            string xml = Path.ChangeExtension(dll, ".xml");
            Assert.True(File.Exists(xml), "the XML documentation file is not next to the library: " + xml);
            string text = File.ReadAllText(xml);
            var members = Regex.Matches(text, "Badly formed XML comment ignored for member \"([^\"]*)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.True(members.Count == 0, "badly formed XML comments (CS1570): " + string.Join(", ", members));
        }
    }
}
