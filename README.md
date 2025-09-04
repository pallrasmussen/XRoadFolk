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

## Session and Cookie Consent

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
- If you set `IsEssential=true`, ensure consent/compliance requirements are met in your jurisdiction.

## Localization mappings and Accept-Language

Localization is driven by `Localization` section and a best-match culture provider.

- Required keys:
  ```json
  "Localization": {
    "DefaultCulture": "fo-FO",
    "SupportedCultures": ["fo-FO", "da-DK", "en-US"],
    "FallbackMap": { "en": "en-US", "fo": "fo-FO" }
  }
  ```
- Best-match rules in order:
  1. Cookie (`.AspNetCore.Culture`) if valid.
  2. Accept-Language header (hardened): strict `q` parsing, invalid tags skipped, caps on total header size and item count.
  3. Exact supported name.
  4. Configured `FallbackMap` (e.g., neutral `en` -> `en-US`).
  5. Parent cultures.
  6. Same language match (e.g., `en-GB` -> `en-US`).

## Logging verbosity expectations and safety

- Verbosity is controlled under `Logging` section. Keep `Default` at `Information` in production unless you need verbose diagnostics.
  ```json
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" },
    "Verbose": false,
    "MaskTokens": true
  }
  ```
- SOAP/XML is always sanitized before logging via `SafeSoapLogger`. Usernames, passwords, and tokens are masked.
- HTTP and app logs feed an in-memory/file-backed store:
  - Scopes are truncated (~2KB) and depth-limited.
  - Individual messages are capped (~8KB) to avoid large allocations.
  - Live Logs UI caps rows and batches updates to keep the page responsive.

## Health and Metrics

- Health check endpoint: `GET /health`.
- Metrics (System.Diagnostics.Metrics):
  - `XRoadFolkRaw` meter:
    - `xroad.http.retries` (counter)
    - `xroad.http.duration` (histogram, ms)
  - `XRoadFolkWeb` meter:
    - `logs.dropped` (counter)
    - `logs.queue.length` (observable gauge)
- Integrate with OpenTelemetry by adding the OTel SDK and exporters to scrape these meters.

## CI/CD

- GitHub Actions workflow builds with analyzers (warnings as errors), runs tests with coverage, and publishes the web artifact on `main`/`master`.

## Security headers and CSP

- App sets standard security headers (X-Frame-Options, Referrer-Policy, etc.) and a Content Security Policy with per-request nonce for scripts/styles.

## Notes

- Template/resource loading uses positive/negative caching to avoid repeated IO and noisy logs.
- Accept-Language and XML parsing are hardened against DoS-like inputs.
