using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class CultureCookieTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CultureCookieTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        [Fact]
        public async Task SetCulture_Without_Antiforgery_Fails()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var post = new HttpRequestMessage(HttpMethod.Post, "/set-culture")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("culture", "en-US"),
                    new KeyValuePair<string,string>("returnUrl", "/")
                })
            };

            var resp = await client.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task SetCulture_InvalidCulture_Returns_BadRequest_And_NoCookie()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            string? token = await GetAntiTokenAsync(client);
            token.Should().NotBeNullOrEmpty();

            var post = new HttpRequestMessage(HttpMethod.Post, "/set-culture")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("culture", "xx-YY"),
                    new KeyValuePair<string,string>("returnUrl", "/")
                })
            };
            post.Headers.Add("RequestVerificationToken", token);

            var resp = await client.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            resp.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookies).Should().BeFalse();
        }

        [Fact]
        public async Task SetCulture_Sets_Cookie_With_Expected_Attributes()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            string? token = await GetAntiTokenAsync(client);
            token.Should().NotBeNullOrEmpty();

            var post = new HttpRequestMessage(HttpMethod.Post, "/set-culture")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("culture", "en-US"),
                    new KeyValuePair<string,string>("returnUrl", "/")
                })
            };
            post.Headers.Add("RequestVerificationToken", token);

            var resp = await client.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
            resp.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookies).Should().BeTrue();
            var cookie = setCookies!.First(c => c.Contains(".AspNetCore.Culture"));

            cookie.Should().Contain("Path=/");
            cookie.Should().Contain("HttpOnly");
            cookie.Should().Contain("SameSite=Lax");
            cookie.Should().Contain("Expires=");
            // TestServer uses HTTP, so cookie should not be marked Secure
            cookie.Should().NotContain("Secure");
        }

        [Fact]
        public async Task SetCulture_Invalid_ReturnUrl_Falls_Back_To_Root()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            string? token = await GetAntiTokenAsync(client);
            token.Should().NotBeNullOrEmpty();

            var post = new HttpRequestMessage(HttpMethod.Post, "/set-culture")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("culture", "en-US"),
                    new KeyValuePair<string,string>("returnUrl", "http://evil.example.com/")
                })
            };
            post.Headers.Add("RequestVerificationToken", token);

            var resp = await client.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
            resp.Headers.Location.Should().NotBeNull();
            // Expect fallback to root
            resp.Headers.Location!.ToString().Should().Be("/");
        }

        private static async Task<string?> GetAntiTokenAsync(System.Net.Http.HttpClient client)
        {
            var resp = await client.GetAsync("/");
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync();
            return ExtractMetaToken(html);
        }

        private static string? ExtractMetaToken(string html)
        {
            const string marker = "name=\"request-verification-token\"";
            int i = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int c = html.IndexOf("content=\"", i, StringComparison.OrdinalIgnoreCase);
            if (c < 0) return null;
            c += "content=\"".Length;
            int end = html.IndexOf('"', c);
            if (end <= c) return null;
            return html.Substring(c, end - c);
        }
    }
}
