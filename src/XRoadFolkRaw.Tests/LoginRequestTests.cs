using System.Net.Sockets;
using System.Net;
using System.Text;
using Xunit;
using XRoadFolkRaw.Lib;

public class LoginRequestTests
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
                read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                _ = sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
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

                        _ = sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
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

    private static string MinimalLoginTemplate()
    {
        // Must match client expectation: prod:Login with a nested <request> wrapper
        return $@"<?xml version=""1.0""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:prod=""{ProdNs}"">
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
    }

    [Fact]
    public async Task LoginComposesHeaderAndPopulatesCredentials()
    {
        (int port, Task serverTask, Func<string> getReq) = await StartTestServerAsync(@"<Envelope><Body><loginResponse><token>TKN</token><expires>2099-01-01T00:00:00Z</expires></loginResponse></Body></Envelope>");
        string url = $"http://127.0.0.1:{port}/";

        // Template with correct namespace and structure
        string tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalLoginTemplate(), Encoding.UTF8);

        FolkRawClient client = new(url, null, TimeSpan.FromSeconds(5), logger: null, verbose: false, maskTokens: false);

        string respXml = await client.LoginAsync(
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

        await serverTask;
        string req = getReq();
        string body = ExtractBody(req);
        string bodyLower = body.ToLowerInvariant();

        // Assert credentials placed in request
        Assert.Contains("<username>bob</username>", bodyLower);
        Assert.Contains("<password>hunter2</password>", bodyLower);

        // Service headers (case-insensitive check)
        Assert.Contains("servicecode>login", bodyLower);
        Assert.Contains("serviceversion>v1", bodyLower);

        // Returned XML should contain token
        Assert.Contains("<token>tkn</token>", respXml.ToLowerInvariant());
    }
}
