using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace XRoadFolkRaw.Tests.Helpers
{
    public sealed class TestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _responseBody;
        private readonly Task _serverTask;
        private string _captured = string.Empty;

        public int Port { get; }

        private TestServer(TcpListener listener, int port, Task serverTask, string responseBody)
        {
            _listener = listener;
            Port = port;
            _serverTask = serverTask;
            _responseBody = responseBody;
        }

        public static async Task<TestServer> StartAsync(string responseBody)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var stream = client.GetStream();

                var buffer = new byte[8192];
                int read;
                var sb = new StringBuilder();
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    var text = sb.ToString();
                    var headEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headEnd >= 0)
                    {
                        // read exact body length if Content-Length present
                        var headers = text.Substring(0, headEnd);
                        var bodyStart = headEnd + 4;
                        if (TryGetContentLength(headers, out var len))
                        {
                            while (text.Length - bodyStart < len)
                            {
                                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                                if (read <= 0) break;
                                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                                text = sb.ToString();
                            }
                        }
                        break;
                    }
                }

                // Capture request
                _ = sb.ToString(); // keep for clarity
                var captured = sb.ToString();
                // Store in object
                // (we can't set instance field here yet, so we will update after creating instance)
                _capturedBacking = captured;

                var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
                var headersOut = "HTTP/1.1 200 OK\r\n" +
                                 "Content-Type: text/xml; charset=utf-8\r\n" +
                                 $"Content-Length: {bodyBytes.Length}\r\n" +
                                 "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headersOut)).ConfigureAwait(false);
                await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                listener.Stop();
            });

            var server = new TestServer(listener, port, serverTask, responseBody);
            // connect captured backbuffer
            _capturedBackingTarget = server;
            return server;
        }

        // hack to pass captured string from static serverTask closure into instance
        private static string _capturedBacking = string.Empty;
        private static TestServer? _capturedBackingTarget;
        public string CapturedRequest => _captured;

        private static bool TryGetContentLength(string headers, out int len)
        {
            len = 0;
            foreach (var line in headers.Split(new[]{ "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = line.Substring("Content-Length:".Length).Trim();
                    return int.TryParse(v, out len);
                }
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            try { await _serverTask.ConfigureAwait(false); }
            catch { /* ignore */ }
            _listener.Stop();
            // lift captured into instance when completed
            _captured = _capturedBacking;
            _capturedBacking = string.Empty;
            _capturedBackingTarget = null;
        }
    }
}
