using Xunit;
using XRoadFolkRaw.Lib;

public class TokenProviderTests
{
    private static string MakeLoginXml(string token, DateTimeOffset? expiresUtc)
    {
        string exp = expiresUtc?.ToString("O") ?? DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
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
    public async Task ReturnsSameTokenWithoutRefresh()
    {
        int calls = 0;
        string Login(CancellationToken ct)
        {
            calls++;
            return MakeLoginXml("TKN1", DateTimeOffset.UtcNow.AddSeconds(5));
        }

        FolkRawClient client = new("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        FolkTokenProviderRaw provider = new(client, ct => Task.FromResult(Login(ct)));

        string t1 = await provider.GetTokenAsync();
        string t2 = await provider.GetTokenAsync();

        Assert.Equal("TKN1", t1);
        Assert.Equal("TKN1", t2);
        Assert.Equal(1, calls);
        client.Dispose();
    }

    [Fact]
    public async Task RefreshesTokenAfterExpiry()
    {
        int calls = 0;
        string Login(CancellationToken ct)
        {
            calls++;
            if (calls == 1)
            {
                return MakeLoginXml("OLD", DateTimeOffset.UtcNow.AddSeconds(1));
            }

            return MakeLoginXml("NEW", DateTimeOffset.UtcNow.AddMinutes(1));
        }

        FolkRawClient client = new("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        FolkTokenProviderRaw provider = new(client, ct => Task.FromResult(Login(ct)));

        string t1 = await provider.GetTokenAsync();
        await Task.Delay(1200);
        string t2 = await provider.GetTokenAsync();

        Assert.Equal("OLD", t1);
        Assert.Equal("NEW", t2);
        Assert.True(calls >= 2);
        client.Dispose();
    }

    [Fact]
    public async Task ConcurrentRequestsCauseSingleLogin()
    {
        int calls = 0;
        async Task<string> LoginAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(150, ct);
            return MakeLoginXml("TKN-ALL", DateTimeOffset.UtcNow.AddSeconds(5));
        }

        FolkRawClient client = new("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        FolkTokenProviderRaw provider = new(client, LoginAsync);

        Task<string>[] tasks = new Task<string>[8];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = provider.GetTokenAsync();
        }

        string[] tokens = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(tokens, t => Assert.Equal("TKN-ALL", t));
        client.Dispose();
    }

    [Fact]
    public async Task FailedLoginIsClearedForNextAttempt()
    {
        int calls = 0;
        async Task<string> LoginAsync(CancellationToken ct)
        {
            calls++;
            if (calls == 1)
            {
                await Task.Delay(1, ct);
                throw new InvalidOperationException("nope");
            }

            return MakeLoginXml("OK", DateTimeOffset.UtcNow.AddMinutes(1));
        }

        FolkRawClient client = new("http://example/", null, TimeSpan.FromSeconds(10), null, false, true);
        FolkTokenProviderRaw provider = new(client, LoginAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync());
        string token = await provider.GetTokenAsync();

        Assert.Equal("OK", token);
        Assert.Equal(2, calls);
        client.Dispose();
    }
}