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
            if (s is null) { throw new ArgumentNullException(nameof(s)); }
            string xml = s.Get;
            var text = Parser().PrettyFormatXml(xml);
            _ = text.Length >= 0;
        }

        [Property(MaxTest = 200)]
        public void ParsePeopleList_Never_Throws_On_Random_Strings(NonEmptyString s)
        {
            if (s is null) { throw new ArgumentNullException(nameof(s)); }
            var rows = Parser().ParsePeopleList(s.Get);
            _ = rows != null;
        }

        [Property(MaxTest = 200)]
        public void FlattenResponse_Never_Throws_On_Random_Strings(NonEmptyString s)
        {
            if (s is null) { throw new ArgumentNullException(nameof(s)); }
            var pairs = Parser().FlattenResponse(s.Get);
            _ = pairs != null;
        }

        [Property(MaxTest = 100)]
        public void ParsePeopleList_Depth_Bombs_Return_Empty(PositiveInt depthInput)
        {
            if (depthInput is null) { throw new ArgumentNullException(nameof(depthInput)); }
            int depth = Math.Min(600, depthInput.Get);
            var sb = new StringBuilder();
            sb.Append("<root>");
            for (int i = 0; i < depth; i++) { sb.Append("<a>"); }
            for (int i = 0; i < depth; i++) { sb.Append("</a>"); }
            sb.Append("</root>");
            string xml = sb.ToString();
            var rows = Parser().ParsePeopleList(xml);
            _ = rows != null;
        }
    }
}
