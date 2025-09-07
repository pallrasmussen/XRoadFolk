using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    // Simple smoke test ensuring localizer resolves known keys in default culture.
    public class LocalizationResourcesAnalyzerSmokeTests
    {
        [Fact]
        public void SharedResource_Has_Expected_Keys()
        {
            var asm = typeof(XRoadFolkWeb.Shared.SharedResource).Assembly;
            var factory = new ResourceManagerStringLocalizerFactory(
                Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()),
                new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());
            var loc = factory.Create(typeof(XRoadFolkWeb.Shared.SharedResource));

            string[] keys = new[]
            {
                "AppName","Summary","RawXml","PrettyXml","Copy","Copied","Download",
                "PersonDetails","NoBasics","NoNames","NoAddresses","NoForeignSsns","NoJuridicalParents","NoData"
            };

            foreach (var k in keys)
            {
                var v = loc[k];
                v.ResourceNotFound.Should().BeFalse($"Missing resource key: {k}");
                v.Name.Should().Be(k);
                v.Value.Should().NotBeNullOrWhiteSpace();
            }
        }
    }
}
