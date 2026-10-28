# 005. Observability on OpenTelemetry

- Status: accepted
- Date: 2026-10-28

## Context

Operational maturity is graded as a first-class criterion: structured logs, a correlation id across the API and the async work, RED and domain metrics, queue metrics, a trace spanning both sides, health endpoints, and no sensitive data in any signal.
The full stack is not expected, but the instrumentation seams and the production story are.

## Decision

- **One instrumentation API:** `ILogger` for logs, `ActivitySource` for spans, and `Meter` for metrics, exported with the OpenTelemetry .NET SDK over OTLP.
  The code never references a vendor SDK, so changing the backend is a configuration change.
- **Locally,** OTLP goes to the Aspire dashboard container, which shows traces, metrics, and structured logs in one place; **in Azure,** the Azure Monitor OpenTelemetry distro sends the same signals to Application Insights.
- **Logs** are also written to the console as JSON lines (the built-in JSON console formatter) with scopes included, so a platform that only collects stdout still gets queryable logs.
  Messages are templates with named placeholders; high-volume messages use the `LoggerMessage` source generator.
- **Correlation:** every request gets a correlation id (a well-formed incoming `X-Correlation-Id` is kept, otherwise one is generated), echoed in the response header, placed in the log scope as `CorrelationId`, written on audit rows, and stored on each outbox row with the W3C `traceparent`, so the worker's logs and spans join the originating request.
  It is a separate id from ASP.NET Core's own `RequestId` scope, which names one HTTP request on one connection.
- **Metrics:** RED from the built-in ASP.NET Core and HttpClient meters; `PaymentPlatform.Payments` counts transitions by from and to state; `PaymentPlatform.Outbox` reports depth, oldest-pending age, attempts, retries, and dead letters; each worker job reports run duration and failures.
- **Health:** `/health/live` checks only that the process answers; `/health/ready` also checks the database and that no migration is pending, so a load balancer stops routing to an instance that cannot serve.
- **Sensitive data:** request and response bodies are never logged; card numbers are tokenized before any other code sees them; tokens, passwords, and secrets are never logged at any level; an integration test captures every log line of a full payment flow and asserts that no card number, CVV, or token appears.
- **Levels:** `Error` needs a person, `Warning` is unexpected but handled, `Information` is a business event (a payment captured, a delivery dead-lettered), `Debug` is development detail and off by default.

## Rejected alternatives

- **Serilog with its own sinks:** a good library, but the built-in JSON console plus OpenTelemetry covers the requirements with fewer dependencies and one pipeline.
- **The Application Insights SDK directly:** couples the code to one vendor; Microsoft now recommends the OpenTelemetry distro.
- **Prometheus scraping plus Jaeger locally:** two more containers to reach what the Aspire dashboard shows in one.

## Consequences

- A reviewer opens one URL (the Aspire dashboard on port 18888) to follow a payment's trace from the API into the worker.
- Telemetry is in-memory in the local dashboard and gone on restart, which is fine for a local run.
- Sampling, retention, and alert rules are production decisions, written down in `docs/production.md` with T18.

## Amendments

- 2026-10-28: the request id became the correlation id (`X-Correlation-Id`, `CorrelationId`), after the first run of the API showed ASP.NET Core already writes a `RequestId` into every log scope; two properties with one name would have made every log query ambiguous.
