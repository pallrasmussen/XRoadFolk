using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using XRoadFolkWeb.Pages;
using Xunit;

namespace XRoadFolkRaw.Tests;

public class IndexModelLocalizationTests
{
    [Theory]
    [InlineData("en-US", " (Name)")]
    [InlineData("da-DK", " (Name)")]
    [InlineData("fo-FO", " (Name)")]
    public void SelectedNameSuffixFormat_ComesFromPageResources(string culture, string expected)
    {
        var services = new ServiceCollection();
        services.AddLocalization(opts => opts.ResourcesPath = "Resources");
        using var provider = services.BuildServiceProvider();

        var localizer = provider.GetRequiredService<IStringLocalizer<IndexModel>>();

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            LocalizedString str = localizer["SelectedNameSuffixFormat", "Name"];    
            Assert.False(str.ResourceNotFound);
            Assert.Equal(expected, str.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
