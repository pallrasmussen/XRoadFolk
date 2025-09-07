using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Xunit;

namespace XRoadFolkWeb.Tests.Ui
{
    public class PlaywrightUiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private IPlaywright? _pw;
        private IBrowser? _browser;

        public PlaywrightUiTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        public async Task InitializeAsync()
        {
            _pw = await Playwright.CreateAsync();
            _browser = await _pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        }

        public async Task DisposeAsync()
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
            }
            _pw?.Dispose();
        }

        private async Task<IPage> NewPageAsync()
        {
            var ctx = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
            });
            var page = await ctx.NewPageAsync();
            return page;
        }

        [Fact(Skip="Playwright requires browsers installed; run locally with browsers provisioned.")]
        public async Task Index_Tabs_Summary_Copy_Download_And_LazyLoad_PersonDetails()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = true
            });
            var baseAddress = client.BaseAddress ?? new Uri("http://localhost");

            var page = await NewPageAsync();
            await page.GotoAsync(baseAddress.ToString());

            // Tabs exist (Summary, Raw XML, Pretty XML)
            await page.Locator("#viewer-tabs").IsVisibleAsync();

            // Paste some simple XML into the viewer textarea to build summary
            string xml = "<Envelope><Body><GetPeoplePublicInfoResponse><ListOfPersonPublicInfo><PersonPublicInfo><PublicId>P1</PublicId><Names><Name><Type>FirstName</Type><Order>1</Order><Value>Jane</Value></Name><Name><Type>LastName</Type><Value>Doe</Value></Name></Names></PersonPublicInfo></ListOfPersonPublicInfo></GetPeoplePublicInfoResponse></Body></Envelope>";
            await page.FillAsync("#xml-input", xml);
            await page.ClickAsync("#build-summary");

            // Summary shows People section and a row
            await page.Locator("#people-summary").WaitForAsync();
            (await page.Locator("#people-summary .person-row").CountAsync()).Should().BeGreaterThan(0);

            // Copy and Download buttons exist and are clickable
            await page.ClickAsync("#copy-xml");
            await page.ClickAsync("#download-xml");

            // Lazy load person details when clicking a person
            var firstPerson = page.Locator("#people-summary .person-row").First;
            await firstPerson.ClickAsync();
            await page.Locator("#person-details-section").WaitForAsync(new LocatorWaitForOptions{ State = WaitForSelectorState.Visible });
        }
    }
}
