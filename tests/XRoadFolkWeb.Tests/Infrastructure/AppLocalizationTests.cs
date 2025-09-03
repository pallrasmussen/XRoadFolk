using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Infrastructure;
using Xunit;

namespace XRoadFolkWeb.Tests.Infrastructure
{
    public class AppLocalizationTests
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> data)
        {
            return new ConfigurationBuilder().AddInMemoryCollection(data!).Build();
        }

        [Fact]
        public void FromConfiguration_UsesFallbacks_WhenSectionMissing()
        {
            IConfiguration cfg = BuildConfig(new());
            (string def, IReadOnlyList<CultureInfo> list) = AppLocalization.FromConfiguration(cfg, NullLogger.Instance);
            def.Should().NotBeNullOrWhiteSpace();
            list.Should().NotBeEmpty();
            list.Select(c => c.Name).Should().Contain(new[] { "fo-FO", "da-DK", "en-US" });
        }

        [Fact]
        public void FromConfiguration_Deduplicates_CaseInsensitive_AndPreservesOrder()
        {
            IConfiguration cfg = BuildConfig(new()
            {
                ["Localization:DefaultCulture"] = "en-US",
                ["Localization:SupportedCultures:0"] = "en-us",
                ["Localization:SupportedCultures:1"] = "EN-US",
                ["Localization:SupportedCultures:2"] = "da-DK",
                ["Localization:SupportedCultures:3"] = "fo-FO",
                ["Localization:SupportedCultures:4"] = "da-dk",
            });

            (string def, IReadOnlyList<CultureInfo> list) = AppLocalization.FromConfiguration(cfg, NullLogger.Instance);
            def.Should().Be("en-US");
            list.Select(c => c.Name).Should().ContainInOrder(new[] { "en-US", "da-DK", "fo-FO" });
            list.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void FromConfiguration_Skips_Invalid_Cultures_And_FallsBack_When_All_Invalid()
        {
            IConfiguration cfg = BuildConfig(new()
            {
                ["Localization:DefaultCulture"] = "zz-ZZ",
                ["Localization:SupportedCultures:0"] = "bad-culture",
                ["Localization:SupportedCultures:1"] = "also-bad",
                ["Localization:SupportedCultures:2"] = "en-US",
            });

            (string def, IReadOnlyList<CultureInfo> list) = AppLocalization.FromConfiguration(cfg, NullLogger.Instance);
            def.Should().Be("en-US");
            list.Select(c => c.Name).Should().Contain(new[] { "en-US" });

            // now make them all invalid
            IConfiguration cfgAllBad = BuildConfig(new()
            {
                ["Localization:DefaultCulture"] = "zz-ZZ",
                ["Localization:SupportedCultures:0"] = "bad-culture",
            });
            (string def2, IReadOnlyList<CultureInfo> list2) = AppLocalization.FromConfiguration(cfgAllBad, NullLogger.Instance);
            def2.Should().Be("fo-FO");
            list2.Select(c => c.Name).Should().Contain(new[] { "fo-FO", "da-DK", "en-US" });
        }

        [Fact]
        public void FromConfiguration_Default_Not_In_List_FallsBack_To_First()
        {
            IConfiguration cfg = BuildConfig(new()
            {
                ["Localization:DefaultCulture"] = "zz-ZZ",
                ["Localization:SupportedCultures:0"] = "da-DK",
                ["Localization:SupportedCultures:1"] = "en-US",
            });

            (string def, IReadOnlyList<CultureInfo> list) = AppLocalization.FromConfiguration(cfg, NullLogger.Instance);
            def.Should().Be("da-DK");
            list.Select(c => c.Name).Should().ContainInOrder(new[] { "da-DK", "en-US" });
        }
    }
}
