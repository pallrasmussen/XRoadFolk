using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class LogsUiDomTests
{
    private static string RepoFile(params string[] parts)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }.Concat(parts).ToArray());
        return Path.GetFullPath(path);
    }

    [Fact]
    public async Task LogsViewer_Has_Table_And_Controls()
    {
        var file = RepoFile("src","XRoadFolkWeb","Pages","Shared","_LogsViewer.cshtml");
        var html = await System.IO.File.ReadAllTextAsync(file);
        Assert.Contains("id=\"logs-table\"", html);
        Assert.Contains("id=\"logs-clear\"", html);
        Assert.Contains("id=\"logs-pause\"", html);
        Assert.Contains("id=\"logs-filter\"", html);
        Assert.Contains("id=\"logs-level\"", html);
    }
}
