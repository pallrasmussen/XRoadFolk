using System;
using Microsoft.Extensions.Logging;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

public class SafeSoapLoggerTests
{
    private sealed class ListLogger : ILogger
    {
        public readonly System.Collections.Generic.List<string> Lines = new();
        public IDisposable BeginScope<TState>(TState state) => default!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Lines.Add(formatter(state, exception));
    }

    [Fact]
    public void Masks_Username_Password_Token_By_Default()
    {
        var xml = @"<Envelope>
  <Header/>
  <Body>
    <Login>
      <username>alice</username>
      <password>p@ssw0rd</password>
      <token>ABC123TOKEN</token>
    </Login>
  </Body>
</Envelope>";

        var lg = new ListLogger();
        lg.SafeSoapDebug(xml, "test");

        var all = string.Join("\n", lg.Lines);
        Assert.Contains("username>*****ce<", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password>*******rd<", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token>**********EN<", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">alice<", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">p@ssw0rd<", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">ABC123TOKEN<", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allows_Global_Sanitizer_Override()
    {
        try
        {
            SafeSoapLogger.GlobalSanitizer = s => s.Replace("SECRET", "***");
            var xml = "<Envelope><token>SECRET</token></Envelope>";
            var lg = new ListLogger();
            lg.SafeSoapInfo(xml, "custom");

            var all = string.Join("\n", lg.Lines);
            Assert.Contains("***", all);
            Assert.DoesNotContain("SECRET", all);
        }
        finally
        {
            SafeSoapLogger.GlobalSanitizer = null;
        }
    }
}
