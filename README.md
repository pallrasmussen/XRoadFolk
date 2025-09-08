# XRoadFolk (Razor Pages)

> Concise overview of the application. Detailed logging & configuration docs moved to /docs.

## Overview
XRoadFolk is a .NET 8 ASP.NET Core Razor Pages application providing a UI over X-Road SOAP services (people lookup & details). It uses a shared library (XRoadFolkRaw.Lib) for SOAP request construction, sanitization, retry logic, and token handling.

## Key Features
- People search & detailed person viewer (sanitized XML display)
- In-memory tri-kind logging (http | soap | app) with optional rolling file persistence
- Live logs viewer (SSE stream) + filtering / pagination
- Localization (culture fallback & mapping) and theme switching
- Certificate expiry pre-check with UI banner and warning logs
- Configurable readiness delay for health probes
- OpenTelemetry metrics & tracing (optional exporters: Prometheus, OTLP, Console)
- Hardened security headers + CSP with nonces; PII/token masking outside Development

## Quick Start
```bash
dotnet run --project src/XRoadFolkWeb/XRoadFolkWeb.csproj
```
Open http://localhost:5000 (HTTP) or https://localhost:7000 (HTTPS).

## Architecture (Logging Path)
```
+-------------------+       +-------------------------+       +------------------+
| ILogger calls     |  -->  | InMemoryHttpLogLogger   |  -->  | IHttpLogStore     |
|  (app / http /    |       |  (classification +      |       |  (ring buffer +   |
|   soap events)    |       |   scope enrichment)     |       |   optional file)  |
+-------------------+       +-------------------------+       +---------+--------+
                                                                       |
                                                                       v
                                                             +---------------------+
                                                             | ILogFeed / SSE      |
                                                             |  (LogStreamBroad-   |
                                                             |   caster -> /logs/  |
                                                             |   stream clients)   |
                                                             +----------+----------+
                                                                        |
                                                                        v
                                                            +----------------------+
                                                            | Browser Logs Viewer  |
                                                            |  (filter, paginate,  |
                                                            |   cards/table views) |
                                                            +----------------------+
```
Kinds: soap (SOAP EventIds), http (HttpClient/System.Net.Http or message starts with HTTP), else app.

## Health Endpoints
- /health/live – liveness (quick)
- /health/ready – readiness (includes optional startup delay & log file writable check)

## Certificate Expiry Banner
Displays warning when missing, expired, or within XRoad:Certificate:WarnIfExpiresInDays (default 30 days).

## Configuration & Logging Docs
- Detailed configuration: [docs/configuration.md](docs/configuration.md)
- Logging architecture & tuning: [docs/logging.md](docs/logging.md)

## Minimal Production Tips
```jsonc
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "System": "Warning",
    "System.Net.Http": "Information"
  }
},
"Features": { "ShowLogs": false },
"Health": { "ReadinessDelaySeconds": 15 }
```
Add System.Net.Http to surface HTTP diagnostics; disable ShowLogs if exposing logs externally is not desired.

## Localization
Fallback order: Cookie -> Accept-Language (q) -> Exact -> FallbackMap -> Parent -> Same-language.

## Security Highlights
- CSP with per-request nonce (scripts & styles)
- Referrer-Policy: no-referrer; X-Frame-Options: DENY
- Permissions-Policy hardened allowlist
- Token & PII masking outside Development

## Repository Layout
- src/XRoadFolkWeb – Razor Pages UI (Pages, Infrastructure, Extensions)
- src/XRoadFolkRaw.Lib – SOAP client, sanitizers, retry, configuration helpers
- tests/XRoadFolkWeb.Tests – integration & unit tests (logging, culture fallback, sanitizer, health, classification)
- docs/ – extended documentation

## License / Author
See repository metadata for license. Author: @pallrasmussen (Branding:AuthorName configurable).

---
For deeper details consult the docs above. Contributions welcome; prefer PRs with tests for new logging or configuration behaviors.
