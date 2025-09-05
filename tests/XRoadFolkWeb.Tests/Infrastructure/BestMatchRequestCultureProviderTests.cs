using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Infrastructure;
using Xunit;

namespace XRoadFolkWeb.Tests.Infrastructure
{
    public class BestMatchRequestCultureProviderTests
    {
        private static BestMatchRequestCultureProvider CreateProvider(params string[] supported)
        {
            var cultures = supported.Select(s => new CultureInfo(s));
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fo"] = "fo-FO",
                ["da"] = "da-DK",
                ["en"] = "en-US",
            };
            return new BestMatchRequestCultureProvider(cultures, map, NullLogger.Instance);
        }

        private static async Task<ProviderCultureResult?> DetermineAsync(BestMatchRequestCultureProvider provider, string? acceptLanguage = null, string? cookieCulture = null)
        {
            var ctx = new DefaultHttpContext();
            if (!string.IsNullOrWhiteSpace(acceptLanguage))
            {
                ctx.Request.Headers.AcceptLanguage = acceptLanguage;
            }
            if (!string.IsNullOrWhiteSpace(cookieCulture))
            {
                string cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(cookieCulture));
                // Set request cookie header directly
                ctx.Request.Headers["Cookie"] = $"{CookieRequestCultureProvider.DefaultCookieName}={cookieValue}";
            }
            return await provider.DetermineProviderCultureResult(ctx);
        }

        [Fact]
        public async Task Cookie_Takes_Priority_If_Valid()
        {
            var provider = CreateProvider("fo-FO", "da-DK", "en-US");
            var result = await DetermineAsync(provider, acceptLanguage: "da-DK, en-US", cookieCulture: "en-US");
            result.Should().NotBeNull();
            result!.Cultures[0].Value.Should().Be("en-US");
            result.UICultures[0].Value.Should().Be("en-US");
        }

        [Fact]
        public async Task TypedHeaders_Q_Order_Is_Respected()
        {
            var provider = CreateProvider("fo-FO", "da-DK", "en-US");
            var result = await DetermineAsync(provider, acceptLanguage: "da-DK;q=0.4, en-US;q=0.8, fo-FO;q=1.0");
            result.Should().NotBeNull();
            result!.Cultures[0].Value.Should().Be("fo-FO");
        }

        [Fact]
        public async Task Invalid_Tags_Are_Skipped()
        {
            var provider = CreateProvider("fo-FO", "da-DK", "en-US");
            var result = await DetermineAsync(provider, acceptLanguage: "*;q=1, zz-INVALID;q=0.9, en-US;q=0.8");
            result.Should().NotBeNull();
            result!.Cultures[0].Value.Should().Be("en-US");
        }

        [Fact]
        public async Task Same_Language_Fallback_Works()
        {
            var provider = CreateProvider("en-US");
            var result = await DetermineAsync(provider, acceptLanguage: "en-GB");
            result.Should().NotBeNull();
            result!.Cultures[0].Value.Should().Be("en-US");
        }

        [Fact]
        public async Task Handles_Large_AcceptLanguage_Headers()
        {
            var provider = CreateProvider("fo-FO", "da-DK", "en-US");
            // Build a large header with many non-matching tags, then a match with highest q
            var many = string.Join(',', Enumerable.Range(0, 200).Select(i => $"xx-{i};q=0.{i % 9}"));
            var header = many + ", fo-FO;q=1";
            var result = await DetermineAsync(provider, acceptLanguage: header);
            result.Should().NotBeNull();
            result!.Cultures[0].Value.Should().Be("fo-FO");
        }
    }
}
