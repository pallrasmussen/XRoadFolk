using System.Text.RegularExpressions;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

public partial class SafeSoapLoggerSanitizeTests
{
    [Fact]
    public void SanitizesSensitiveElementsAndAttributes()
    {
        string xml = "<Envelope password=\"secret123\" apikey=\"apikey123\"><Body><Login><username>alice</username><password>p@ssw0rd</password><token>ABC123TOKEN</token></Login></Body></Envelope>";

        string sanitized = SafeSoapLogger.Sanitize(xml);

        Assert.DoesNotContain(">alice<", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">p@ssw0rd<", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">ABC123TOKEN<", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password=\"secret123\"", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apikey=\"apikey123\"", sanitized, StringComparison.OrdinalIgnoreCase);

        Assert.Matches(UsernameRegex(), sanitized);
        Assert.Matches(PasswordRegex(), sanitized);
        Assert.Matches(TokenRegex(), sanitized);
        Assert.Matches(PasswordAttributeRegex(), sanitized);
        Assert.Matches(ApiKeyAttributeRegex(), sanitized);
    }

    [GeneratedRegex("username>\\*+ce<", RegexOptions.IgnoreCase)]
    private static partial Regex UsernameRegex();

    [GeneratedRegex("password>\\*+rd<", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex("token>\\*+EN<", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    [GeneratedRegex("password=\\\"\\*+23\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordAttributeRegex();

    [GeneratedRegex("apikey=\\\"\\*+23\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyAttributeRegex();
}
