using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests.People
{
    public class PeopleResponseParserRoundtripTests
    {
        private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);

        [Fact]
        public void Flatten_And_Rehydrate_Keeps_Leaf_Values()
        {
            string xml = @"<Envelope><Body><GetPeoplePublicInfoResponse><Item><A>1</A><B>2</B><C><D>3</D><D>4</D></C></Item></GetPeoplePublicInfoResponse></Body></Envelope>";
            var pairs = Parser().FlattenResponse(xml);

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in pairs)
            {
                dict[k] = v;
            }

            Assert.Equal("1", dict["Item.A"]);
            Assert.Equal("2", dict["Item.B"]);
            Assert.Equal("3", dict["Item.C.D[0]"]);
            Assert.Equal("4", dict["Item.C.D[1]"]);
        }
    }
}
