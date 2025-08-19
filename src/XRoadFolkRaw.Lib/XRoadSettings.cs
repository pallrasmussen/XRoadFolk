namespace XRoad.Config
{
    public sealed class XRoadSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public HttpSettings Http { get; set; } = new();
        public CertificateSettings Certificate { get; set; } = new();
        public HeaderSettings Headers { get; set; } = new();
        public ClientIdSettings Client { get; set; } = new();
        public ServiceIdSettings Service { get; set; } = new();
        public AuthSettings Auth { get; set; } = new();
        public RawSettings Raw { get; set; } = new();
        public TokenInsertSettings TokenInsert { get; set; } = new();
    }
    public sealed class HttpSettings { public int TimeoutSeconds { get; set; } = 60; }
    public sealed class CertificateSettings { public string? PfxPath { get; set; } public string? PfxPassword { get; set; } public string? PemCertPath { get; set; } public string? PemKeyPath { get; set; } }
    public sealed class HeaderSettings { public string ProtocolVersion { get; set; } = "4.0"; }
    public sealed class ClientIdSettings { public string XRoadInstance { get; set; } = ""; public string MemberClass { get; set; } = ""; public string MemberCode { get; set; } = ""; public string SubsystemCode { get; set; } = ""; }
    public sealed class ServiceIdSettings { public string XRoadInstance { get; set; } = ""; public string MemberClass { get; set; } = ""; public string MemberCode { get; set; } = ""; public string SubsystemCode { get; set; } = ""; public string ServiceCode { get; set; } = "Login"; public string? ServiceVersion { get; set; } = "v1"; }
    public sealed class AuthSettings { public string UserId { get; set; } = ""; public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
    public sealed class RawSettings { public string LoginXmlPath { get; set; } = "Login.xml"; }
    public sealed class TokenInsertSettings { public string Mode { get; set; } = "RequestElement"; public string ElementLocalName { get; set; } = "token"; public string ParentLocalName { get; set; } = "request"; public bool CreateIfMissing { get; set; } = true; }
}