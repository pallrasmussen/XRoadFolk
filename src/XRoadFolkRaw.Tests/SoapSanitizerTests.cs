using Xunit;
using XRoadFolkRaw.Lib;

public class SoapSanitizerTests
{
    [Fact]
    public void MasksUsernamePasswordAndTokenByDefault()
    {
        string xml = @"<Envelope>
  <Body>
    <login>
      <username>alice</username>
      <password>SuperSecret!</password>
      <token>ABCDEF1234567890</token>
    </login>
  </Body>
</Envelope>";

        string scrubbed = SoapSanitizer.Scrub(xml, maskTokens: true);

        Assert.Contains("<username>********</username>", scrubbed);
        Assert.Contains("<password>********</password>", scrubbed);
        Assert.Contains("<token>ABCDEF...(masked)</token>", scrubbed);
        Assert.DoesNotContain("SuperSecret!", scrubbed);
        Assert.DoesNotContain("ABCDEF1234567890", scrubbed);
    }

    [Fact]
    public void TokenMaskingCanBeDisabled()
    {
        string xml = @"<Envelope><Body><token>XYZ123456</token></Body></Envelope>";

        string scrubbed = SoapSanitizer.Scrub(xml, maskTokens: false);

        Assert.Contains("<token>XYZ123456</token>", scrubbed);
    }

    [Fact]
    public void HandlesMultilineValues()
    {
        string xml = @"<Envelope>
  <Body>
    <username>
      user-on-newline
    </username>
    <password>line1
line2</password>
  </Body>
</Envelope>";

        string scrubbed = SoapSanitizer.Scrub(xml, maskTokens: true);

        Assert.Contains("<username>********</username>", scrubbed.Replace("\r", "").Replace("\n", ""));
        Assert.Contains("<password>********</password>", scrubbed.Replace("\r", "").Replace("\n", ""));
    }
}