using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class PeopleResponseParserEdgeTests
{
    private static PeopleResponseParser Parser() => new(NullLogger<PeopleResponseParser>.Instance);

    [Fact]
    public void ParsePeopleList_Returns_Empty_On_Malformed_Xml()
    {
        string xml = "<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo></ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body>"; // broken
        var rows = Parser().ParsePeopleList(xml);
        Assert.Empty(rows);
    }

    [Fact]
    public void FlattenResponse_Returns_Empty_On_Malformed_Xml()
    {
        string xml = "<Envelope><Body><GetPeoplePublicInfoResponse><X><A>1</A></X></GetPeoplePublicInfoResponse></Body>"; // broken
        var pairs = Parser().FlattenResponse(xml);
        Assert.Empty(pairs);
    }

    [Fact]
    public void PrettyFormatXml_Returns_Input_On_Malformed_Xml()
    {
        string xml = "<<<not-xml>>>";
        string pretty = Parser().PrettyFormatXml(xml);
        Assert.Equal(xml, pretty);
    }

    [Fact]
    public void ParsePeopleList_Uses_Request_Ssn_When_Single_Result_Missing_Ssn()
    {
        string xml = """
        <s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'>
          <s:Body>
            <GetPeoplePublicInfoResponse>
              <ListOfPersonPublicInfo>
                <PersonPublicInfo>
                  <PublicId>PID1</PublicId>
                  <Names>
                    <Name><Type>FirstName</Type><Order>1</Order><Value>Jane</Value></Name>
                    <Name><Type>LastName</Type><Value>Doe</Value></Name>
                  </Names>
                  <!-- SSN intentionally missing in result -->
                </PersonPublicInfo>
              </ListOfPersonPublicInfo>
              <ListOfPersonPublicInfoCriteria>
                <PersonPublicInfoCriteria>
                  <SSN>010203123</SSN>
                </PersonPublicInfoCriteria>
              </ListOfPersonPublicInfoCriteria>
            </GetPeoplePublicInfoResponse>
          </s:Body>
        </s:Envelope>
        """;

        var rows = Parser().ParsePeopleList(xml);
        Assert.Single(rows);
        Assert.Equal("010203123", rows[0].SSN);
        Assert.Equal("PID1", rows[0].PublicId);
        Assert.Equal("Jane", rows[0].FirstName);
        Assert.Equal("Doe", rows[0].LastName);
    }

    [Fact]
    public void ParsePeopleList_Skips_Completely_Empty_Person()
    {
        string xml = """
        <Envelope>
          <Body>
            <GetPeoplePublicInfoResponse>
              <ListOfPersonPublicInfo>
                <PersonPublicInfo>
                  <!-- no id, no ssn, no names -->
                </PersonPublicInfo>
                <PersonPublicInfo>
                  <Names>
                    <Name><Type>FirstName</Type><Order>1</Order><Value>John</Value></Name>
                    <Name><Type>LastName</Type><Value>Smith</Value></Name>
                  </Names>
                </PersonPublicInfo>
              </ListOfPersonPublicInfo>
            </GetPeoplePublicInfoResponse>
          </Body>
        </Envelope>
        """;

        var rows = Parser().ParsePeopleList(xml);
        Assert.Single(rows);
        Assert.Null(rows[0].PublicId);
        Assert.Null(rows[0].SSN);
        Assert.Equal("John", rows[0].FirstName);
        Assert.Equal("Smith", rows[0].LastName);
    }

    [Fact]
    public void ParsePeopleList_Returns_Empty_On_Depth_Limit_Exceeded()
    {
        // Build an XML with depth greater than PeopleResponseParser.MaxElementDepth (128)
        int depth = 140;
        var sb = new System.Text.StringBuilder();
        sb.Append("<a>");
        for (int i = 0; i < depth; i++) sb.Append("<a>");
        for (int i = 0; i < depth; i++) sb.Append("</a>");
        sb.Append("</a>");
        string xml = sb.ToString();

        var rows = Parser().ParsePeopleList(xml);
        Assert.Empty(rows);

        var pairs = Parser().FlattenResponse(xml);
        Assert.Empty(pairs);

        // PrettyFormat should return the original on failure
        string pretty = Parser().PrettyFormatXml(xml);
        Assert.Equal(xml, pretty);
    }

    [Fact]
    public void FlattenResponse_Returns_Empty_When_No_Body_Or_Response()
    {
        string xml = "<Envelope><Foo/></Envelope>";
        var pairs = Parser().FlattenResponse(xml);
        Assert.Empty(pairs);
    }

    [Fact]
    public void Dtd_Is_Prohibited_And_Returns_Empty()
    {
        string xml = """
        <!DOCTYPE foo [ <!ELEMENT foo ANY > <!ENTITY xxe SYSTEM "file:///etc/passwd" > ]>
        <foo>&xxe;</foo>
        """;
        var rows = Parser().ParsePeopleList(xml);
        Assert.Empty(rows);
        var pairs = Parser().FlattenResponse(xml);
        Assert.Empty(pairs);
    }
}
