using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class PeopleResponseParserE2eTests
{
    [Fact]
    public void ParsePeopleList_And_Flatten_EndToEnd()
    {
        // Arrange: SOAP with two persons and mixed casing/namespaces
        string xml = """
        <s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'>
          <s:Body>
            <t:GetPeoplePublicInfoResponse xmlns:t='urn:test'>
              <t:ListOfPersonPublicInfo>
                <t:PersonPublicInfo>
                  <t:PublicId>AAA111</t:PublicId>
                  <t:Names>
                    <t:Name><t:Type>FirstName</t:Type><t:Order>2</t:Order><t:Value>Jane</t:Value></t:Name>
                    <t:Name><t:Type>FirstName</t:Type><t:Order>1</t:Order><t:Value>Mary</t:Value></t:Name>
                    <t:Name><t:Type>LastName</t:Type><t:Value>Doe</t:Value></t:Name>
                  </t:Names>
                  <t:CivilStatusDate>1990-01-31T00:00:00Z</t:CivilStatusDate>
                  <t:SSN>010203123</t:SSN>
                </t:PersonPublicInfo>
                <t:PersonPublicInfo>
                  <t:PersonId>BBB222</t:PersonId>
                  <t:Names>
                    <t:Name><t:Type>FirstName</t:Type><t:Order>1</t:Order><t:Value>John</t:Value></t:Name>
                    <t:Name><t:Type>LastName</t:Type><t:Value>Smith</t:Value></t:Name>
                  </t:Names>
                </t:PersonPublicInfo>
              </t:ListOfPersonPublicInfo>
              <t:ListOfPersonPublicInfoCriteria>
                <t:PersonPublicInfoCriteria>
                  <t:SSN>010203123</t:SSN>
                </t:PersonPublicInfoCriteria>
              </t:ListOfPersonPublicInfoCriteria>
            </t:GetPeoplePublicInfoResponse>
          </s:Body>
        </s:Envelope>
        """;

        var parser = new PeopleResponseParser(NullLogger<PeopleResponseParser>.Instance);

        // Act
        var rows = parser.ParsePeopleList(xml);
        var flat = parser.FlattenResponse(xml);

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("AAA111", rows[0].PublicId);
        Assert.Equal("Mary Jane", rows[0].FirstName); // sorted by Order
        Assert.Equal("Doe", rows[0].LastName);
        Assert.Equal("010203123", rows[0].SSN);

        Assert.Equal("BBB222", rows[1].PublicId);
        Assert.Equal("John", rows[1].FirstName);
        Assert.Equal("Smith", rows[1].LastName);

        Assert.Contains(flat, p => p.Key.EndsWith("ListOfPersonPublicInfo[0].SSN") && p.Value == "010203123");
        Assert.Contains(flat, p => p.Key.EndsWith("ListOfPersonPublicInfo[1].Names.Name[1].Value") && p.Value == "Smith");
    }
}
