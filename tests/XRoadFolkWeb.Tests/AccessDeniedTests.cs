using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class AccessDeniedTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly TestLogSink _sink = new();

    public AccessDeniedTests(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new System.Collections.Generic.Dictionary<string,string?>
                {
                    ["AppRoles:ImplicitWindowsAdminEnabled"] = "false" // ensure no implicit elevation during test
                };
                cfg.AddInMemoryCollection(dict);
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(BasicTestAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, BasicTestAuthHandler>(BasicTestAuthHandler.Scheme, _ => { });
                services.AddLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.AddProvider(new TestLoggerProvider(_sink));
                    lb.SetMinimumLevel(LogLevel.Debug);
                });
            });
        });
    }

    [Fact]
    public async Task Forbidden_Request_Logs_And_Shows_Localized_Page()
    {
        // First call: expect 302 redirect to /Error/403 (status code pages redirect)
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/Admin/Users");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.RedirectKeepVerb, HttpStatusCode.Found, HttpStatusCode.SeeOther);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().StartWith("/Error/403");

        // Follow to 403 page
        var forbidden = await client.GetAsync(resp.Headers.Location);
        forbidden.StatusCode.Should().Be(HttpStatusCode.OK); // custom Razor page rendered
        string html = await forbidden.Content.ReadAsStringAsync();
        html.Should().Contain("Access denied"); // fallback or localized title
        html.Should().MatchRegex("(?i)contact.*administrator");

        // Logging assertion: event id 1100, category Auth.Denied, includes path
        _sink.Events.Should().Contain(e => e.EventId.Id == 1100 && e.Category == "Auth.Denied" && e.Message.Contains("/Admin/Users"));
        // Also ensure user name captured
        _sink.Events.Should().Contain(e => e.EventId.Id == 1100 && e.Message.Contains("testuser"));

        resp.Headers.TryGetValues("X-Access-Denied-Message", out var deniedHeader).Should().BeTrue();
        deniedHeader!.FirstOrDefault().Should().NotBeNullOrWhiteSpace();
        deniedHeader!.First().Should().Contain("Access denied");
    }

    private sealed class BasicTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "BasicTest";
        public BasicTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "testuser") }; // No role claims => should yield 403 for Admin area
            var id = new ClaimsIdentity(claims, Scheme);
            var principal = new ClaimsPrincipal(id);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly TestLogSink _sink;
        public TestLoggerProvider(TestLogSink sink) => _sink = sink;
        public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _sink);
        public void Dispose() { }
    }

    private sealed class TestLogger : ILogger
    {
        private readonly string _category; private readonly TestLogSink _sink;
        public TestLogger(string category, TestLogSink sink) { _category = category; _sink = sink; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            _sink.Add(new LogEvent(_category, logLevel, eventId, msg));
        }
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }

    private sealed record LogEvent(string Category, LogLevel Level, EventId EventId, string Message);
    private sealed class TestLogSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();
        public void Add(LogEvent evt) => _events.Enqueue(evt);
        public System.Collections.Generic.IReadOnlyCollection<LogEvent> Events => _events.ToArray();
    }
}
