using Xunit;

public class SoapSanitizerTests
{
    [Fact]
    public void Masks_Username_Password_And_Token_By_Default()
    {
        var xml = @"<Envelope>
  <Body>
    <login>
      <username>alice</username>
      <password>SuperSecret!</password>
      <token>ABCDEF1234567890</token>
    </login>
  </Body>
</Envelope>";

        var scrubbed = SoapSanitizer.Scrub(xml, maskTokens: true);

        Assert.Contains("<username>********</username>", scrubbed);
        Assert.Contains("<password>********</password>", scrubbed);
        Assert.Contains("<token>ABCDEF...(masked)</token>", scrubbed);
        Assert.DoesNotContain("SuperSecret!", scrubbed);
        Assert.DoesNotContain("ABCDEF1234567890", scrubbed);
    }

    [Fact]
    public void Token_Masking_Can_Be_Disabled()
    {
        var xml = @"<Envelope><Body><token>XYZ123456</token></Body></Envelope>";

        var scrubbed = SoapSanitizer.Scrub(xml, maskTokens: false);

        Assert.Contains("<token>XYZ123456</token>", scrubbed);
    }

    [Fact]
    public void Handles_Multiline_Values()
    {
        var xml = @"<Envelope>
  <Body>
    <username>
      user-on-newline
    </username>
    <password>line1
line2</password>
  </Body>
</Envelope>";

        var scrubbed = SoapSanitizer.Scrub(xml, maskTokens: true);

        Assert.Contains("<username>********</username>", scrubbed.Replace("\r","").Replace("\n",""));
        Assert.Contains("<password>********</password>", scrubbed.Replace("\r","").Replace("\n",""));
    }
}