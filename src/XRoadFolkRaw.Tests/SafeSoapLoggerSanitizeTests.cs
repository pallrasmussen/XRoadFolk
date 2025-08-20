using System;
using System.Text.RegularExpressions;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

public class SafeSoapLoggerSanitizeTests
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

        Assert.Matches(new Regex("username>\\*+ce<", RegexOptions.IgnoreCase), sanitized);
        Assert.Matches(new Regex("password>\\*+rd<", RegexOptions.IgnoreCase), sanitized);
        Assert.Matches(new Regex("token>\\*+EN<", RegexOptions.IgnoreCase), sanitized);
        Assert.Matches(new Regex("password=\\"\\*+23\\"", RegexOptions.IgnoreCase), sanitized);
        Assert.Matches(new Regex("apikey=\\"\\*+23\\"", RegexOptions.IgnoreCase), sanitized);
    }
}
