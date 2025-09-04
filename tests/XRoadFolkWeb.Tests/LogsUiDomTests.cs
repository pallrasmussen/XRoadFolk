using System.Threading.Tasks;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class LogsUiDomTests
{
    [Fact]
    public async Task LogsViewer_Has_Table_And_Controls()
    {
        var html = await System.IO.File.ReadAllTextAsync("src/XRoadFolkWeb/Pages/Shared/_LogsViewer.cshtml");
        Assert.Contains("id=\"logs-table\"", html);
        Assert.Contains("id=\"logs-clear\"", html);
        Assert.Contains("id=\"logs-pause\"", html);
        Assert.Contains("id=\"logs-filter\"", html);
        Assert.Contains("id=\"logs-level\"", html);
    }
}
