using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Infrastructure;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class BestMatchRequestCultureProviderHardeningTests
{
    private static BestMatchRequestCultureProvider CreateProvider(params string[] supported)
    {
        var cultures = supported.Select(s => new CultureInfo(s));
        var map = new Dictionary<string,string>{{"en","en-US"},{"fo","fo-FO"}};
        return new BestMatchRequestCultureProvider(cultures, map, NullLogger.Instance);
    }

    [Fact]
    public async Task Caps_Header_Length_And_Items()
    {
        var provider = CreateProvider("en-US","da-DK","fo-FO");
        var ctx = new DefaultHttpContext();
        // Build a very long header with many items
        var many = string.Join(",", Enumerable.Repeat("zz-ZZ;q=0.9", 100));
        ctx.Request.Headers.AcceptLanguage = many + ", en-US";
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("en-US", res!.Cultures[0].Value);
    }

    [Theory]
    [InlineData("en-US;q=1.000", "en-US")]
    [InlineData("en-US;q=0.5", "en-US")]
    [InlineData("en-US;q=1.1", "en-US")] // invalid q -> default 1.0
    [InlineData("en-US;q=abc", "en-US")] // invalid q -> default 1.0
    public async Task Parses_Q_Strictly_With_Default_On_Invalid(string header, string expected)
    {
        var provider = CreateProvider("en-US","da-DK");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = header;
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal(expected, res!.Cultures[0].Value);
    }

    [Fact]
    public async Task Uses_Configured_Mapping()
    {
        var provider = CreateProvider("en-US","fo-FO");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = "en, fo";
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("en-US", res!.Cultures[0].Value);
    }

    [Fact]
    public async Task Rejects_Invalid_Tags()
    {
        var provider = CreateProvider("en-US","da-DK");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = "en_US;q=0.9, **;q=0.8, en-US";
        var res = await provider.DetermineProviderCultureResult(ctx);
        Assert.Equal("en-US", res!.Cultures[0].Value);
    }
}
