using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

namespace XRoadFolkRaw.Lib.Tests;

public class SafeSoapLoggerTests
{
    [Fact]
    public void GlobalSanitizer_Is_Invoked_And_Fallback_On_Exception()
    {
        try
        {
            int called = 0;
            SafeSoapLogger.GlobalSanitizer = s =>
            {
                called++;
                if (called == 1) throw new InvalidOperationException("boom");
                return "OK";
            };

            var logger = NullLogger.Instance;
            // First call: sanitizer throws, fallback to default and warning is logged (we can't assert log here with NullLogger)
            string s1 = SafeSoapLogger.Sanitize("<token>abc</token>", logger);
            Assert.NotNull(s1);
            // Second call: sanitizer returns "OK"
            string s2 = SafeSoapLogger.Sanitize("<token>abc</token>", logger);
            Assert.Equal("OK", s2);
        }
        finally
        {
            SafeSoapLogger.GlobalSanitizer = null;
        }
    }
}
