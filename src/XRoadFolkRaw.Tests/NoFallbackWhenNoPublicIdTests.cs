using System.Xml.Linq;
using Xunit;

public class NoFallbackWhenNoPublicIdTests
{
    [Fact]
    public void NoDefaultFallbackWhenNoPublicId()
    {
        // Simulate a GetPeoplePublicInfo response with NO PublicId/PersonId elements
        string response = @"<?xml version=""1.0""?>
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

        XDocument doc = XDocument.Parse(response);
        System.Collections.Generic.List<XElement> people = [.. doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];
        Assert.True(people.Count == 2, "Setup sanity check failed");

        // This mirrors the Program.cs guard logic (prefix-agnostic)
        var peopleWithPublicId = people
            .Select(p => new
            {
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
