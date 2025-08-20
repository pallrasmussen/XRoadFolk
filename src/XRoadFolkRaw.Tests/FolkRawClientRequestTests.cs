using System.Net.Sockets;
using System.Net;
using System.Text;
using Xunit;

public class FolkRawClientRequestTests
{
    private static async Task<(int Port, Task ServerTask, Func<string> GetCapturedRequest)> StartTestServerAsync(string responseBody)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string captured = "";
        async Task Server()
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();

            var buffer = new byte[8192];
            int read;
            var sb = new StringBuilder();
            do
            {
                read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                _ = sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var current = sb.ToString();
                var headEnd = current.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headEnd >= 0)
                {
                    var headers = current.Substring(0, headEnd);
                    var bodyStart = headEnd + 4;
                    int contentLen = 0;
                    foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            var v = line.Substring("Content-Length:".Length).Trim();
                            int.TryParse(v, out contentLen);
                        }
                    }
                    while (current.Length - bodyStart < contentLen)
                    {
                        read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        _ = sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        current = sb.ToString();
                    }
                    break;
                }
            } while (true);

            captured = sb.ToString();

            var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
            var headersOut = "HTTP/1.1 200 OK\r\n" +
                             "Content-Type: text/xml; charset=utf-8\r\n" +
                             $"Content-Length: {bodyBytes.Length}\r\n" +
                             "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headersOut));
            await stream.WriteAsync(bodyBytes);
            await stream.FlushAsync();
            listener.Stop();
        }

        return (port, Server(), () => captured);
    }

    private static string ExtractBody(string rawHttp)
    {
        var idx = rawHttp.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return idx >= 0 ? rawHttp.Substring(idx + 4) : rawHttp;
    }

    private const string ProdNs = "http://us-folk-v2.x-road.eu/producer";

    private static string MinimalGetPeopleTemplate()
    {
        return $@"<?xml version=""1.0""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:prod=""{ProdNs}"">
  <soapenv:Header />
  <soapenv:Body>
    <prod:GetPeoplePublicInfo>
      <request>
        <requestHeader/>
        <requestBody>
          <ListOfPersonPublicInfoCriteria>
            <PersonPublicInfoCriteria/>
          </ListOfPersonPublicInfoCriteria>
        </requestBody>
      </request>
    </prod:GetPeoplePublicInfo>
  </soapenv:Body>
</soapenv:Envelope>";
    }

    [Fact]
    public async Task GetPeoplePublicInfo_Inserts_Token_And_ServiceCode()
    {
        var (port, serverTask, getReq) = await StartTestServerAsync(@"<Envelope><Body><GetPeoplePublicInfoResponse><ok>true</ok></GetPeoplePublicInfoResponse></Body></Envelope>");
        var url = $"http://127.0.0.1:{port}/";

        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalGetPeopleTemplate(), Encoding.UTF8);

        var client = new FolkRawClient(url, null, TimeSpan.FromSeconds(5), logger:null, verbose:false, maskTokens:false);

        var xml = await client.GetPeoplePublicInfoAsync(
            xmlPath: tmp,
            xId: "REQ-9",
            userId: "inspect",
            token: "ABC123TOKEN",
            protocolVersion: "4.0",
            clientXRoadInstance: "FO",
            clientMemberClass:   "GOV",
            clientMemberCode:    "123",
            clientSubsystemCode: "CLI",
            serviceXRoadInstance: "FO",
            serviceMemberClass:   "GOV",
            serviceMemberCode:    "321",
            serviceSubsystemCode: "SRV",
            serviceCode:          "GetPeoplePublicInfo",
            serviceVersion:       "v1",
            ct: default);

        await serverTask;
        var req = getReq();

        Assert.Contains("Content-Type: text/xml", req, StringComparison.OrdinalIgnoreCase);

        var body = ExtractBody(req);

        // prefix-agnostic body checks
        Assert.Contains("serviceCode>GetPeoplePublicInfo", body);
        Assert.Contains("serviceVersion>v1", body);
        Assert.Contains("<token>ABC123TOKEN</token>", body);
        Assert.Contains("<ListOfPersonPublicInfoCriteria>", body);
        Assert.Contains("<PersonPublicInfoCriteria", body); // allow self-closing tag e.g., <... />
    }
}
