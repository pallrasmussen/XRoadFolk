using System;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests.People
{
    public class PeopleResponseParserSecurityTests
    {
        private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);

        [Fact]
        public void Prohibits_Dtd_And_Entity_Expansion_Returns_Empty()
        {
            string xml = """
            <!DOCTYPE lolz [
                <!ENTITY lol "lol">
                <!ENTITY lol1 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
                <!ENTITY lol2 "&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;">
            ]>
            <Envelope><Body><GetPeoplePublicInfoResponse>
              <ListOfPersonPublicInfo>
                <PersonPublicInfo>
                  <PublicId>&lol2;</PublicId>
                </PersonPublicInfo>
              </ListOfPersonPublicInfo>
            </GetPeoplePublicInfoResponse></Body></Envelope>
            """;

            var rows = Parser().ParsePeopleList(xml);
            var pairs = Parser().FlattenResponse(xml);
            string pretty = Parser().PrettyFormatXml(xml);

            Assert.Empty(rows);
            Assert.Empty(pairs);
            Assert.Equal(xml, pretty);
        }

        [Fact]
        public void MaxCharactersInDocument_Enforced_Returns_Empty()
        {
            // 11 MB of content to exceed 10 MB limit
            int size = 11 * 1024 * 1024;
            string chunk = new string('a', 1024);
            var sb = new StringBuilder(size + 1024);
            sb.Append("<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo><PublicId>");
            for (int i = 0; i < size / 1024; i++)
            {
                sb.Append(chunk);
            }
            sb.Append("</PublicId></PersonPublicInfo></ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body></Envelope>");
            string xml = sb.ToString();

            var rows = Parser().ParsePeopleList(xml);
            var pairs = Parser().FlattenResponse(xml);

            Assert.Empty(rows);
            Assert.Empty(pairs);
        }
    }
}
