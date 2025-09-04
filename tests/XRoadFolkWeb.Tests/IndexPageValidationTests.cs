using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using AngleSharp;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class IndexPageValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IndexPageValidationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task Get_Index_Renders_Client_Side_Validation_Attributes()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");

        var doc = await new HtmlParser().ParseDocumentAsync(html);
        var ssnInput = doc.QuerySelector("input[name='Ssn']");
        ssnInput.Should().NotBeNull();
        ssnInput!.GetAttribute("data-val").Should().Be("true");
        ssnInput.GetAttribute("data-val-ssn").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_Index_Invalid_Ssn_Shows_Error()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("Ssn","123"), // invalid
            new KeyValuePair<string,string>("FirstName",""),
            new KeyValuePair<string,string>("LastName",""),
            new KeyValuePair<string,string>("DateOfBirth","")
        });

        var resp = await client.PostAsync("/", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await resp.Content.ReadAsStringAsync();
        var doc = await new HtmlParser().ParseDocumentAsync(html);
        var ssnSpan = doc.QuerySelector("span[data-valmsg-for='Ssn']");
        ssnSpan.Should().NotBeNull();
        (ssnSpan!.TextContent ?? "").Trim().Length.Should().BeGreaterThan(0);
    }
}