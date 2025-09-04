using XRoadFolkRaw.Lib;
using Xunit;

namespace XRoadFolkRaw.Lib.Tests;

public class SoapSanitizerAdditionalTests
{
    [Fact]
    public void Scrub_Masks_Namespaced_User_Pass_Token_And_Aliases()
    {
        string xml = """
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <prod:Login xmlns:prod="urn:x">
              <prod:username>ns-user</prod:username>
              <prod:password>ns-pass</prod:password>
              <prod:userId>u-123456789</prod:userId>
              <prod:token>ns-token-abcdef</prod:token>
              <wsse:BinarySecurityToken xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd">BASE64TOKEN</wsse:BinarySecurityToken>
              <sessionId>abcdef-123456</sessionId>
              <authToken>auth-xyz-987</authToken>
            </prod:Login>
          </s:Body>
        </s:Envelope>
        """;

        string s = SoapSanitizer.Scrub(xml, maskTokens: true);
        Assert.DoesNotContain("ns-user", s);
        Assert.DoesNotContain("ns-pass", s);
        Assert.DoesNotContain("u-123456789", s);
        Assert.DoesNotContain("ns-token-abcdef", s);
        Assert.DoesNotContain("BASE64TOKEN", s);
        Assert.DoesNotContain("abcdef-123456", s);
        Assert.DoesNotContain("auth-xyz-987", s);
        Assert.Contains("username", s);
        Assert.Contains("password", s);
        Assert.Contains("userId", s);
        Assert.Contains("BinarySecurityToken", s);
    }
}
