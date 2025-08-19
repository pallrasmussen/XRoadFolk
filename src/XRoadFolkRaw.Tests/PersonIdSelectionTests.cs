using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

public class PersonIdSelectionTests
{
    [Fact]
    public void Accepts_PersonId_When_PublicId_Missing()
    {
        // Simulated response where only PersonId is present
        var response = @"<?xml version=""1.0""?>
<Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
  <Body>
    <GetPeoplePublicInfoResponse>
      <people>
        <PersonPublicInfo>
          <FirstName>Alice</FirstName>
          <LastName>Example</LastName>
          <!-- no PublicId or PersonId -->
        </PersonPublicInfo>
        <PersonPublicInfo>
          <FirstName>Charlie</FirstName>
          <LastName>Example</LastName>
          <PersonId>PID-789</PersonId>
        </PersonPublicInfo>
      </people>
    </GetPeoplePublicInfoResponse>
  </Body>
</Envelope>";

        var doc = XDocument.Parse(response);
        var people = doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo").ToList();
        Assert.True(people.Count == 2, "Setup sanity check failed.");

        // Mirror Program.cs filter: prefer PublicId, otherwise PersonId; require non-empty
        var peopleWithPublicId = people
            .Select(p => new
            {
                Elem = p,
                PublicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                        ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.PublicId))
            .ToList();

        Assert.Single(peopleWithPublicId);
        Assert.Equal("PID-789", peopleWithPublicId[0].PublicId);
    }
}
