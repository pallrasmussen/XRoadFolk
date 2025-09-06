using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class CultureSwitchingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CultureSwitchingTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        [Fact]
        public async Task Index_Respects_AcceptLanguage_Header()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            using var req = new HttpRequestMessage(HttpMethod.Get, "/");
            req.Headers.AcceptLanguage.ParseAdd("da-DK,da;q=0.9,en-US;q=0.8,en;q=0.7");

            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Expect Current UI culture in response HTML somewhere as lang attribute
            string html = await resp.Content.ReadAsStringAsync();
            html.Should().Contain("lang=\"da-DK\"");
        }

        [Fact]
        public async Task SetCulture_Sets_Cookie_And_Redirects()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Load index to obtain antiforgery tokens (meta or hidden field emitted in layout)
            var indexResp = await client.GetAsync("/");
            indexResp.EnsureSuccessStatusCode();
            var html = await indexResp.Content.ReadAsStringAsync();

            // Extract token from meta tag if present
            string token = ExtractMetaToken(html) ?? string.Empty;

            using var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("culture", "fo-FO"),
                new KeyValuePair<string,string>("returnUrl", "/")
            });

            using var post = new HttpRequestMessage(HttpMethod.Post, "/set-culture");
            post.Content = formContent;
            if (!string.IsNullOrEmpty(token))
            {
                post.Headers.Add("RequestVerificationToken", token);
            }

            var resp = await client.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
            resp.Headers.Location.Should().NotBeNull();

            // Cookie should be set
            resp.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookies).Should().BeTrue();
            setCookies!.Should().Contain(c => c.Contains(".AspNetCore.Culture") || c.Contains(".AspNetCore.CultureC"));
        }

        [Fact]
        public async Task CultureCookie_Applies_On_Subsequent_Request()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Post culture
            var token = await GetAntiTokenAsync(client);
            using var post = new HttpRequestMessage(HttpMethod.Post, "/set-culture")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("culture", "en-US"),
                    new KeyValuePair<string,string>("returnUrl", "/")
                })
            };
            if (!string.IsNullOrEmpty(token))
            {
                post.Headers.Add("RequestVerificationToken", token);
            }
            var resp = await client.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.Redirect);

            // Follow with GET and ensure html lang is en-US
            var next = await client.GetAsync("/");
            next.EnsureSuccessStatusCode();
            var html = await next.Content.ReadAsStringAsync();
            html.Should().Contain("lang=\"en-US\"");
        }

        private static string? ExtractMetaToken(string html)
        {
            const string marker = "name=\"request-verification-token\"";
            int i = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                return null;
            }
            int c = html.IndexOf("content=\"", i, StringComparison.OrdinalIgnoreCase);
            if (c < 0)
            {
                return null;
            }
            c += "content=\"".Length;
            int end = html.IndexOf('"', c);
            if (end <= c)
            {
                return null;
            }
            return html.Substring(c, end - c);
        }

        private static async Task<string?> GetAntiTokenAsync(System.Net.Http.HttpClient client)
        {
            var resp = await client.GetAsync("/");
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync();
            return ExtractMetaToken(html);
        }
    }
}
