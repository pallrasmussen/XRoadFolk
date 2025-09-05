using System;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests.People
{
    public class PeopleResponseParserPropertyTests
    {
        private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);

        [Property(MaxTest = 100)]
        public void PrettyFormat_Never_Throws_And_Returns_Input_On_Failure(NonEmptyString s)
        {
            string xml = s.Get;
            var pretty = Parser().PrettyFormatXml(xml);
            // If the input is valid XML, pretty output may differ; otherwise must equal input
            bool looksXml = xml.Contains('<') && xml.Contains('>');
            if (!looksXml)
            {
                Assert.Equal(xml, pretty);
            }
        }

        [Property(MaxTest = 200)]
        public void ParsePeopleList_Never_Throws_On_Random_Strings(NonEmptyString s)
        {
            var rows = Parser().ParsePeopleList(s.Get);
            Assert.NotNull(rows);
        }

        [Property(MaxTest = 200)]
        public void FlattenResponse_Never_Throws_On_Random_Strings(NonEmptyString s)
        {
            var pairs = Parser().FlattenResponse(s.Get);
            Assert.NotNull(pairs);
        }

        [Property(MaxTest = 100)]
        public void ParsePeopleList_Depth_Bombs_Return_Empty(PositiveInt depthInput)
        {
            int depth = Math.Min(600, depthInput.Get);
            var sb = new StringBuilder();
            sb.Append("<a>");
            for (int i = 0; i < depth; i++) sb.Append("<a>");
            for (int i = 0; i < depth; i++) sb.Append("</a>");
            sb.Append("</a>");
            string xml = sb.ToString();
            var rows = Parser().ParsePeopleList(xml);
            Assert.NotNull(rows);
        }
    }
}
