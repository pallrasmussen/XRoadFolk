using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Extensions;
using Xunit;

namespace XRoadFolkWeb.Tests.Session
{
    public class SessionOptionsTests
    {
        private static (IServiceProvider Sp, IOptions<SessionOptions> Opts) BuildWithConfig(params (string Key, string? Value)[] pairs)
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                // Minimal valid Localization to pass validation-on-start
                ["Localization:DefaultCulture"] = "en-US",
                ["Localization:SupportedCultures:0"] = "en-US",
            };
            foreach (var (k, v) in pairs)
            {
                dict[k] = v;
            }

            IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            var services = new ServiceCollection();
            services.AddApplicationServices(cfg);
            IServiceProvider sp = services.BuildServiceProvider(validateScopes: true);
            var opts = sp.GetRequiredService<IOptions<SessionOptions>>();
            return (sp, opts);
        }

        [Fact]
        public void Defaults_Are_Lax_Secure_And_NotEssential()
        {
            (_, var opts) = BuildWithConfig();
            var o = opts.Value;
            o.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
            o.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
            o.Cookie.HttpOnly.Should().BeTrue();
            o.Cookie.IsEssential.Should().BeFalse();
        }

        [Fact]
        public void Invalid_Enum_And_Bool_Values_Fall_Back_To_Defaults()
        {
            (_, var opts) = BuildWithConfig(
                ("Session:Cookie:SameSite", "NotAValue"),
                ("Session:Cookie:SecurePolicy", "Nope"),
                ("Session:Cookie:HttpOnly", "banana"),
                ("Session:Cookie:IsEssential", "nope")
            );

            var o = opts.Value;
            o.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
            o.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
            o.Cookie.HttpOnly.Should().BeTrue();
            o.Cookie.IsEssential.Should().BeFalse();
        }

        [Fact]
        public void IdleTimeout_Parses_And_Clamps_When_Negative()
        {
            (_, var opts) = BuildWithConfig(("Session:IdleTimeoutMinutes", "-3"));
            opts.Value.IdleTimeout.Should().Be(TimeSpan.FromMinutes(1));
        }

        [Fact]
        public void IdleTimeout_Defaults_When_Invalid_String()
        {
            (_, var opts) = BuildWithConfig(("Session:IdleTimeoutMinutes", "abc"));
            opts.Value.IdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
        }
    }
}
