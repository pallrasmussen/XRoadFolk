using System.Security.Cryptography.X509Certificates;

namespace XRoadFolkRaw.Lib
{
    public static class CertLoader
    {
        public static X509Certificate2 LoadFromConfig(CertificateSettings cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            string? envPfx = Environment.GetEnvironmentVariable("XR_PFX_PATH");
            string? envPwd = Environment.GetEnvironmentVariable("XR_PFX_PASSWORD");
            string? envPemC = Environment.GetEnvironmentVariable("XR_PEM_CERT_PATH");
            string? envPemK = Environment.GetEnvironmentVariable("XR_PEM_KEY_PATH");

            if (!string.IsNullOrWhiteSpace(envPfx))
            {
                string pfxPath = envPfx;
                return LoadFromPfx(pfxPath, envPwd ?? cfg.PfxPassword);
            }

            if (!string.IsNullOrWhiteSpace(envPemC) && !string.IsNullOrWhiteSpace(envPemK))
            {
                string pemCertPath = envPemC;
                string pemKeyPath = envPemK;
                return LoadFromPem(pemCertPath, pemKeyPath);
            }

            if (!string.IsNullOrWhiteSpace(cfg.PfxPath))
            {
                string pfxPath = cfg.PfxPath;
                return LoadFromPfx(pfxPath, cfg.PfxPassword);
            }

            if (!string.IsNullOrWhiteSpace(cfg.PemCertPath) && !string.IsNullOrWhiteSpace(cfg.PemKeyPath))
            {
                string pemCertPath = cfg.PemCertPath;
                string pemKeyPath = cfg.PemKeyPath;
                return LoadFromPem(pemCertPath, pemKeyPath);
            }

            throw new InvalidOperationException("No certificate configured.");
        }

        public static X509Certificate2 LoadFromPfx(string path, string? password = null)
        {
            ArgumentNullException.ThrowIfNull(path);

            // When a relative path is provided, try to resolve it against common base dirs
            string? resolved = ResolvePathCandidates(path);
            if (resolved is null)
            {
                throw new FileNotFoundException($"PFX not found '{path}'", path);
            }

            return new X509Certificate2(resolved, password ?? string.Empty,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        private static string? ResolvePathCandidates(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return File.Exists(path) ? path : null;
            }

            // Try as-given (relative to current working dir)
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            // Try next to the app binaries (bin folder)
            string binPath = Path.Combine(AppContext.BaseDirectory, path);
            if (File.Exists(binPath))
            {
                return binPath;
            }

            // Try under a Resources folder next to the app binaries
            string binRes = Path.Combine(AppContext.BaseDirectory, "Resources", Path.GetFileName(path));
            if (File.Exists(binRes))
            {
                return binRes;
            }

            // Try relative to the current directory explicitly
            string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (File.Exists(cwdPath))
            {
                return cwdPath;
            }

            return null;
        }

        public static X509Certificate2 LoadFromPem(string certPath, string keyPath)
        {
            ArgumentNullException.ThrowIfNull(certPath);
            ArgumentNullException.ThrowIfNull(keyPath);
            if (!File.Exists(certPath))
            {
                throw new FileNotFoundException($"PEM cert not found '{certPath}'", certPath);
            }

            if (!File.Exists(keyPath))
            {
                throw new FileNotFoundException($"PEM key not found '{keyPath}'", keyPath);
            }

            using X509Certificate2 cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            string? password = null; // disambiguate ctor
            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12), password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
    }
}