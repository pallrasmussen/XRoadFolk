using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class XRoadSettingsValidator : IValidateOptions<XRoadSettings>
    {
        private readonly IHostEnvironment _env;

        public XRoadSettingsValidator(IHostEnvironment env)
        {
            _env = env;
        }

        public ValidateOptionsResult Validate(string? name, XRoadSettings options)
        {
            if (options is null) return ValidateOptionsResult.Fail("XRoad: configuration is required.");

            var errors = new List<string>(8);

            // Base URL
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                errors.Add("XRoad:BaseUrl must be configured.");
            }
            else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"XRoad:BaseUrl '{options.BaseUrl}' is not a valid absolute URI.");
            }

            // Headers
            if (options.Headers is null || string.IsNullOrWhiteSpace(options.Headers.ProtocolVersion))
            {
                errors.Add("XRoad:Headers:ProtocolVersion must be configured.");
            }

            // Client
            if (options.Client is null)
            {
                errors.Add("XRoad:Client section is missing.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.Client.XRoadInstance)) errors.Add("XRoad:Client:XRoadInstance must be configured.");
                if (string.IsNullOrWhiteSpace(options.Client.MemberClass)) errors.Add("XRoad:Client:MemberClass must be configured.");
                if (string.IsNullOrWhiteSpace(options.Client.MemberCode)) errors.Add("XRoad:Client:MemberCode must be configured.");
                if (string.IsNullOrWhiteSpace(options.Client.SubsystemCode)) errors.Add("XRoad:Client:SubsystemCode must be configured.");
            }

            // Service
            if (options.Service is null)
            {
                errors.Add("XRoad:Service section is missing.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.Service.XRoadInstance)) errors.Add("XRoad:Service:XRoadInstance must be configured.");
                if (string.IsNullOrWhiteSpace(options.Service.MemberClass)) errors.Add("XRoad:Service:MemberClass must be configured.");
                if (string.IsNullOrWhiteSpace(options.Service.MemberCode)) errors.Add("XRoad:Service:MemberCode must be configured.");
                if (string.IsNullOrWhiteSpace(options.Service.SubsystemCode)) errors.Add("XRoad:Service:SubsystemCode must be configured.");
                if (string.IsNullOrWhiteSpace(options.Service.ServiceCode)) errors.Add("XRoad:Service:ServiceCode must be configured.");
            }

            // Auth
            if (options.Auth is null || string.IsNullOrWhiteSpace(options.Auth.UserId))
            {
                errors.Add("XRoad:Auth:UserId must be configured.");
            }

            // TokenInsert mode sanity (accept common values)
            string mode = options.TokenInsert?.Mode ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (!IsValidTokenMode(mode))
                {
                    errors.Add($"XRoad:TokenInsert:Mode '{mode}' is invalid. Use 'request' or 'header'.");
                }
            }

            // Certificate requirement: in non-development, require either PFX or PEM pair
            bool requireClientCert = !_env.IsDevelopment();
            string? pfx = options.Certificate?.PfxPath;
            string? pemCert = options.Certificate?.PemCertPath;
            string? pemKey = options.Certificate?.PemKeyPath;

            if (requireClientCert && string.IsNullOrWhiteSpace(pfx) && (string.IsNullOrWhiteSpace(pemCert) || string.IsNullOrWhiteSpace(pemKey)))
            {
                errors.Add("XRoad:Certificate must configure either PfxPath or both PemCertPath and PemKeyPath in non-Development environments.");
            }

            // If any path is provided, require it to exist
            void EnsureFileExists(string? path, string key)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
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

            EnsureFileExists(pfx, "XRoad:Certificate:PfxPath");
            EnsureFileExists(pemCert, "XRoad:Certificate:PemCertPath");
            EnsureFileExists(pemKey, "XRoad:Certificate:PemKeyPath");

            if (errors.Count > 0)
            {
                return ValidateOptionsResult.Fail(errors);
            }
            return ValidateOptionsResult.Success;
        }

        private static bool IsValidTokenMode(string mode)
        {
            return mode.Equals("request", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("header", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("RequestElement", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("Header", StringComparison.OrdinalIgnoreCase);
        }
    }
}
