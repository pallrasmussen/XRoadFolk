using System.Xml.Linq;
using Xunit;

namespace XRoadFolkRaw.Tests.Helpers
{
    public static class SoapAssert
    {
        /// <summary>
        /// Asserts that an element with the given local name exists anywhere in the XML,
        /// and optionally matches the expected inner text. Prefix-agnostic.
        /// </summary>
        public static void HasElement(string xml, string localName, string? expectedValue = null)
        {
            XDocument doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            System.Collections.Generic.List<XElement> els = [.. System.Linq.Enumerable.Where(doc.Descendants(), e => e.Name.LocalName.Equals(localName, System.StringComparison.OrdinalIgnoreCase))];
            Assert.True(els.Count != 0, $"Expected element with local-name '{localName}' to exist.");
            if (expectedValue != null)
            {
                Assert.Contains(els, e => (e.Value ?? string.Empty).Contains(expectedValue, System.StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// Quick raw substring check that is robust to prefixes by checking only the local-name part.
        /// e.g., LooksLikeLocalName(xml, "serviceCode", "GetPeoplePublicInfo")
        /// </summary>
        public static void LooksLikeLocalName(string xml, string localName, string? contains = null)
        {
            string needle = localName + ">";
            Assert.Contains(needle, xml);
            if (contains != null)
            {
                Assert.Contains(contains, xml);
            }
        }
    }
}
