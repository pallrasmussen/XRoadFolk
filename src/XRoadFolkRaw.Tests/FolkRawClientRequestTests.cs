using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using XRoadFolkRaw.Lib;

public class FolkRawClientRequestTests
{
    private static async Task<(int Port, Task ServerTask, Func<string> GetCapturedRequest)> StartTestServerAsync(string responseBody)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string captured = "";
        async Task Server()
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();

            byte[] buffer = new byte[8192];
            int read;
            StringBuilder sb = new();
            do
            {
                read = await stream.ReadAsync(buffer);
                if (read <= 0)
                {
                    break;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                string current = sb.ToString();
                int headEnd = current.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headEnd >= 0)
                {
                    string headers = current.Substring(0, headEnd);
                    int bodyStart = headEnd + 4;
                    int contentLen = 0;
                    foreach (string line in headers.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring("Content-Length:".Length).Trim();
                            int.TryParse(v, out contentLen);
                        }
                    }
                    while (current.Length - bodyStart < contentLen)
                    {
                        read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0)
                        {
                            break;
                        }

                        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        current = sb.ToString();
                    }
                    break;
                }
            } while (true);

            captured = sb.ToString();

            byte[] bodyBytes = Encoding.UTF8.GetBytes(responseBody);
            string headersOut = "HTTP/1.1 200 OK\r\n" +
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
        int idx = rawHttp.IndexOf("\r\n\r\n", StringComparison.Ordinal);
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
    public async Task GetPeoplePublicInfoInsertsTokenAndServiceCode()
    {
        (int port, Task serverTask, Func<string> getReq) = await StartTestServerAsync(@"<Envelope><Body><GetPeoplePublicInfoResponse><ok>true</ok></GetPeoplePublicInfoResponse></Body></Envelope>");
        string url = $"http://127.0.0.1:{port}/";

        string tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalGetPeopleTemplate(), Encoding.UTF8);

        FolkRawClient client = new(url, null, TimeSpan.FromSeconds(5), logger: null, verbose: false, maskTokens: false);

        string xml = await client.GetPeoplePublicInfoAsync(
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

        await serverTask;
        string req = getReq();

        Assert.Contains("Content-Type: text/xml", req, StringComparison.OrdinalIgnoreCase);

        string body = ExtractBody(req);

        // prefix-agnostic body checks
        Assert.Contains("serviceCode>GetPeoplePublicInfo", body);
        Assert.Contains("serviceVersion>v1", body);
        Assert.Contains("<token>ABC123TOKEN</token>", body);
        Assert.Contains("<ListOfPersonPublicInfoCriteria>", body);
        Assert.Contains("<PersonPublicInfoCriteria", body); // allow self-closing tag e.g., <... />
    }
}
