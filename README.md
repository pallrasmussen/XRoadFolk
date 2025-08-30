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
