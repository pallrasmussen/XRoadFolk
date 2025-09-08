# Logging

## Overview
The application uses a custom in-memory log provider (InMemoryHttpLogLoggerProvider) that classifies logs into three kinds:
- http
- soap
- app

Logs are stored in a ring buffer (capacity from HttpLogs:Capacity) and optionally persisted to a rolling file when HttpLogs:PersistToFile is true and a FilePath is configured.

A broadcast feed (ILogFeed) streams new log entries to connected Server-Sent Events clients (/logs/stream) and the UI logs viewer.

Sensitive data is sanitized (PII/tokens) via the PiiSanitizer and SafeSoapLogger helpers.

## Classification Heuristics
A log entry becomes `soap` if its EventId matches SafeSoapLogger events. It becomes `http` if:
- Category contains HttpClient or System.Net.Http
- OR the message starts with "HTTP"
Otherwise it defaults to `app`.

## Rate Limiting & Backpressure
The store applies:
- Optional rate limit (MaxWritesPerSecond). Debug/Trace entries may be dropped first unless AlwaysAllowWarningsAndErrors is false.
- An ingestion channel with DropOldest behavior to bound memory.
- Capacity-based eviction (oldest entries removed when over Capacity).

Counters (OpenTelemetry Metrics):
- logs.dropped
- logs.dropped.reason {reason=rate|backpressure|capacity}
- logs.dropped.level
- logs.queue.length (gauge)

## File Persistence
When enabled, entries are formatted as JSON lines with fields (timestamp, level, kind, category, message, exception...) and rolled using size + MaxRolls. Writes are batched with a flush interval.

## Readiness Delay
Health:ReadinessDelaySeconds can defer ready state to avoid startup probing during JIT warmup.

## HTTP Logs in Production
Explicit overrides required because System.* categories default to Warning. See appsettings.Production.json for examples.

## Adding New Kinds
Extend ComputeKind inside the SinkLogger class (InMemoryHttpLog). Add new heuristics before the final return.

## SSE Stream
Each subscriber gets a bounded channel; old messages drop if the client is slow. The UI automatically reconnects.

## Masking / Sanitization
Outside Development, tokens and sensitive markers are masked. SOAP payloads sanitized via SafeSoapLogger; general messages via PiiSanitizer before persistence/stream.

## Useful Configuration Keys
```jsonc
"HttpLogs": {
  "Capacity": 1000,
  "MaxWritesPerSecond": 200,
  "AlwaysAllowWarningsAndErrors": true,
  "PersistToFile": false,
  "FilePath": "logs/http-logs.log",
  "MaxFileBytes": 5000000,
  "MaxRolls": 3,
  "MaxQueue": 5000,
  "FlushIntervalMs": 250
}
```

## Troubleshooting
1. No HTTP logs: add System.Net.Http at Information.
2. High drops: check rate vs MaxWritesPerSecond, enlarge MaxQueue.
3. Performance: reduce Capacity or disable file persistence.
4. PII visible in Production: ensure Logging:MaskTokens true (default) and not running in Development.

See also /docs/configuration.md for full settings.
