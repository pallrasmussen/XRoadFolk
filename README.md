# XRoadFolk

## Project Goals
XRoadFolk is a .NET console application and supporting library for interacting with X-Road services. It demonstrates how to perform raw requests against a Folk registry service and display the results in a simple console UI.

## Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/) is required to build and run the solution.
- Access to a valid X-Road environment and client certificate (PFX or PEM).

## Configuration
Application settings are read from `src/XRoadFolkRaw/appsettings.json` with optional overrides from `appsettings.Development.json` or `appsettings.Production.json`. Use the following fields to configure your X-Road endpoint, credentials, and client details. Environment variables can override JSON settings:

### Certificate Configuration
Provide a client certificate either as a single PFX file or as a PEM certificate/key pair.
- JSON settings: `XRoad:Certificate:PfxPath`, `XRoad:Certificate:PfxPassword`, `XRoad:Certificate:PemCertPath`, `XRoad:Certificate:PemKeyPath`.
- Environment variables: `XR_PFX_PATH`, `XR_PFX_PASSWORD`, `XR_PEM_CERT_PATH`, `XR_PEM_KEY_PATH`.

### Environment Variables
- `XR_BASE_URL` – base URL for X-Road requests.
- `XR_USER` / `XR_PASSWORD` – credentials used for authentication.
- `XR_PFX_PATH` / `XR_PFX_PASSWORD` – PFX certificate path and password.
- `XR_PEM_CERT_PATH` / `XR_PEM_KEY_PATH` – PEM certificate and key paths.
- `DOTNET_ENVIRONMENT` – select which appsettings file to use (e.g. `Development`, `Production`).

### Localization
Specify the UI culture by setting `Localization:Culture` in `appsettings.json` (e.g. `en-US`, `fr-FR`). This culture is applied at startup for date and number formatting.

## Building and Running
Restore dependencies and run the console application:
```bash
dotnet run --project src/XRoadFolkRaw
```
Press `Ctrl+Q` in the console to exit.

## Running Tests
Execute the test suite:
```bash
dotnet test src/XRoadFolkRaw.Tests
```

## External Resources
- [X-Road Documentation](https://x-road.global/documentation)
- [X-Road GitHub Repository](https://github.com/nordic-institute/X-Road)
