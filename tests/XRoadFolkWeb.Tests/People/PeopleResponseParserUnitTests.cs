using System;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests.People
{
    public class PeopleResponseParserUnitTests
    {
        private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);

        [Fact]
        public void Handles_Deeply_Nested_Xml_By_Depth_Cap()
        {
            int depth = 300;
            var open = new string('<', 0);
            var sb = new System.Text.StringBuilder();
            sb.Append("<root>");
            for (int i = 0; i < depth; i++)
            {
                sb.Append("<a>");
            }
            for (int i = 0; i < depth; i++)
            {
                sb.Append("</a>");
            }
            sb.Append("</root>");
            string xml = sb.ToString();
            var rows = Parser().ParsePeopleList(xml);
            var flat = Parser().FlattenResponse(xml);
            Assert.Empty(rows);
            Assert.Empty(flat);
        }

        [Fact]
        public void Respects_xsi_nil_Attributes_As_Empty()
        {
            string xml = @"<Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo><PublicId xsi:nil=""true"" /><Names><Name><Type>FirstName</Type><Order>1</Order><Value>Jane</Value></Name><Name><Type>LastName</Type><Value>Doe</Value></Name></Names></PersonPublicInfo></ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body></Envelope>";
            var rows = Parser().ParsePeopleList(xml);
            Assert.Single(rows);
            Assert.Null(rows[0].PublicId);
            Assert.Equal("Jane", rows[0].FirstName);
            Assert.Equal("Doe", rows[0].LastName);
        }

        [Fact]
        public void Multiple_FirstNames_Are_Sorted_By_Order_And_Joined()
        {
            string xml = @"<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo><Names>"+
                         @"<Name><Type>FirstName</Type><Order>2</Order><Value>Jane</Value></Name>"+
                         @"<Name><Type>FirstName</Type><Order>1</Order><Value>Mary</Value></Name>"+
                         @"<Name><Type>LastName</Type><Value>Doe</Value></Name>"+
                         @"</Names><SSN>123</SSN></PersonPublicInfo></ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body></Envelope>";
            var rows = Parser().ParsePeopleList(xml);
            Assert.Single(rows);
            Assert.Equal("Mary Jane", rows[0].FirstName);
            Assert.Equal("Doe", rows[0].LastName);
        }

        [Fact]
        public void Single_Vs_Multiple_Person_Results()
        {
            string single = @"<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo><PublicId>P1</PublicId><Names><Name><Type>FirstName</Type><Order>1</Order><Value>Jane</Value></Name><Name><Type>LastName</Type><Value>Doe</Value></Name></Names></PersonPublicInfo></ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body></Envelope>";
            var rows1 = Parser().ParsePeopleList(single);
            Assert.Single(rows1);

            string multi = @"<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo>"+
                           @"<PersonPublicInfo><PublicId>A</PublicId><Names><Name><Type>LastName</Type><Value>X</Value></Name></Names></PersonPublicInfo>"+
                           @"<PersonPublicInfo><PublicId>B</PublicId><Names><Name><Type>LastName</Type><Value>Y</Value></Name></Names></PersonPublicInfo>"+
                           @"</ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body></Envelope>";
            var rows2 = Parser().ParsePeopleList(multi);
            Assert.Equal(2, rows2.Count);
        }

        [Fact]
        public void Malformed_Xml_Returns_Empty()
        {
            string xml = "<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo>"; // incomplete
            Assert.Empty(Parser().ParsePeopleList(xml));
            Assert.Empty(Parser().FlattenResponse(xml));
        }
    }
}
