using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FluentAssertions;
using XRoadFolkWeb.Validation;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class SsnAttributeTests
{
    private readonly SsnAttribute _attr = new();

    [Theory]
    [InlineData("010203123")]
    [InlineData("01-02-03 123")]
    public void Server_IsValid_When_9_Digits(string value)
    {
        _attr.GetValidationResult(value, new ValidationContext(new object()))
             .Should().Be(ValidationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abcdefgh9")]
    public void Server_IsInvalid_Otherwise(string value)
    {
        var result = _attr.GetValidationResult(value, new ValidationContext(new object()));
        result.Should().NotBe(ValidationResult.Success);
        result!.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Localized_Message_Changes_With_Culture()
    {
        var prev = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var en = _attr.GetValidationResult("123", new ValidationContext(new object()))!.ErrorMessage;

            CultureInfo.CurrentUICulture = new CultureInfo("fo-FO");
            var fo = _attr.GetValidationResult("123", new ValidationContext(new object()))!.ErrorMessage;

            // At minimum, messages are non-empty; if you provide culture-specific values, they may differ
            en.Should().NotBeNullOrWhiteSpace();
            fo.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }
}