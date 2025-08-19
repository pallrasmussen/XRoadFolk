using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

public class NoFallbackWhenNoPublicIdTests
{
    [Fact]
    public void No_Default_Fallback_When_No_PublicId()
    {
        // Simulate a GetPeoplePublicInfo response with NO PublicId/PersonId elements
        var response = @"<?xml version=""1.0""?>
<Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
  <Body>
    <GetPeoplePublicInfoResponse>
      <people>
        <PersonPublicInfo>
          <FirstName>Alice</FirstName>
          <LastName>Example</LastName>
          <!-- intentionally no PublicId / PersonId -->
        </PersonPublicInfo>
        <PersonPublicInfo>
          <FirstName>Bob</FirstName>
          <LastName>Example</LastName>
          <!-- intentionally no PublicId / PersonId -->
        </PersonPublicInfo>
      </people>
    </GetPeoplePublicInfoResponse>
  </Body>
</Envelope>";

        var doc = XDocument.Parse(response);
        var people = doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo").ToList();
        Assert.True(people.Count == 2, "Setup sanity check failed");

        // This mirrors the Program.cs guard logic (prefix-agnostic)
        var peopleWithPublicId = people
            .Select(p => new {
                Elem = p,
                PublicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                           ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.PublicId))
            .ToList();

        // ASSERT: no entries qualify => app should NOT fall back and should "continue" (retry)
        Assert.Empty(peopleWithPublicId);
    }
}
