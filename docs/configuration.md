# Configuration

## Sources
- appsettings.json (base)
- appsettings.{Environment}.json
- Environment variables (colon notation -> __ in shells)
- User secrets (Development)

## Key Sections
### XRoad
BaseUrl, headers, client/service identifiers, authentication, certificate paths.

### Localization
DefaultCulture, SupportedCultures, FallbackMap.

### Features
- ShowLogs: show/hide Logs navigation.
- Logs:Enabled: expose endpoints (/logs, /logs/stream).
- ResponseViewer: ShowRawXml, ShowPrettyXml (forced off in Production).

### HttpLogs
Controls in-memory log ring and persistence.

### Logging
MaskTokens, Verbose, per-category levels. Add System.Net.Http for production HTTP visibility.

### Health
ReadinessDelaySeconds (defer readiness probe success).

### Session
Cookie settings (IsEssential, SameSite, SecurePolicy), IdleTimeoutMinutes, Store (InMemory | Redis | SqlServer) and backend-specific settings.

### DataProtection
ApplicationName, KeysDirectory, optional certificate for key encryption (non-Windows).

### Retry:Http
Attempts, Backoff, TimeoutMs for outbound calls (via Polly handler).

### OpenTelemetry
Metrics and exporters: Prometheus, Console, Otlp. Enable/disable AspNetCore, Runtime instrumentation.

## Environment Variable Examples
```
XR_BASE_URL=https://example/xroad
XR_USER=serviceuser
XR_PASSWORD=secret
XR_PFX_PATH=/certs/client.pfx
XR_PFX_PASSWORD=changeit
HttpLogs__PersistToFile=true
HttpLogs__FilePath=/logs/http-logs.log
Logging__LogLevel__System.Net.Http=Information
Health__ReadinessDelaySeconds=20
```

## Certificate Expiry Warning
XRoad:Certificate:WarnIfExpiresInDays (default 30) triggers warning banner + log entries. Banner appears when missing, expired, or within threshold.

## Security Headers
CSP with nonce, Referrer-Policy, X-Frame-Options=DENY, Permissions-Policy hardened list. Adjust only if new external resources required.

## Localization Fallback Chain
Cookie -> Accept-Language (q-values) -> Exact match -> FallbackMap -> Parent culture -> Same-language.

## Recommended Production Overrides
```
Logging:LogLevel:System=Warning
Logging:LogLevel:System.Net.Http=Information
Features:ShowLogs=false
ResponseViewer:* = false
```

## Session Store Selection
Set Session:Store to Redis or SqlServer and supply connection settings. Default InMemory is single-instance only.

See /docs/logging.md for logging internals.
