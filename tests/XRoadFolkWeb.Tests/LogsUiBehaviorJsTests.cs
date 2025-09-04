using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class LogsUiBehaviorJsTests
{
    private static string RepoFile(params string[] parts)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }.Concat(parts).ToArray());
        return Path.GetFullPath(path);
    }

    private static async Task<string> ReadViewerAsync()
    {
        var file = RepoFile("src","XRoadFolkWeb","Pages","Shared","_LogsViewer.cshtml");
        return await System.IO.File.ReadAllTextAsync(file);
    }

    [Fact]
    public async Task Contains_SSE_Reconnect_Logic()
    {
        var html = await ReadViewerAsync();
        Assert.Contains("new EventSource('/logs/stream?", html);
        Assert.Contains("es.onerror = function()", html);
        Assert.Contains("setTimeout(connect, 1000)", html);
    }

    [Fact]
    public async Task Contains_Trim_Behavior_And_Limits()
    {
        var html = await ReadViewerAsync();
        Assert.Contains("var MAX_ROWS = 2000", html);
        Assert.Contains("var TRIM_TO_ROWS = 1500", html);
        Assert.Contains("function trimIfNeeded()", html);
        Assert.Contains("tbody.removeChild(tbody.firstChild)", html);
    }

    [Fact]
    public async Task Batching_And_Autoscroll_Present()
    {
        var html = await ReadViewerAsync();
        Assert.Contains("var BATCH_FLUSH_MS =", html);
        Assert.Contains("document.createDocumentFragment()", html);
        Assert.Contains("tableContainer.scrollTop = tableContainer.scrollHeight", html);
    }
}
