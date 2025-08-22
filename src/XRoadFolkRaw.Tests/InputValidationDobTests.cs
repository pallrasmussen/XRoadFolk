using System;
using System.Globalization;
using XRoadFolkRaw.Lib;
using Xunit;

namespace XRoadFolkRaw.Tests;

public class InputValidationDobTests
{
    [Theory]
    [InlineData("1980/12/31")]
    [InlineData("31.12.1980")]
    [InlineData("12/31/1980")]
    public void AcceptsAdditionalDobFormats(string input)
    {
        Assert.True(InputValidation.TryParseDob(input, out var dob));
        Assert.Equal(new DateTimeOffset(1980, 12, 31, 0, 0, 0, TimeSpan.Zero), dob);
    }

    [Fact]
    public void AcceptsCurrentCultureFormat()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            string input = "31/12/1980";
            Assert.True(InputValidation.TryParseDob(input, out var dob));
            Assert.Equal(new DateTimeOffset(1980, 12, 31, 0, 0, 0, TimeSpan.Zero), dob);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
