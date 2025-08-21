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
            return !File.Exists(path)
                ? throw new FileNotFoundException($"PFX not found '{path}'", path)
                : new X509Certificate2(path, password ?? string.Empty, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
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

            X509Certificate2 cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
    }
}