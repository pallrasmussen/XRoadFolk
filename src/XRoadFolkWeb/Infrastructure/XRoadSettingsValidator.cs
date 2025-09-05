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
            if (options is null)
            {
                return ValidateOptionsResult.Fail("XRoad: configuration is required.");
            }

            var errors = new List<string>(16);
            ValidateBaseUrl(options, errors);
            ValidateHeaders(options, errors);
            ValidateClient(options, errors);
            ValidateService(options, errors);
            ValidateAuth(options, errors);
            ValidateTokenMode(options, errors);
            ValidateCertificates(options, errors);

            if (errors.Count > 0)
            {
                return ValidateOptionsResult.Fail(errors);
            }
            return ValidateOptionsResult.Success;
        }

        private static void ValidateBaseUrl(XRoadSettings options, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                errors.Add("XRoad:BaseUrl must be configured.");
            }
            else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"XRoad:BaseUrl '{options.BaseUrl}' is not a valid absolute URI.");
            }
        }

        private static void ValidateHeaders(XRoadSettings options, List<string> errors)
        {
            if (options.Headers is null || string.IsNullOrWhiteSpace(options.Headers.ProtocolVersion))
            {
                errors.Add("XRoad:Headers:ProtocolVersion must be configured.");
            }
        }

        private static void ValidateClient(XRoadSettings options, List<string> errors)
        {
            if (options.Client is null)
            {
                errors.Add("XRoad:Client section is missing.");
                return;
            }

            if (string.IsNullOrWhiteSpace(options.Client.XRoadInstance))
            {
                errors.Add("XRoad:Client:XRoadInstance must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Client.MemberClass))
            {
                errors.Add("XRoad:Client:MemberClass must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Client.MemberCode))
            {
                errors.Add("XRoad:Client:MemberCode must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Client.SubsystemCode))
            {
                errors.Add("XRoad:Client:SubsystemCode must be configured.");
            }
        }

        private static void ValidateService(XRoadSettings options, List<string> errors)
        {
            if (options.Service is null)
            {
                errors.Add("XRoad:Service section is missing.");
                return;
            }

            if (string.IsNullOrWhiteSpace(options.Service.XRoadInstance))
            {
                errors.Add("XRoad:Service:XRoadInstance must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Service.MemberClass))
            {
                errors.Add("XRoad:Service:MemberClass must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Service.MemberCode))
            {
                errors.Add("XRoad:Service:MemberCode must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Service.SubsystemCode))
            {
                errors.Add("XRoad:Service:SubsystemCode must be configured.");
            }
            if (string.IsNullOrWhiteSpace(options.Service.ServiceCode))
            {
                errors.Add("XRoad:Service:ServiceCode must be configured.");
            }
        }

        private static void ValidateAuth(XRoadSettings options, List<string> errors)
        {
            if (options.Auth is null || string.IsNullOrWhiteSpace(options.Auth.UserId))
            {
                errors.Add("XRoad:Auth:UserId must be configured.");
            }
        }

        private static void ValidateTokenMode(XRoadSettings options, List<string> errors)
        {
            string mode = options.TokenInsert?.Mode ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mode) && !IsValidTokenMode(mode))
            {
                errors.Add($"XRoad:TokenInsert:Mode '{mode}' is invalid. Use 'request' or 'header'.");
            }
        }

        private void ValidateCertificates(XRoadSettings options, List<string> errors)
        {
            bool requireClientCert = !_env.IsDevelopment();
            string? pfx = options.Certificate?.PfxPath;
            string? pemCert = options.Certificate?.PemCertPath;
            string? pemKey = options.Certificate?.PemKeyPath;

            if (requireClientCert && string.IsNullOrWhiteSpace(pfx) && (string.IsNullOrWhiteSpace(pemCert) || string.IsNullOrWhiteSpace(pemKey)))
            {
                errors.Add("XRoad:Certificate must configure either PfxPath or both PemCertPath and PemKeyPath in non-Development environments.");
            }

            EnsureFileExists(pfx, "XRoad:Certificate:PfxPath", errors);
            EnsureFileExists(pemCert, "XRoad:Certificate:PemCertPath", errors);
            EnsureFileExists(pemKey, "XRoad:Certificate:PemKeyPath", errors);
        }

        private static void EnsureFileExists(string? path, string key, List<string> errors)
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

        private static bool IsValidTokenMode(string mode)
        {
            return mode.Equals("request", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("header", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("RequestElement", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("Header", StringComparison.OrdinalIgnoreCase);
        }
    }
}
