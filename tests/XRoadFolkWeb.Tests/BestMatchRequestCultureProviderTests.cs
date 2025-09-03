using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Infrastructure;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class BestMatchRequestCultureProviderTests
{
    private static BestMatchRequestCultureProvider CreateProvider(params string[] supported)
    {
        var cultures = supported.Select(s => new CultureInfo(s));
        return new BestMatchRequestCultureProvider(cultures, map: new Dictionary<string, string>{{"en","en-US"}}, logger: NullLogger.Instance);
    }

    [Fact]
    public async Task Picks_Cookie_Culture_When_Valid()
    {
        var provider = CreateProvider("en-US","da-DK");
        var ctx = new DefaultHttpContext();
        var opt = new RequestCulture("da-DK","da-DK");
        ctx.Request.Cookies = new RequestCookieCollection(new Dictionary<string, string>{{CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(opt)}});
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("da-DK", res!.Cultures[0].Value);
    }

    [Fact]
    public async Task Respects_AcceptLanguage_Q_Ordering()
    {
        var provider = CreateProvider("en-US","da-DK");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = "da-DK;q=0.5, en-US;q=0.9";
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("en-US", res!.Cultures[0].Value);
    }

    [Fact]
    public async Task Skips_Q_Zero_And_Star()
    {
        var provider = CreateProvider("en-US","da-DK");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = "*, da-DK;q=0, en-US";
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("en-US", res!.Cultures[0].Value);
    }

    [Fact]
    public async Task Falls_Back_To_Parent_Culture()
    {
        var provider = CreateProvider("en","da");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = "en-GB";
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("en", res!.Cultures[0].Value);
    }
}
