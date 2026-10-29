---
name: payment-platform-error-handling-logging
description: Payment Platform logging, tracing, metrics, health, and correlation id facts for .NET, plus the error rules translated from Rust. Use when adding a log line, a span, a meter, a health check, or an error path in this repository.
---

# Payment Platform error-handling-logging overlay

Load the global `error-handling-logging` guide for the rules; its Next.js section applies to `frontend/` unchanged.
ADR 005 (observability) and ADR 006 (problem details) come first; this file says what exists and how the Rust rules translate.

## What exists (`backend/src/PaymentPlatform.ServiceDefaults`)

- `AddServiceDefaults()` clears the default providers and installs the JSON console formatter (UTC timestamps, scopes included) plus OpenTelemetry logging, with `TraceId`, `SpanId`, and `ParentId` added to every scope.
- OpenTelemetry traces and metrics: every `ActivitySource` and `Meter` named `PaymentPlatform.*`, ASP.NET Core (health probes left out of traces and, through `DisableHttpMetrics()`, out of `http.server.request.duration`), HttpClient, SQL client, and runtime instrumentation; OTLP export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; the `TraceSampling` sampler, parent-based, drops a root span tagged `sampling.priority` `0` with its children, which is how a job that runs every second (`PeriodicJob.TraceRuns => false`) stays out of the trace list while the work it does for a request is traced under that request (ADR 005, 2026-10-29).
- `UseCorrelationIds()` (`CorrelationIdMiddleware`, rules in `CorrelationIds`): keeps a caller's `X-Correlation-Id` of 1 to 64 characters from `[A-Za-z0-9-_.:]`, otherwise generates 32 hex characters; stores it for `HttpContext.GetCorrelationId()`, echoes the header, tags the span `correlation.id`, and opens a `CorrelationId` log scope.
  It must be the first middleware, so every later line carries the id.
  ASP.NET Core's own `RequestId` scope (connection id plus a counter) is a different thing and stays as it is; `CorrelationIds.BeginScope` is how the worker restores an id from an outbox row.
- `MapDefaultEndpoints()`: `/health/live` runs only checks tagged `live` (the built-in `self`); `/health/ready` runs all; both answer only `GET` and `HEAD`; `HealthResponseWriter` returns status, per-check status, and duration, never a description or exception.
- Checks log nothing themselves: the health check service logs each unhealthy result once at `Error` with the description and exception the check returns.
- Tests: `tests/PaymentPlatform.ServiceDefaults.Tests` (correlation id rules, header injection refused, health output leaks nothing).

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

- Metrics (R19, R20): `Persistence/PaymentMetrics` (`payments.transitions`, fed by `PaymentTransitionInterceptor` once a save commits and only for rows recorded as news, plus settlement's batch counts) and `Application/Webhooks/QueueMetrics` (`webhook.delivery.attempts` by `webhook.outcome`, and gauges over the last `MeasureQueues` snapshot, which the worker takes every 10 s; a host that never measures reports nothing). Tests read them with `MetricCollector` on the host's own `IMeterFactory`, so no other host is heard; a gauge's singleton must exist before a test can show it reports nothing.
- No alert rules exist locally; production.md names the ones that page a person (outbox age over 60 s, any dead letter, a settlement failure), and the gauges above are what they read.
- `Microsoft.AspNetCore.DataProtection` is at `Error` in the API's `appsettings.json`: authentication registers Data Protection, which warns in a container about unpersisted, unencrypted keys, but the API protects nothing with it (tokens are signed with the JWT key). If the API ever protects data (cookies, antiforgery), persist the keys to Blob Storage and protect them with Key Vault instead.
- `scripts/check-stack.sh` fails when the API or the worker logs anything at `Warning` or above before "Application started", so CI catches warnings that only appear in the Linux container (the Data Protection ones never appear on Windows).
- The health check service logs every unhealthy probe at `Error`, so an outage logs once per probe per instance; acceptable while alerting keys on readiness, revisit when alerts are wired (production.md).
