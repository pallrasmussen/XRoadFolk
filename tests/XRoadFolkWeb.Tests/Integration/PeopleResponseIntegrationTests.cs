using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests.Integration
{
    public class PeopleResponseIntegrationTests
    {
        private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);

        [Theory]
        [InlineData("Sample-GetPeoplePublicInfo-1.xml")]
        [InlineData("Sample-GetPeoplePublicInfo-2.xml")]
        [InlineData("Sample-GetPerson-1.xml")]
        public void Real_Response_Samples_Parse_Without_Errors(string fileName)
        {
            string baseDir = Path.Combine(AppContext.BaseDirectory, "TestData");
            string path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path))
            {
                // Skip if samples not present in CI
                return;
            }

            string xml = File.ReadAllText(path);
            var rows = Parser().ParsePeopleList(xml);
            var pairs = Parser().FlattenResponse(xml);

            Assert.NotNull(rows);
            Assert.NotNull(pairs);
        }
    }
}
