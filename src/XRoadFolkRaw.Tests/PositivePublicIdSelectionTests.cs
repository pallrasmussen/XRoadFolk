using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

public class PositivePublicIdSelectionTests
{
    [Fact]
    public void Picks_PublicId_When_Present()
    {
        // Simulated GetPeoplePublicInfo response with ONE PublicId present
        var response = @"<?xml version=""1.0""?>
<Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
  <Body>
    <GetPeoplePublicInfoResponse>
      <people>
        <PersonPublicInfo>
          <FirstName>Alice</FirstName>
          <LastName>Example</LastName>
          <PublicId>PUB-123</PublicId>
        </PersonPublicInfo>
        <PersonPublicInfo>
          <FirstName>Bob</FirstName>
          <LastName>Example</LastName>
          <!-- deliberately no PublicId/PersonId -->
        </PersonPublicInfo>
      </people>
    </GetPeoplePublicInfoResponse>
  </Body>
</Envelope>";

        var doc = XDocument.Parse(response);

        var people = doc
            .Descendants()
            .Where(e => e.Name.LocalName.Equals("PersonPublicInfo", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(people.Count == 2, "Setup sanity check failed.");

        // This mirrors the Program.cs filter (accept PublicId, or PersonId as alt; then require non-empty)
        var peopleWithPublicId = people
            .Select(p => new
            {
                Elem = p,
                PublicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                        ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.PublicId))
            .ToList();

        // Expect exactly one match and the exact PublicId value
        Assert.Single(peopleWithPublicId);
        Assert.Equal("PUB-123", peopleWithPublicId[0].PublicId);
    }
}
