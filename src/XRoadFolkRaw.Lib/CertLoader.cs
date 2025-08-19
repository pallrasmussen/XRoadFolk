using System.Security.Cryptography.X509Certificates;
using XRoad.Config;

namespace XRoadFolkRaw.Lib
{
    public static class CertLoader
    {
        public static X509Certificate2 LoadFromConfig(CertificateSettings cfg)
        {
            string? envPfx = Environment.GetEnvironmentVariable("XR_PFX_PATH");
            string? envPwd = Environment.GetEnvironmentVariable("XR_PFX_PASSWORD");
            string? envPemC = Environment.GetEnvironmentVariable("XR_PEM_CERT_PATH");
            string? envPemK = Environment.GetEnvironmentVariable("XR_PEM_KEY_PATH");

            return !string.IsNullOrWhiteSpace(envPfx)
                ? LoadFromPfx(envPfx, envPwd ?? cfg.PfxPassword)
                : !string.IsNullOrWhiteSpace(envPemC) && !string.IsNullOrWhiteSpace(envPemK)
                ? LoadFromPem(envPemC, envPemK)
                : !string.IsNullOrWhiteSpace(cfg.PfxPath)
                ? LoadFromPfx(cfg.PfxPath!, cfg.PfxPassword)
                : !string.IsNullOrWhiteSpace(cfg.PemCertPath) && !string.IsNullOrWhiteSpace(cfg.PemKeyPath)
                ? LoadFromPem(cfg.PemCertPath!, cfg.PemKeyPath!)
                : throw new InvalidOperationException("No certificate configured.");
        }
        public static X509Certificate2 LoadFromPfx(string path, string? password = null)
        {
            return !File.Exists(path)
                ? throw new FileNotFoundException($"PFX not found '{path}'", path)
                : new X509Certificate2(path, password ?? string.Empty, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
        public static X509Certificate2 LoadFromPem(string certPath, string keyPath)
        {
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