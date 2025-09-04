using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Features.People;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class PeopleResponseParserTests
{
    [Fact]
    public void ParsePeopleList_Finds_Persons_Ignoring_Namespaces()
    {
        string xml = """
            <s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'>
              <s:Body>
                <x:GetPeoplePublicInfoResponse xmlns:x='urn:test'>
                  <x:ListOfPersonPublicInfo>
                    <x:PersonPublicInfo>
                      <x:PublicId>123</x:PublicId>
                      <x:Names>
                        <x:Name>
                          <x:Type>FirstName</x:Type>
                          <x:Order>1</x:Order>
                          <x:Value>Jane</x:Value>
                        </x:Name>
                        <x:Name>
                          <x:Type>LastName</x:Type>
                          <x:Value>Doe</x:Value>
                        </x:Name>
                      </x:Names>
                      <x:SSN>010203123</x:SSN>
                    </x:PersonPublicInfo>
                  </x:ListOfPersonPublicInfo>
                </x:GetPeoplePublicInfoResponse>
              </s:Body>
            </s:Envelope>
        """;
        var parser = new PeopleResponseParser(NullLogger<PeopleResponseParser>.Instance);
        var rows = parser.ParsePeopleList(xml);
        Assert.Single(rows);
        Assert.Equal("123", rows[0].PublicId);
        Assert.Equal("Jane", rows[0].FirstName);
        Assert.Equal("Doe", rows[0].LastName);
        Assert.Equal("010203123", rows[0].SSN);
    }

    [Fact]
    public void FlattenResponse_Produces_KeyValue_Pairs()
    {
        string xml = """
            <s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'>
              <s:Body>
                <x:GetPeoplePublicInfoResponse xmlns:x='urn:test'>
                  <X>
                    <A>1</A>
                    <B>2</B>
                  </X>
                </x:GetPeoplePublicInfoResponse>
              </s:Body>
            </s:Envelope>
        """;
        var parser = new PeopleResponseParser(NullLogger<PeopleResponseParser>.Instance);
        var pairs = parser.FlattenResponse(xml);
        Assert.Contains(pairs, p => p.Key.EndsWith("X.A") && p.Value == "1");
        Assert.Contains(pairs, p => p.Key.EndsWith("X.B") && p.Value == "2");
    }

    [Fact]
    public void FlattenResponse_Indexes_Repeated_Children()
    {
        string xml = """
            <s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'>
              <s:Body>
                <GetPersonResponse>
                  <Person>
                    <Names>
                      <Name><Type>FirstName</Type><Order>1</Order><Value>Jane</Value></Name>
                      <Name><Type>FirstName</Type><Order>2</Order><Value>Alice</Value></Name>
                      <Name><Type>LastName</Type><Value>Doe</Value></Name>
                    </Names>
                  </Person>
                </GetPersonResponse>
              </s:Body>
            </s:Envelope>
        """;
        var parser = new PeopleResponseParser(NullLogger<PeopleResponseParser>.Instance);
        var pairs = parser.FlattenResponse(xml);
        // Expect indexes on repeated Name elements
        Assert.Contains(pairs, p => p.Key.Contains("Names.Name[0].Value") && p.Value == "Jane");
        Assert.Contains(pairs, p => p.Key.Contains("Names.Name[1].Value") && p.Value == "Alice");
        Assert.Contains(pairs, p => p.Key.Contains("Names.Name[2].Value") && p.Value == "Doe");
    }
}
