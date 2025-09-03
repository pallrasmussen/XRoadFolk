namespace XRoadFolkRaw.Lib
{
    public sealed class XRoadSettings
    {
        /// <summary>
        /// Base URL for X-Road endpoints.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// HTTP client related settings.
        /// </summary>
        public HttpSettings Http { get; set; } = new();

        /// <summary>
        /// Certificate configuration for TLS authentication.
        /// </summary>
        public CertificateSettings Certificate { get; set; } = new();

        /// <summary>
        /// SOAP header related settings.
        /// </summary>
        public HeaderSettings Headers { get; set; } = new();

        /// <summary>
        /// Client identifier information.
        /// </summary>
        public ClientIdSettings Client { get; set; } = new();

        /// <summary>
        /// Service identifier information.
        /// </summary>
        public ServiceIdSettings Service { get; set; } = new();

        /// <summary>
        /// Credentials used for authentication.
        /// </summary>
        public AuthSettings Auth { get; set; } = new();

        /// <summary>
        /// Raw payload configuration.
        /// </summary>
        public RawSettings Raw { get; set; } = new();

        /// <summary>
        /// Token insertion behavior.
        /// </summary>
        public TokenInsertSettings TokenInsert { get; set; } = new();
    }

    public sealed class HttpSettings
    {
        /// <summary>
        /// HTTP request timeout in seconds. Default is 60.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Lifetime for pooled connections in seconds. 0 means infinite. Default 300 (5 minutes).
        /// </summary>
        public int PooledConnectionLifetimeSeconds { get; set; } = 300;

        /// <summary>
        /// Idle timeout for pooled connections in seconds. 0 means infinite. Default 120 (2 minutes).
        /// </summary>
        public int PooledConnectionIdleTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Max concurrent connections per server. <= 0 uses default 20.
        /// </summary>
        public int MaxConnectionsPerServer { get; set; } = 20;
    }

    public sealed class CertificateSettings
    {
        /// <summary>
        /// Optional path to a PFX certificate file.
        /// </summary>
        public string? PfxPath { get; set; }

        /// <summary>
        /// Password for the PFX certificate, if required.
        /// </summary>
        public string? PfxPassword { get; set; }

        /// <summary>
        /// Optional path to a PEM certificate file.
        /// </summary>
        public string? PemCertPath { get; set; }

        /// <summary>
        /// Optional path to a PEM key file.
        /// </summary>
        public string? PemKeyPath { get; set; }
    }

    public sealed class HeaderSettings
    {
        /// <summary>
        /// X-Road protocol version. Default is 4.0.
        /// </summary>
        public string ProtocolVersion { get; set; } = "4.0";
    }

    public sealed class ClientIdSettings
    {
        /// <summary>
        /// X-Road instance identifier.
        /// </summary>
        public string XRoadInstance { get; set; } = string.Empty;

        /// <summary>
        /// Member class of the client.
        /// </summary>
        public string MemberClass { get; set; } = string.Empty;

        /// <summary>
        /// Member code of the client.
        /// </summary>
        public string MemberCode { get; set; } = string.Empty;

        /// <summary>
        /// Subsystem code of the client.
        /// </summary>
        public string SubsystemCode { get; set; } = string.Empty;
    }

    public sealed class ServiceIdSettings
    {
        /// <summary>
        /// X-Road instance identifier for the target service.
        /// </summary>
        public string XRoadInstance { get; set; } = string.Empty;

        /// <summary>
        /// Member class providing the service.
        /// </summary>
        public string MemberClass { get; set; } = string.Empty;

        /// <summary>
        /// Member code providing the service.
        /// </summary>
        public string MemberCode { get; set; } = string.Empty;

        /// <summary>
        /// Subsystem code providing the service.
        /// </summary>
        public string SubsystemCode { get; set; } = string.Empty;

        /// <summary>
        /// Service code to call. Default is "Login".
        /// </summary>
        public string ServiceCode { get; set; } = "Login";

        /// <summary>
        /// Optional service version, e.g. "v1".
        /// </summary>
        public string? ServiceVersion { get; set; } = "v1";
    }

    public sealed class AuthSettings
    {
        /// <summary>
        /// External user identifier.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Username for authentication.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Password for authentication.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }

    public sealed class RawSettings
    {
        /// <summary>
        /// Path to the raw login XML template. Default is "Login.xml".
        /// </summary>
        public string LoginXmlPath { get; set; } = "Login.xml";
    }

    public sealed class TokenInsertSettings
    {
        /// <summary>
        /// Token insertion mode, e.g. "RequestElement".
        /// </summary>
        public string Mode { get; set; } = "RequestElement";

        /// <summary>
        /// Local name of the element containing the token.
        /// </summary>
        public string ElementLocalName { get; set; } = "token";

        /// <summary>
        /// Local name of the parent element. Default is "request".
        /// </summary>
        public string ParentLocalName { get; set; } = "request";

        /// <summary>
        /// Whether to create the parent element if it is missing. Default is true.
        /// </summary>
        public bool CreateIfMissing { get; set; } = true;
    }
}
