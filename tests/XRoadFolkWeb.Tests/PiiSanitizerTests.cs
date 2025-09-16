using System;
using FluentAssertions;
using Xunit;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Tests
{
    public class PiiSanitizerTests
    {
        [Theory]
        [InlineData("User GUID 123e4567-e89b-12d3-a456-426614174000 should mask")]
        public void Masks_Guids(string input)
        {
            var s = PiiSanitizer.Sanitize(input, maskTokens: true);
            s.Should().Contain("****");
        }

        [Fact]
        public void Masks_Long_Digits()
        {
            var s = PiiSanitizer.Sanitize("ID 1234567890", true);
            s.Should().NotContain("1234567890");
        }

        [Fact]
        public void Masks_Email_User_And_Domain()
        {
            var s = PiiSanitizer.Sanitize("Contact john.doe@example.com now", true);
            s.Should().NotContain("john.doe@example.com");
            s.Should().Contain("*@***");
        }

        [Fact]
        public void Masks_Bearer_Token()
        {
            var s = PiiSanitizer.Sanitize("Authorization: Bearer abcdefghijklmnop", true);
            s.Should().Contain("Bearer").And.NotContain("abcdefghijklmnop");
        }

        [Fact]
        public void Respects_MaskTokens_False_For_ApiKey()
        {
            var s = PiiSanitizer.Sanitize("Api-Key=SECRETKEYVALUE", false);
            s.Should().Contain("SECRETKEYVALUE");
        }

        [Fact]
        public void Soap_Reroutes_To_Soap_Sanitizer()
        {
            var xml = "<Envelope><username>bob</username><password>pass</password></Envelope>";
            var s = PiiSanitizer.Sanitize(xml, true);
            s.Should().Contain("<username>***").And.Contain("<password>***");
        }

        [Theory]
        [InlineData("SSN=123456789")]
        [InlineData("ForeignSSN=998877665")]
        [InlineData("{\"SSN\":\"123456789\"}")]
        [InlineData("{foreignSsn: '112233445'}")]
        [InlineData("The SSN is 123-45-6789")] 
        public void Masks_Ssn_And_ForeignSsn_In_Text(string input)
        {
            var s = PiiSanitizer.Sanitize(input, true);
            s.Should().NotContain("123456789").And.NotContain("998877665").And.NotContain("112233445").And.NotContain("123-45-6789");
        }

        [Fact]
        public void Masks_Ssn_And_ForeignSsn_In_Soap()
        {
            var xml = "<Envelope><Body><SSN>123456789</SSN><ForeignSSN>AA123456BB</ForeignSSN></Body></Envelope>";
            var s = PiiSanitizer.Sanitize(xml, true);
            s.Should().Contain("<SSN>***");
            s.Should().Contain("<ForeignSSN>***");
            s.Should().NotContain("123456789").And.NotContain("AA123456BB");
        }
    }
}
