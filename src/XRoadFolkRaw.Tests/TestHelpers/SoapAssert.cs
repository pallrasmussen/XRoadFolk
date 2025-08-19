using System;
using System.Linq;
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
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var els = doc.Descendants().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(els.Any(), $"Expected element with local-name '{localName}' to exist.");
            if (expectedValue != null)
            {
                Assert.Contains(els, e => (e.Value ?? string.Empty).Contains(expectedValue, StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// Quick raw substring check that is robust to prefixes by checking only the local-name part.
        /// e.g., LooksLikeLocalName(xml, "serviceCode", "GetPeoplePublicInfo")
        /// </summary>
        public static void LooksLikeLocalName(string xml, string localName, string? contains = null)
        {
            var needle = localName + ">";
            Assert.Contains(needle, xml);
            if (contains != null)
                Assert.Contains(contains, xml);
        }
    }
}
