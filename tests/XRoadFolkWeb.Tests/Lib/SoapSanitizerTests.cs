using FluentAssertions;
using XRoadFolkRaw.Lib;
using Xunit;

namespace XRoadFolkWeb.Tests.Lib;

public class SoapSanitizerTests
{
    [Fact]
    public void Masks_User_Pass_Token_In_Namespaced_And_NonNamespaced_Tags()
    {
        string xml = """
        <Envelope xmlns:a="urn:test">
          <a:username>alice</a:username>
          <a:password>secret</a:password>
          <a:token>abcdef1234567890</a:token>
          <username>bob</username>
          <password>topsecret</password>
          <token>ghijkl9876543210</token>
        </Envelope>
        """;
        string scrubbed = SoapSanitizer.Scrub(xml, maskTokens: true);
        scrubbed.Should().NotContain("alice").And.NotContain("secret");
        scrubbed.Should().Contain("<username>****e</username>");
        scrubbed.Should().Contain("<password>****t</password>");
        // token keeps last 6 visible
        scrubbed.Should().Contain("<token>**********567890</token>");
    }

    [Fact]
    public void Masks_UserId_And_Token_Aliases()
    {
        string xml = """
        <Envelope>
          <x:userId xmlns:x='urn:test'>my-user</x:userId>
          <sessionId>SID-123456</sessionId>
          <accessToken>ZXCVBNMASDF</accessToken>
          <BinarySecurityToken>BIN-TOKEN</BinarySecurityToken>
        </Envelope>
        """;
        string scrubbed = SoapSanitizer.Scrub(xml, maskTokens: true);
        scrubbed.Should().Contain("<x:userId xmlns:x=\"urn:test\">****ser</x:userId>");
        scrubbed.Should().Contain("<sessionId>*****23456</sessionId>");
        scrubbed.Should().Contain("<accessToken>*****MASDF</accessToken>");
        scrubbed.Should().Contain("<BinarySecurityToken>****TOKEN</BinarySecurityToken>");
    }

    [Fact]
    public void Leaves_Token_Unmasked_When_Flag_False()
    {
        string xml = "<Envelope><token>abc123456</token></Envelope>";
        string scrubbed = SoapSanitizer.Scrub(xml, maskTokens: false);
        scrubbed.Should().Contain("<token>abc123456</token>");
    }
}
