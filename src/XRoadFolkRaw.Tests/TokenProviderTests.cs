using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class TokenProviderTests
{
    private static string MakeLoginXml(string token, DateTimeOffset? expiresUtc)
    {
        var exp = expiresUtc?.ToString("O") ?? DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
        return $@"<Envelope>
  <Body>
    <loginResponse>
      <token>{token}</token>
      <expires>{exp}</expires>
    </loginResponse>
  </Body>
</Envelope>";
    }

    [Fact]
    public async Task Returns_Same_Token_Without_Refresh()
    {
        var calls = 0;
        string Login(CancellationToken ct)
        {
            calls++;
            return MakeLoginXml("TKN1", DateTimeOffset.UtcNow.AddSeconds(5));
        }

        var client = new FolkRawClient("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        var provider = new FolkTokenProviderRaw(client, ct => Task.FromResult(Login(ct)));

        var t1 = await provider.GetTokenAsync();
        var t2 = await provider.GetTokenAsync();

        Assert.Equal("TKN1", t1);
        Assert.Equal("TKN1", t2);
        Assert.Equal(1, calls);
        client.Dispose();
    }

    [Fact]
    public async Task Refreshes_Token_After_Expiry()
    {
        var calls = 0;
        string Login(CancellationToken ct)
        {
            calls++;
            if (calls == 1)
                return MakeLoginXml("OLD", DateTimeOffset.UtcNow.AddSeconds(1));
            return MakeLoginXml("NEW", DateTimeOffset.UtcNow.AddMinutes(1));
        }

        var client = new FolkRawClient("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        var provider = new FolkTokenProviderRaw(client, ct => Task.FromResult(Login(ct)));

        var t1 = await provider.GetTokenAsync();
        await Task.Delay(1200);
        var t2 = await provider.GetTokenAsync();

        Assert.Equal("OLD", t1);
        Assert.Equal("NEW", t2);
        Assert.True(calls >= 2);
        client.Dispose();
    }

    [Fact]
    public async Task Concurrent_Requests_Cause_Single_Login()
    {
        var calls = 0;
        async Task<string> LoginAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(150, ct);
            return MakeLoginXml("TKN-ALL", DateTimeOffset.UtcNow.AddSeconds(5));
        }

        var client = new FolkRawClient("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        var provider = new FolkTokenProviderRaw(client, LoginAsync);

        var tasks = new Task<string>[8];
        for (int i = 0; i < tasks.Length; i++)
            tasks[i] = provider.GetTokenAsync();

        var tokens = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(tokens, t => Assert.Equal("TKN-ALL", t));
        client.Dispose();
    }
}