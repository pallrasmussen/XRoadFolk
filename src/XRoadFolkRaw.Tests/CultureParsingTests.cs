using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using XRoadFolkRaw.Lib;
using Xunit;

public class CultureParsingTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void TryParseDob_UsesInvariantCulture(string cultureName)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

            bool ok = InputValidation.TryParseDob("01/02/1990", out DateTimeOffset? dob);
            Assert.True(ok);
            Assert.Equal(new DateTimeOffset(1990, 1, 2, 0, 0, 0, TimeSpan.Zero), dob);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public async Task RefreshAsync_ParsesExpirationInvariant(string cultureName)
    {
        string xml = "<root><token>abc</token><expires>01/02/2025 03:04:05 +00:00</expires></root>";
        using FolkRawClient client = new("https://example.com");
        FolkTokenProviderRaw provider = new(client, _ => Task.FromResult(xml));

        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

            await provider.GetTokenAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }

        FieldInfo? field = typeof(FolkTokenProviderRaw).GetField("_expiresUtc", BindingFlags.NonPublic | BindingFlags.Instance);
        DateTimeOffset expires = (DateTimeOffset)field!.GetValue(provider)!;
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero), expires);
    }
}
