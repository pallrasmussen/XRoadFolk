using System;
using System.IO;
using System.Linq;
using Xunit;

public class AppArtifactsTests
{
    private static string? FindSolutionRoot(string startDir)
    {
        // Walk up to 8 levels to find a folder containing XRoadFolk.sln or "src" folder
        var dir = new DirectoryInfo(startDir);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "XRoadFolk.sln")))
                return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
        }
        return null;
    }

    private static string? FindFileUnder(string root, string fileName, string? mustContain = null)
    {
        foreach (var path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            if (mustContain == null || path.Replace('\\','/').Contains(mustContain.Replace('\\','/'), StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    [Fact]
    public void Templates_Exist_In_Source_Tree()
    {
        var cwd = Directory.GetCurrentDirectory();
        var root = FindSolutionRoot(cwd) ?? cwd;

        // Try source tree first
        var srcLogin = Path.Combine(root, "src", "XRoadFolkRaw", "Login.xml");
        var srcGetPeople = Path.Combine(root, "src", "XRoadFolkRaw", "GetPeoplePublicInfo.xml");

        // If not found, search anywhere under root (CI/build agents may place things differently)
        if (!File.Exists(srcLogin))
            srcLogin = FindFileUnder(root, "Login.xml", "XRoadFolkRaw") ?? srcLogin;
        if (!File.Exists(srcGetPeople))
            srcGetPeople = FindFileUnder(root, "GetPeoplePublicInfo.xml", "XRoadFolkRaw") ?? srcGetPeople;

        Assert.True(File.Exists(srcLogin) && File.Exists(srcGetPeople),
            $"Could not find required XML templates.\n" +
            $"Searched root: {root}\n" +
            $"Login.xml candidate: {srcLogin}\n" +
            $"GetPeoplePublicInfo.xml candidate: {srcGetPeople}");
    }
}
