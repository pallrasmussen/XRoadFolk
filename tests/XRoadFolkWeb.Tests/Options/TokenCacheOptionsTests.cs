using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Extensions;
using Xunit;

namespace XRoadFolkWeb.Tests.Options
{
    public class TokenCacheOptionsTests
    {
        private static (IServiceProvider Sp, IOptions<XRoadFolkRaw.Lib.Options.TokenCacheOptions> Opts) BuildWithConfig(params (string Key, string? Value)[] pairs)
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                // Minimal valid Localization to satisfy validations configured elsewhere
                ["Localization:DefaultCulture"] = "en-US",
                ["Localization:SupportedCultures:0"] = "en-US",
            };
            foreach (var (k, v) in pairs) dict[k] = v;

            IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            var services = new ServiceCollection();
            services.AddApplicationServices(cfg);
            IServiceProvider sp = services.BuildServiceProvider(validateScopes: true);
            var opts = sp.GetRequiredService<IOptions<XRoadFolkRaw.Lib.Options.TokenCacheOptions>>();
            return (sp, opts);
        }

        [Fact]
        public void Negative_RefreshSkewSeconds_Should_Throw_OptionsValidationException()
        {
            (_, var opts) = BuildWithConfig(("TokenCache:RefreshSkewSeconds", "-1"));
            Action act = () => { var _ = opts.Value; };
            act.Should().Throw<OptionsValidationException>()
               .WithMessage("*RefreshSkewSeconds must be >= 0*");
        }

        [Fact]
        public void DefaultTtlSeconds_Less_Than_1_Should_Throw_OptionsValidationException()
        {
            (_, var opts) = BuildWithConfig(("TokenCache:DefaultTtlSeconds", "0"));
            Action act = () => { var _ = opts.Value; };
            act.Should().Throw<OptionsValidationException>()
               .WithMessage("*DefaultTtlSeconds must be >= 1*");
        }
    }
}
