using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

namespace XRoadFolkRaw.Lib.Tests;

public class SoapSanitizerTests
{
    [Fact]
    public void Scrub_Masks_User_Pass_Token()
    {
        string xml = """
        <Envelope>
          <Body>
            <login>
              <username>user-secret-12345</username>
              <password>p@ss-Secret-98765</password>
              <token>ABCDEF-0123456789-SECRET-TOKEN-TEXT</token>
            </login>
          </Body>
        </Envelope>
        """;

        string sanitized = SoapSanitizer.Scrub(xml, maskTokens: true);

        Assert.DoesNotContain("user-secret-12345", sanitized);
        Assert.DoesNotContain("p@ss-Secret-98765", sanitized);
        Assert.DoesNotContain("ABCDEF-0123456789-SECRET-TOKEN-TEXT", sanitized);
        Assert.Contains("<username>", sanitized);
        Assert.Contains("<password>", sanitized);
        Assert.Contains("<token>", sanitized);
        Assert.Contains("*", sanitized); // masked content uses asterisks
    }
}
