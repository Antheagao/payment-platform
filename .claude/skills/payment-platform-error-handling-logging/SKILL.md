---
name: payment-platform-error-handling-logging
description: Payment Platform logging, tracing, metrics, health, and request id facts for .NET, plus the error rules translated from Rust. Use when adding a log line, a span, a meter, a health check, or an error path in this repository.
---

# Payment Platform error-handling-logging overlay

Load the global `error-handling-logging` guide for the rules; its Next.js section applies to `frontend/` unchanged.
ADR 005 (observability) and ADR 006 (problem details) come first; this file says what exists and how the Rust rules translate.

## What exists (`backend/src/PaymentPlatform.ServiceDefaults`)

- `AddServiceDefaults()` clears the default providers and installs the JSON console formatter (UTC timestamps, scopes included) plus OpenTelemetry logging, with `TraceId`, `SpanId`, and `ParentId` added to every scope.
- OpenTelemetry traces and metrics: every `ActivitySource` and `Meter` named `PaymentPlatform.*`, ASP.NET Core (health probes filtered out of traces), HttpClient, and runtime instrumentation; OTLP export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `UseRequestIds()` (`RequestIdMiddleware`, rules in `RequestIds`): keeps a caller's `X-Request-Id` of 1 to 64 characters from `[A-Za-z0-9-_.:]`, otherwise generates 32 hex characters; sets `HttpContext.TraceIdentifier`, echoes the header, tags the span `request.id`, and opens a `RequestId` log scope.
  It must be the first middleware, so every later line carries the id.
- `MapDefaultEndpoints()`: `/health/live` runs only checks tagged `live` (the built-in `self`); `/health/ready` runs all; `HealthResponseWriter` returns status, per-check status, and duration, never a description or exception.
- Tests: `tests/PaymentPlatform.ServiceDefaults.Tests` (request id rules, header injection refused, health output leaks nothing).

## Translations from the Rust guide

- `tracing::info!(field = ..., "msg")` becomes a message template with PascalCase placeholders: `logger.LogInformation("Payment {PaymentId} captured", id)`; never string interpolation in a log call (CA2254).
- Messages logged on a hot path or more than once use the `[LoggerMessage]` source generator in a `static partial class Log` beside the code.
- `anyhow` context becomes a typed result for expected failures and an exception only for the unexpected; the API's exception handler logs the cause once at `Error` and returns `internal_error`.
- `RUST_LOG` becomes `Logging:LogLevel` in `appsettings.json` or `Logging__LogLevel__Default` in the environment.
- New spans come from a static `ActivitySource` named `PaymentPlatform.<Project>`; new metrics from a `Meter` of the same name, created through `IMeterFactory`.

## Levels and what never goes in a log

- `Error` a person must look; `Warning` unexpected but handled; `Information` a business event; `Debug` development detail, off by default.
- Never: card numbers, CVV, tokens, passwords, password hashes, signing keys, connection strings, `Authorization` or `Cookie` headers, or request and response bodies.
- Log ids (`PaymentId`, `MerchantId`, user id), never emails at `Information` or above.

## Known gaps

- Problem details and the exception handler land with the API host (T8); the log-capture test for card data lands with T20.
- SQL client instrumentation lands with Persistence (T6); worker job spans and the outbox trace hop with T9 and T23.
