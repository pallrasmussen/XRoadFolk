using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using XRoad.Config;

public static class CertLoader
{
    public static X509Certificate2 LoadFromConfig(CertificateSettings cfg)
    {
        var envPfx  = Environment.GetEnvironmentVariable("XR_PFX_PATH");
        var envPwd  = Environment.GetEnvironmentVariable("XR_PFX_PASSWORD");
        var envPemC = Environment.GetEnvironmentVariable("XR_PEM_CERT_PATH");
        var envPemK = Environment.GetEnvironmentVariable("XR_PEM_KEY_PATH");

        if (!string.IsNullOrWhiteSpace(envPfx)) return LoadFromPfx(envPfx!, envPwd ?? cfg.PfxPassword);
        if (!string.IsNullOrWhiteSpace(envPemC) && !string.IsNullOrWhiteSpace(envPemK)) return LoadFromPem(envPemC!, envPemK!);
        if (!string.IsNullOrWhiteSpace(cfg.PfxPath)) return LoadFromPfx(cfg.PfxPath!, cfg.PfxPassword);
        if (!string.IsNullOrWhiteSpace(cfg.PemCertPath) && !string.IsNullOrWhiteSpace(cfg.PemKeyPath)) return LoadFromPem(cfg.PemCertPath!, cfg.PemKeyPath!);
        throw new InvalidOperationException("No certificate configured.");
    }
    public static X509Certificate2 LoadFromPfx(string path, string? password = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"PFX not found '{path}'", path);
        return new X509Certificate2(path, password ?? string.Empty, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }
    public static X509Certificate2 LoadFromPem(string certPath, string keyPath)
    {
        if (!File.Exists(certPath)) throw new FileNotFoundException($"PEM cert not found '{certPath}'", certPath);
        if (!File.Exists(keyPath))  throw new FileNotFoundException($"PEM key not found '{keyPath}'",  keyPath);
        var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        return new X509Certificate2(cert.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }
}