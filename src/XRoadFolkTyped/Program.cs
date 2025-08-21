using System.ServiceModel;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using FOLKService.ServiceReference1;
using XRoadFolkRaw.Lib;

// Load configuration
IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

XRoadSettings xr = config.GetSection("XRoad").Get<XRoadSettings>() ?? new();

// Load client certificate
X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);

// Configure binding and endpoint
BasicHttpsBinding binding = new(BasicHttpsSecurityMode.Transport)
{
    Security =
    {
        Transport = { ClientCredentialType = HttpClientCredentialType.Certificate }
    }
};

EndpointAddress address = new(xr.BaseUrl);

// Instantiate generated client
using CrsPortTypeClient client = new(binding, address);
client.ClientCredentials.ClientCertificate.Certificate = cert;

// Prepare request for testSystem operation
testSystemRequest request = new(
    xr.Headers.ProtocolVersion,
    Guid.NewGuid().ToString(),
    new XRoadClientIdentifierType
    {
        xRoadInstance = xr.Client.XRoadInstance,
        memberClass = xr.Client.MemberClass,
        memberCode = xr.Client.MemberCode,
        subsystemCode = xr.Client.SubsystemCode
    },
    new XRoadServiceIdentifierType
    {
        xRoadInstance = xr.Service.XRoadInstance,
        memberClass = xr.Service.MemberClass,
        memberCode = xr.Service.MemberCode,
        subsystemCode = xr.Service.SubsystemCode,
        serviceCode = xr.Service.ServiceCode,
        serviceVersion = xr.Service.ServiceVersion
    },
    null!);

// Call service and log response
testSystemResponse response = await client.testSystemAsync(request);
Console.WriteLine($"testSystemResponse: {response.testSystemResponse1 ?? "<null>"}");

