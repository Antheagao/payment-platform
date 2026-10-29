// Traces from the dashboard's server (ADR 005): every page render and Server Action is a span, and each call to
// the API carries `traceparent`, so a capture from a payment's page is one trace through the API, the outbox, and
// the worker to the webhook. Exported over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (compose: the Aspire
// dashboard), and nowhere otherwise.
import { registerOTel } from "@vercel/otel";

export function register() {
  registerOTel({
    serviceName: process.env.OTEL_SERVICE_NAME ?? "payment-dashboard",
    instrumentationConfig: {
      fetch: {
        // Only the platform's own services are told the trace's ids.
        propagateContextUrls: [
          process.env.API_ORIGIN ?? "http://localhost:5080",
          process.env.WORKER_ORIGIN ?? "http://localhost:5081",
        ],
        // The status panel asks both probes every 5 s; like the API, the dashboard leaves them out of its traces.
        ignoreUrls: [/\/health\/(ready|live)$/],
      },
    },
  });
}
