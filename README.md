# XRoadFolk (Razor Pages)

## Overview
XRoadFolk is a .NET 8 web application built with ASP.NET Core Razor Pages. It provides a UI for querying people data via X-Road SOAP services using a shared library.

- Web UI: src/XRoadFolkWeb
- Shared client library: src/XRoadFolkRaw.Lib (SOAP client, options, logging)

The older console UI referenced in prior versions has been removed. Use the Razor Pages app.

## Prerequisites
- .NET 8 SDK
- Access to an X-Road endpoint and client certificate (PFX or PEM) if required

## Quick start
From the repository root:

```bash
# run the web app
dotnet run --project src/XRoadFolkWeb/XRoadFolkWeb.csproj
```

Open http://localhost:5000 or https://localhost:7000 (default Kestrel ports).

## Configuration
The web app loads default X‑Road settings from the library at startup, then applies appsettings and environment overrides.

- Appsettings: src/XRoadFolkWeb/appsettings.json (+ appsettings.Development.json)
- Important sections:
  - XRoad: base URL, client/service identifiers, headers, auth, certificate
  - Operations:GetPeoplePublicInfo:XmlPath (template path)
  - Operations:GetPerson:XmlPath (template path)
  - Operations:GetPerson:Request
    - Include (boolean switches) or Include flags via GetPersonRequestOptions.Include
  - Localization (DefaultCulture, SupportedCultures, optional FallbackMap)
  - Logging (MaskTokens, Verbose)

Environment variable overrides (examples):
- XR_BASE_URL, XR_USER, XR_PASSWORD
- XR_PFX_PATH / XR_PFX_PASSWORD or XR_PEM_CERT_PATH / XR_PEM_KEY_PATH
- DOTNET_ENVIRONMENT=Development

## Features
- People search (SSN or First/Last + Date of Birth)
- Detail panel via GetPerson with pretty-printed XML
- Centralized include configuration helper merges boolean Include keys and flags
- Request logging (Development) with static-asset filtering and PII redaction
- SOAP logging sanitizer (SafeSoapLogger) masks secrets/tokens/SSN-like values
- Localization with culture switch endpoint

## Diagnostics (Development only)
- GET /__culture – shows configured and applied cultures
- Logs endpoints (require IHttpLogStore):
  - GET /logs?kind=http|soap|app
  - POST /logs/clear
  - POST /logs/write
  - GET /logs/stream (Server-Sent Events)

## Dependency Injection
- PeopleService encapsulates X‑Road calls and token handling
- PeopleResponseParser (singleton) parses/pretty-prints XML responses

## Certificates
Configure under XRoad:Certificate in appsettings (PFX or PEM). In Development, server certificate validation can be bypassed when enabled in configuration.

## Build and test
```bash
# build all
dotnet build

# run web app
dotnet run --project src/XRoadFolkWeb/XRoadFolkWeb.csproj

# run tests (if present)
dotnet test
```

## Repository layout
- src/XRoadFolkWeb – Razor Pages app (Pages, Extensions, Features, Infrastructure)
- src/XRoadFolkRaw.Lib – SOAP client, options, logging, configuration helpers

## Notes
The console application previously located under src/XRoadFolkRaw is no longer part of this solution. All usage is via the Razor Pages app.

# XRoadFolk

Razor Pages app with X-Road SOAP client integration.

This document summarizes key operational configuration and expectations.

## Session, Cookies, and Consent

Session is enabled with consent-friendly defaults. By default, `Session:Cookie:IsEssential` is `false` and must be explicitly set to `true` when you have a legal basis or obtained consent.

- Configure in `appsettings.json` (or environment):
  ```json
  "Session": {
    "Cookie": {
      "IsEssential": false,
      "HttpOnly": true,
      "SameSite": "Strict",
      "SecurePolicy": "Always",
      "Name": ".XRoadFolk.Session"
    },
    "IdleTimeoutMinutes": 30,
    "Store": "InMemory" // or "Redis" or "SqlServer"
  }
  ```
- Stores:
  - InMemory: simple, not shared across instances. Recommended only for single-node or dev.
  - Redis: set `Session:Redis:Configuration` and optional `Session:Redis:InstanceName`.
  - SqlServer: set `Session:SqlServer:ConnectionString` and optional `SchemaName`/`TableName`.
- Cookie policy defaults:
  - HttpOnly enforced on all cookies
  - SameSite=Lax by default
  - Secure=Always outside Development; SameAsRequest in Development (supports HTTP TestServer)
- Culture cookie: set for 1 year, Path=/, SameSite=Lax, HttpOnly; Secure only when HTTPS.
- Anti-forgery: Razor Pages validation is globally ignored for regular page posts; the `/set-culture` endpoint is explicitly validated and returns 400 without a token.

## Localization fallback maps and best-match

Localization is driven by the `Localization` section and a best-match culture provider.

- Required keys:
  ```json
  "Localization": {
    "DefaultCulture": "fo-FO",
    "SupportedCultures": ["fo-FO", "da-DK", "en-US"],
    "FallbackMap": { "en": "en-US", "fo": "fo-FO" }
  }
  ```
- Best-match rules in order:
  1. Culture cookie (`.AspNetCore.Culture`) if valid
  2. Accept-Language header (strict parsing, q-values honored, size/entry caps, invalid tags skipped)
  3. Exact supported match
  4. Explicit `FallbackMap` (e.g., neutral `en` → `en-US`)
  5. Parent cultures
  6. Same-language match (e.g., `en-GB` → `en-US`)

## Logging expectations and safety

- Verbosity is controlled under `Logging`. Keep `Default` at `Information` in production.
- SOAP/XML is always sanitized before logging via `SafeSoapLogger`.
  - Masks: username, password, token, userId, common token aliases (sessionId/sessionToken/authToken/accessToken), and WS-Security `BinarySecurityToken`.
- HTTP and app logs feed an in-memory or file-backed store:
  - Messages capped (~8KB) and scope info truncated (~2KB) with depth/kv limits
  - Back-pressure with drop counters and reasons
  - Live Logs UI caps rows; batches updates; auto-scrolls when at bottom

## Health and Metrics

- Health check endpoints: `GET /health/live`, `GET /health/ready`.
- Metrics (System.Diagnostics.Metrics) – scrape with OpenTelemetry by subscribing to meters below.
  - Meter `XRoadFolkRaw`:
    - `xroad.http.retries` (counter) tags: `op`
    - `xroad.http.duration` (histogram, ms) tags: `op`
  - Meter `XRoadFolkWeb`:
    - `logs.queue.length` (observable gauge) tags: `store` (memory|file)
    - `logs.dropped` (counter)
    - `logs.dropped.reason` (counter) tags: `reason` (rate|backpressure), `store` (memory|file)
    - `logs.dropped.level` (counter) tags: `level`, `store`

### OpenTelemetry wiring (Prometheus/OTLP)

The app wires a MeterProvider and exposes both a Prometheus scrape endpoint and an optional OTLP exporter. Enable them via configuration:

```json
"OpenTelemetry": {
  "Metrics": {
    "AspNetCore": true,
    "Runtime": false
  },
  "Exporters": {
    "Prometheus": { "Enabled": true },
    "Otlp": {
      "Enabled": true,
      "Endpoint": "http://otel-collector:4318",
      "Protocol": "http/protobuf" // or "grpc"
    }
  }
}
```

- Prometheus scrape endpoint: GET /metrics (enabled when Exporters:Prometheus:Enabled=true)
- OTLP: set Endpoint to your collector. Supported Protocol values: grpc, http/protobuf.
- Meters included: XRoadFolkRaw, XRoadFolkWeb. You can add ASP.NET Core or runtime metrics via flags above.

## CI/CD

- GitHub Actions workflow builds with analyzers, runs tests with coverage, and publishes the web artifact on `main`/`master`.

## Security headers and CSP

- App sets standard security headers (X-Frame-Options, Referrer-Policy, etc.) and a Content Security Policy with per-request nonce for scripts/styles.

## Notes

- Template/resource loading uses positive/negative caching to avoid repeated IO and noisy logs.
- Accept-Language and XML parsing are hardened against DoS-like inputs.

# XRoadFolk

## Configuration notes

- JSON configuration files (appsettings*.json) do not support comments. Keep comments in this README or separate docs.
- HttpLogs.PersistToFile: Do not enable in production unless there is a strong operational need and storage/retention is controlled.
  - Use environment-specific overrides (e.g., appsettings.Production.json) instead of editing appsettings.json.
- Features.ShowLogs: Should be disabled in production to reduce exposure of log data in the UI.

## Environment-specific configuration

- appsettings.json: Baseline settings shared across all environments.
- appsettings.Development.json: Development-time tweaks.
- appsettings.Production.json: Production overrides. See example committed in this repo.

## How to override locally

- Use dotnet user-secrets for secrets and local-only overrides.
- Environment variables can override any setting using the colon-delimited form, e.g.:
  - ASPNETCORE_ENVIRONMENT=Production
  - HttpLogs__PersistToFile=false
  - Features__ShowLogs=false
