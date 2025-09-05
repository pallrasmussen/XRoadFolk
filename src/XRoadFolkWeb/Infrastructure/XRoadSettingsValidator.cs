using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class XRoadSettingsValidator : IValidateOptions<XRoadSettings>
    {
        private readonly IHostEnvironment _env;
        private readonly IStringLocalizer<XRoadFolkRaw.Lib.ConfigurationLoader> _loc;
        private readonly IConfiguration _config;

        // Backward-compatible overload for tests
        public XRoadSettingsValidator(IHostEnvironment env)
            : this(env, new PassthroughLocalizer(), new ConfigurationBuilder().Build())
        {
        }

        public XRoadSettingsValidator(IHostEnvironment env, IStringLocalizer<XRoadFolkRaw.Lib.ConfigurationLoader> loc, IConfiguration config)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public ValidateOptionsResult Validate(string? name, XRoadSettings options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("XRoad: configuration is required.");
            }

            bool requireClientCert = !_env.IsDevelopment();
            List<string> errors = ConfigurationLoader.ValidateSettings(options, _config, _loc, requireClientCert);

            // Also verify referenced files exist within the web app base path
            ProbeFiles(options, errors);

            if (errors.Count > 0)
            {
                return ValidateOptionsResult.Fail(errors);
            }
            return ValidateOptionsResult.Success;
        }

        private static void ProbeFiles(XRoadSettings options, List<string> errors)
        {
            void EnsureFileExists(string? path, string key)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }
                string probe = path;
                if (!Path.IsPathRooted(probe))
                {
                    probe = Path.Combine(AppContext.BaseDirectory, probe);
                }
                if (!File.Exists(probe))
                {
                    errors.Add($"{key} file not found: '{path}'.");
                }
            }

            EnsureFileExists(options.Certificate?.PfxPath, "XRoad:Certificate:PfxPath");
            EnsureFileExists(options.Certificate?.PemCertPath, "XRoad:Certificate:PemCertPath");
            EnsureFileExists(options.Certificate?.PemKeyPath, "XRoad:Certificate:PemKeyPath");
        }

        private sealed class PassthroughLocalizer : IStringLocalizer<XRoadFolkRaw.Lib.ConfigurationLoader>
        {
            public LocalizedString this[string name]
                => new(name, name, resourceNotFound: true);

            public LocalizedString this[string name, params object[] arguments]
                => new(name, string.Format(CultureInfo.CurrentCulture, name, arguments), resourceNotFound: true);

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
                => Array.Empty<LocalizedString>();

            public IStringLocalizer WithCulture(CultureInfo culture)
                => this;
        }
    }
}
