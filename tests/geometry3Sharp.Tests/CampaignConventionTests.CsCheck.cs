using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The convention of the property campaigns (docs/QA.md 2.4): a test class that carries [Trait("Category", "Campaign")]
    /// is not run by the Stryker shards. Their test-case-filter terms are name parts (FullyQualifiedName~Part), so the
    /// convention is that no part occurs in the full name of a campaign class; this test fails when one does.
    /// </summary>
    public class CampaignConventionTests
    {
        static string Root()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && File.Exists(Path.Combine(dir, "CLAUDE.md")) == false)
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir;
        }

        static Type[] CampaignClasses() =>
            typeof(CampaignConventionTests).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributesData().Any(d => d.AttributeType == typeof(TraitAttribute) && d.ConstructorArguments.Count == 2
                    && (string)d.ConstructorArguments[0].Value == "Category" && (string)d.ConstructorArguments[1].Value == "Campaign")).ToArray();

        [Fact]
        public void CampaignClasses_Exist() => Assert.NotEmpty(CampaignClasses());

        [Fact]
        public void StrykerFilters_DoNotSelectCampaignClasses()
        {
            string[] files = Directory.GetFiles(Path.Combine(Root(), "tests", "mutation"), "stryker-*.json");
            Assert.NotEmpty(files);
            foreach (string file in files) {
                string text = File.ReadAllText(file);
                Match m = Regex.Match(text, "\"test-case-filter\"\\s*:\\s*\"([^\"]*)\"");
                if (m.Success == false) continue;
                foreach (string term in m.Groups[1].Value.Split('|', '&')) {
                    if (term.StartsWith("FullyQualifiedName~") == false) continue;
                    string part = term.Substring("FullyQualifiedName~".Length);
                    foreach (Type t in CampaignClasses())
                        Assert.False(t.FullName.Contains(part), $"{Path.GetFileName(file)}: '{part}' selects the campaign class {t.FullName}");
                }
            }
        }

        [Fact]
        public void CampaignClasses_AreNotInTheFastTierOfTheFilters_ByName()
        {
            // the class names carry "Campaign", the word the filters never use
            foreach (Type t in CampaignClasses())
                Assert.Contains("Campaign", t.Name);
        }
    }
}
