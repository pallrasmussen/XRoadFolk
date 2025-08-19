using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;

public class LoginRequestTests
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
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var current = sb.ToString();
                var headEnd = current.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headEnd >= 0)
                {
                    var headers = current.Substring(0, headEnd);
                    var bodyStart = headEnd + 4;
                    int contentLen = 0;
                    foreach (var line in headers.Split(new[]{ "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
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
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
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
    public async Task Login_Composes_Header_And_Populates_Credentials()
    {
        var (port, serverTask, getReq) = await StartTestServerAsync(@"<Envelope><Body><loginResponse><token>TKN</token><expires>2099-01-01T00:00:00Z</expires></loginResponse></Body></Envelope>");
        var url = $"http://127.0.0.1:{port}/";

        // Template with correct namespace and structure
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, MinimalLoginTemplate(), Encoding.UTF8);

        var client = new FolkRawClient(url, null, TimeSpan.FromSeconds(5), logger: null, verbose: false, maskTokens: false);

        var respXml = await client.LoginAsync(
            loginXmlPath: tmp,
            xId: "LOGIN-1",
            userId: "tester",
            username: "bob",
            password: "hunter2",
            protocolVersion: "4.0",
            clientXRoadInstance: "EE",
            clientMemberClass:   "GOV",
            clientMemberCode:    "1234567",
            clientSubsystemCode: "MY-SUBSYS",
            serviceXRoadInstance: "EE",
            serviceMemberClass:   "GOV",
            serviceMemberCode:    "7654321",
            serviceSubsystemCode: "TARGET-SUBSYS",
            serviceCode:          "login",
            serviceVersion:       "v1",
            ct: default);

        await serverTask;
        var req = getReq();
        var body = ExtractBody(req);
        var bodyLower = body.ToLowerInvariant();

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
