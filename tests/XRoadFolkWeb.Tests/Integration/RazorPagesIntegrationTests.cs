using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using System.Threading.Tasks;

namespace XRoadFolkWeb.Tests.Integration;

public class RazorPagesIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RazorPagesIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task Get_Index_Returns200_And_HasSearchForm()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = true,
        });

        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("gpip-search-form", html);
    }

    [Fact]
    public async Task Post_SetCulture_Without_AntiForgery_Returns400()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string?, string?>("culture", "en-US"),
            new KeyValuePair<string?, string?>("returnUrl", "/"),
        });

        var resp = await client.PostAsync("/set-culture", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_SetCulture_With_AntiForgery_SetsCookie_AndRedirects()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        // Prime antiforgery token by visiting home
        string token = await GetAntiForgeryTokenAsync(client);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var req = new HttpRequestMessage(HttpMethod.Post, "/set-culture");
        req.Headers.Add("RequestVerificationToken", token);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string?, string?>("culture", "en-US"),
            new KeyValuePair<string?, string?>("returnUrl", "/"),
        });

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        // Culture cookie should be present
        IEnumerable<string>? setCookie;
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out setCookie));
        string cookies = string.Join(";", setCookie!);
        Assert.Contains(".AspNetCore.Culture", cookies);
    }

    [Fact]
    public async Task Logs_Endpoints_Get_List_And_Clear_Requiring_AntiForgery_For_Posts()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        // GET list should be 200 with ok:true
        var listResp = await client.GetAsync("/logs?kind=app");
        string listJson = await listResp.Content.ReadAsStringAsync();
        Assert.True(listResp.IsSuccessStatusCode, listJson);
        Assert.Contains("\"ok\":true", listJson);

        // POST clear without antiforgery -> 400
        var clearBad = await client.PostAsync("/logs/clear", new StringContent(string.Empty, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, clearBad.StatusCode);

        // POST clear with antiforgery -> 200 ok
        string token = await GetAntiForgeryTokenAsync(client);
        var req = new HttpRequestMessage(HttpMethod.Post, "/logs/clear");
        req.Headers.Add("RequestVerificationToken", token);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var clearOk = await client.SendAsync(req);
        string clearJson = await clearOk.Content.ReadAsStringAsync();
        Assert.True(clearOk.IsSuccessStatusCode, clearJson);
        Assert.Contains("\"ok\":true", clearJson);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(System.Net.Http.HttpClient client)
    {
        var resp = await client.GetAsync("/");
        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync();
        // Parse meta name="request-verification-token"
        var parser = new HtmlParser();
        var doc = await parser.ParseDocumentAsync(html);
        var meta = doc.QuerySelector("meta[name=\"request-verification-token\"]");
        var token = meta?.GetAttribute("content") ?? string.Empty;
        return token;
    }
}
