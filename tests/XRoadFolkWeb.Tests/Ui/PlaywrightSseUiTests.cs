using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Xunit;

namespace XRoadFolkWeb.Tests.Ui
{
    public class PlaywrightSseUiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private IPlaywright? _pw;
        private IBrowser? _browser;

        public PlaywrightSseUiTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        public async Task InitializeAsync()
        {
            _pw = await Playwright.CreateAsync();
            _browser = await _pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }

        public async Task DisposeAsync()
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
            }
            _pw?.Dispose();
        }

        private async Task<IPage> NewInstrumentedPageAsync()
        {
            var ctx = await _browser!.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

            // Instrument EventSource before any app script runs
            await ctx.AddInitScriptAsync(@"(() => {
                const NativeES = window.EventSource;
                const counters = { created: 0, closed: 0, active: 0, lastUrl: null };
                class WrappedES extends NativeES {
                    constructor(url, init){ super(url, init); counters.created++; counters.active++; counters.lastUrl = String(url); }
                    close(){ try{ super.close(); } finally { counters.closed++; counters.active = Math.max(0, counters.active - 1); } }
                }
                Object.defineProperty(window, '__esCounters', { value: counters, writable: false });
                window.EventSource = WrappedES;
            })();");

            return await ctx.NewPageAsync();
        }

        [Fact(Skip = "Playwright browsers required. Run locally after installing browsers.")]
        public async Task Logs_SwitchKind_RecreatesSingleSse()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
            var baseAddress = client.BaseAddress ?? new Uri("http://localhost");

            var page = await NewInstrumentedPageAsync();
            await page.GotoAsync(new Uri(baseAddress, "/Logs/App").ToString());

            // Wait for viewer to initialize and connect SSE
            await page.WaitForSelectorAsync("#logs-section");
            await page.WaitForTimeoutAsync(500);

            var created1 = await page.EvaluateAsync<int>("() => window.__esCounters.created");
            var active1 = await page.EvaluateAsync<int>("() => window.__esCounters.active");
            created1.Should().BeGreaterThanOrEqualTo(1);
            active1.Should().Be(1);

            // Click SOAP kind -> should close previous and create a new one
            await page.ClickAsync("[data-kind=soap]");
            await page.WaitForTimeoutAsync(500);

            var created2 = await page.EvaluateAsync<int>("() => window.__esCounters.created");
            var closed2 = await page.EvaluateAsync<int>("() => window.__esCounters.closed");
            var active2 = await page.EvaluateAsync<int>("() => window.__esCounters.active");
            created2.Should().Be(created1 + 1);
            closed2.Should().BeGreaterThanOrEqualTo(1);
            active2.Should().Be(1);

            // Click APP kind -> same behavior
            await page.ClickAsync("[data-kind=app]");
            await page.WaitForTimeoutAsync(500);

            var created3 = await page.EvaluateAsync<int>("() => window.__esCounters.created");
            var closed3 = await page.EvaluateAsync<int>("() => window.__esCounters.closed");
            var active3 = await page.EvaluateAsync<int>("() => window.__esCounters.active");
            created3.Should().Be(created2 + 1);
            closed3.Should().BeGreaterThanOrEqualTo(closed2 + 1);
            active3.Should().Be(1);
        }

        [Fact(Skip = "Playwright browsers required. Run locally after installing browsers.")]
        public async Task Logs_Toggle_View_Tabs_Update_Visibility()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
            var baseAddress = client.BaseAddress ?? new Uri("http://localhost");

            var page = await NewInstrumentedPageAsync();
            await page.GotoAsync(new Uri(baseAddress, "/Logs/App").ToString());

            await page.WaitForSelectorAsync("#logs-section");

            // Default is table visible, cards hidden
            (await page.Locator(".logs-table-container").IsVisibleAsync()).Should().BeTrue();
            (await page.Locator("#logs-cards").IsVisibleAsync()).Should().BeFalse();

            // Switch to cards view
            await page.ClickAsync("[data-view=cards]");
            await page.WaitForTimeoutAsync(100);
            (await page.Locator("#logs-cards").IsVisibleAsync()).Should().BeTrue();
            (await page.Locator(".logs-table-container").IsVisibleAsync()).Should().BeFalse();

            // Back to table view
            await page.ClickAsync("[data-view=table]");
            await page.WaitForTimeoutAsync(100);
            (await page.Locator(".logs-table-container").IsVisibleAsync()).Should().BeTrue();
            (await page.Locator("#logs-cards").IsVisibleAsync()).Should().BeFalse();
        }
    }
}
