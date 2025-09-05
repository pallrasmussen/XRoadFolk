using System;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests.People
{
    public class PeopleResponseParserFuzzTests
    {
        private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);
        private static readonly Random Rng = new(1234);

        [Fact]
        public void Fuzz_Malformed_Payloads_Do_Not_Throw()
        {
            for (int i = 0; i < 300; i++)
            {
                string payload = MakeGarbage(i);
                var rows = Parser().ParsePeopleList(payload);
                var pairs = Parser().FlattenResponse(payload);
                _ = Parser().PrettyFormatXml(payload);
                Assert.NotNull(rows);
                Assert.NotNull(pairs);
            }
        }

        private static string MakeGarbage(int i)
        {
            int len = 1 + (i % 512);
            var sb = new StringBuilder(len);
            for (int j = 0; j < len; j++)
            {
                int k = Rng.Next(0, 5);
                char c = k switch
                {
                    0 => '<',
                    1 => '>',
                    2 => (char)Rng.Next(32, 127),
                    3 => '\\',
                    _ => '"',
                };
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
