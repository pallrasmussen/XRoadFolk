using XRoadFolkRaw.Lib;
using Xunit;

public class LoggingHelperTests
{
    [Theory]
    [InlineData(null, 4, "")]
    [InlineData("", 4, "")]
    [InlineData("123456789", 0, "*********")]
    [InlineData("abcdef", -2, "******")]
    [InlineData("123456789", 4, "*****6789")]
    [InlineData("abcd", 10, "abcd")]
    public void MasksValuesCorrectly(string? value, int visible, string expected)
    {
        string result = LoggingHelper.Mask(value, visible);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void UsesDefaultVisibleOfFour()
    {
        string result = LoggingHelper.Mask("abcdefghij");
        Assert.Equal("******ghij", result);
    }
}
