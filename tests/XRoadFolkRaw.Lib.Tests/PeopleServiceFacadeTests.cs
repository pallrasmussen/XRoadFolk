using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using Xunit;

namespace XRoadFolkRaw.Lib.Tests;

public class PeopleServiceFacadeTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string content = _responder(request);
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "text/xml")
            };
            return Task.FromResult(resp);
        }
    }

    private static XRoadSettings DefaultSettings() => new()
    {
        BaseUrl = "https://example.invalid/",
        Headers = new XRoadHeaderOptions { ProtocolVersion = "4.0" },
        Client = new XRoadClientOptions { XRoadInstance = "X", MemberClass = "GOV", MemberCode = "123", SubsystemCode = "Sub" },
        Service = new XRoadServiceOptions { XRoadInstance = "X", MemberClass = "GOV", MemberCode = "123", SubsystemCode = "S", ServiceCode = "Get", ServiceVersion = "v1" },
        Auth = new XRoadAuthOptions { UserId = "user", Username = "u", Password = "p" },
        Raw = new XRoadRawOptions { LoginXmlPath = "Resources/Login.xml" }
    };

    [Fact]
    public async Task GetPerson_Delegates_To_Client_With_Token()
    {
        // Fake responses: first call is Login -> return token in SOAP; second is GetPerson -> return payload
        bool loginSeen = false;
        var handler = new FakeHandler(req =>
        {
            string body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            if (body.Contains("<prod:Login") || !loginSeen)
            {
                loginSeen = true;
                return "<Envelope><Body><LoginResponse><token>abc</token></LoginResponse></Body></Envelope>";
            }
            return "<ok/>";
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var client = new FolkRawClient(http, NullLogger<FolkRawClient>.Instance, verbose: false);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var settings = DefaultSettings();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var localizer = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance).Create(typeof(PeopleService));
        var svc = new PeopleService(client, cfg, settings, NullLogger<PeopleService>.Instance, localizer, new GetPersonRequestOptionsValidator(), cache, Options.Create(new TokenCacheOptions()));

        var result = await svc.GetPersonAsync("pid", CancellationToken.None);
        Assert.Equal("<ok/>", result);
    }
}
