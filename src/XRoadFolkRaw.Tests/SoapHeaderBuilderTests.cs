using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using XRoadFolkRaw.Tests.Helpers;
using Xunit;

public class SoapHeaderBuilderTests
{
    private const string ProdNs = "http://us-folk-v2.x-road.eu/producer";

    private static string ExtractBody(string rawHttp)
    {
        int idx = rawHttp.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return idx >= 0 ? rawHttp[(idx + 4)..] : rawHttp;
    }

    private static string MinimalLoginTemplate() => $@"<?xml version=\"1.0\"?>
<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:prod=\"{ProdNs}\">
  <soapenv:Header />
  <soapenv:Body>
    <prod:Login>
      <request>
        <username></username>
        <password></password>
      </request>
    </prod:Login>
  </soapenv:Body>
</soapenv:Envelope>";

    private static string MinimalGetPeopleTemplate() => $@"<?xml version=\"1.0\"?>
<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:prod=\"{ProdNs}\">
  <soapenv:Header />
  <soapenv:Body>
    <prod:GetPeoplePublicInfo>
      <request>
        <requestHeader />
        <requestBody>
          <ListOfPersonPublicInfoCriteria>
            <PersonPublicInfoCriteria />
          </ListOfPersonPublicInfoCriteria>
        </requestBody>
      </request>
    </prod:GetPeoplePublicInfo>
  </soapenv:Body>
</soapenv:Envelope>";

    private static string MinimalGetPersonTemplate() => $@"<?xml version=\"1.0\"?>
<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:prod=\"{ProdNs}\">
  <soapenv:Header />
  <soapenv:Body>
    <prod:GetPerson>
      <request>
        <requestHeader />
        <requestBody />
      </request>
    </prod:GetPerson>
  </soapenv:Body>
</soapenv:Envelope>";

    [Fact]
    public async Task LoginAsync_BuildsSoapHeader()
    {
        await using TestServer server = await TestServer.StartAsync("<Envelope />");
        string url = $"http://127.0.0.1:{server.Port}/";
        string tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalLoginTemplate(), Encoding.UTF8);

        FolkRawClient client = new(url, null, TimeSpan.FromSeconds(5), logger: null, verbose: false, maskTokens: false);

        await client.LoginAsync(
            loginXmlPath: tmp,
            xId: "LOGIN-1",
            userId: "tester",
            username: "bob",
            password: "hunter2",
            protocolVersion: "4.0",
            clientXRoadInstance: "EE",
            clientMemberClass: "GOV",
            clientMemberCode: "1234567",
            clientSubsystemCode: "MY-SUBSYS",
            serviceXRoadInstance: "EE",
            serviceMemberClass: "GOV",
            serviceMemberCode: "7654321",
            serviceSubsystemCode: "TARGET-SUBSYS",
            serviceCode: "login",
            serviceVersion: "v1",
            ct: default);

        await server.DisposeAsync();
        string body = ExtractBody(server.CapturedRequest);

        SoapAssert.HasElement(body, "userId", "tester");
        SoapAssert.HasElement(body, "id", "LOGIN-1");
        SoapAssert.HasElement(body, "protocolVersion", "4.0");
        SoapAssert.HasElement(body, "serviceCode", "login");
        SoapAssert.HasElement(body, "serviceVersion", "v1");
    }

    [Fact]
    public async Task GetPeoplePublicInfoAsync_BuildsSoapHeader()
    {
        await using TestServer server = await TestServer.StartAsync("<Envelope />");
        string url = $"http://127.0.0.1:{server.Port}/";
        string tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalGetPeopleTemplate(), Encoding.UTF8);

        FolkRawClient client = new(url, null, TimeSpan.FromSeconds(5), logger: null, verbose: false, maskTokens: false);

        await client.GetPeoplePublicInfoAsync(
            xmlPath: tmp,
            xId: "REQ-9",
            userId: "inspect",
            token: "ABC123TOKEN",
            protocolVersion: "4.0",
            clientXRoadInstance: "FO",
            clientMemberClass: "GOV",
            clientMemberCode: "123",
            clientSubsystemCode: "CLI",
            serviceXRoadInstance: "FO",
            serviceMemberClass: "GOV",
            serviceMemberCode: "321",
            serviceSubsystemCode: "SRV",
            serviceCode: "GetPeoplePublicInfo",
            serviceVersion: "v1",
            ct: default);

        await server.DisposeAsync();
        string body = ExtractBody(server.CapturedRequest);

        SoapAssert.HasElement(body, "userId", "inspect");
        SoapAssert.HasElement(body, "id", "REQ-9");
        SoapAssert.HasElement(body, "serviceCode", "GetPeoplePublicInfo");
        SoapAssert.HasElement(body, "serviceVersion", "v1");
    }

    [Fact]
    public async Task GetPersonAsync_BuildsSoapHeader()
    {
        await using TestServer server = await TestServer.StartAsync("<Envelope />");
        string url = $"http://127.0.0.1:{server.Port}/";
        string tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalGetPersonTemplate(), Encoding.UTF8);

        FolkRawClient client = new(url, null, TimeSpan.FromSeconds(5), logger: null, verbose: false, maskTokens: false);

        await client.GetPersonAsync(
            xmlPath: tmp,
            xId: "REQ-1",
            userId: "test-user",
            token: "TKN",
            protocolVersion: "4.0",
            clientXRoadInstance: "EE",
            clientMemberClass: "GOV",
            clientMemberCode: "1234567",
            clientSubsystemCode: "CLI",
            serviceXRoadInstance: "EE",
            serviceMemberClass: "GOV",
            serviceMemberCode: "7654321",
            serviceSubsystemCode: "SRV",
            serviceCode: "GetPerson",
            serviceVersion: "v1",
            publicId: "PID-1",
            ct: default);

        await server.DisposeAsync();
        string body = ExtractBody(server.CapturedRequest);

        SoapAssert.HasElement(body, "userId", "test-user");
        SoapAssert.HasElement(body, "id", "REQ-1");
        SoapAssert.HasElement(body, "serviceCode", "GetPerson");
        SoapAssert.HasElement(body, "serviceVersion", "v1");
    }
}

