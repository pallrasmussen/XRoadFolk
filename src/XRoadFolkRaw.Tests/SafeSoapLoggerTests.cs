using System;
using Microsoft.Extensions.Logging;
using XRoadFolkRaw.Lib.Logging;
using Xunit;

public class SafeSoapLoggerTests
{
    private sealed class ListLogger : ILogger
    {
        public readonly System.Collections.Generic.List<string> Lines = [];
        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new NoOpDisposable();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void MasksUsernamePasswordTokenByDefault()
    {
        string xml = @"<Envelope>
  <Header/>
  <Body>
    <Login>
      <username>alice</username>
      <password>p@ssw0rd</password>
      <token>ABC123TOKEN</token>
    </Login>
  </Body>
</Envelope>";

        ListLogger lg = new();
        lg.SafeSoapDebug(xml, "test");

        string all = string.Join("\n", lg.Lines);
        Assert.Contains("username>*****ce<", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password>*******rd<", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token>**********EN<", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">alice<", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">p@ssw0rd<", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">ABC123TOKEN<", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsGlobalSanitizerOverride()
    {
        try
        {
            SafeSoapLogger.GlobalSanitizer = s => s.Replace("SECRET", "***");
            string xml = "<Envelope><token>SECRET</token></Envelope>";
            ListLogger lg = new();
            lg.SafeSoapInfo(xml, "custom");

            string all = string.Join("\n", lg.Lines);
            Assert.Contains("***", all);
            Assert.DoesNotContain("SECRET", all);
        }
        finally
        {
            SafeSoapLogger.GlobalSanitizer = null;
        }
    }
}
