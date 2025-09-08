using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class HealthChecksWritablePathTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public HealthChecksWritablePathTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    // Configure an unwritable path (root dir or drive root should fail for append in most environments) 
                    var dict = new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["HttpLogs:PersistToFile"] = "true",
                        ["HttpLogs:FilePath"] = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:/", "__forbidden__", "logs", "app.log")
                    };
                    cfg.AddInMemoryCollection(dict);
                });
            });
        }

        [Fact]
        public async Task Ready_Reports_Unhealthy_When_Log_File_Not_Writable()
        {
            using var client = _factory.CreateClient();
            var resp = await client.GetAsync("/health/ready");
            resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
    }
}
