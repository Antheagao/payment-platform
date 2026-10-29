#!/usr/bin/env node
// A merchant's webhook receiver for the local stack (compose's webhook-receiver), so the demo shows deliveries
// arriving, verified, and retried. It does what docs/demo.md tells a merchant to do: verify the Standard Webhooks
// signature over the raw body, refuse a stale timestamp, drop an event id it has seen, answer 2xx at once.
//
//   POST /webhooks   a delivery; 204 when verified, 401 when the signature or timestamp is wrong
//   GET  /events     the last 50 deliveries it saw, newest first, as JSON
//   GET  /health     200
//
// A payment whose merchant_reference starts with "webhook-fail" is answered 503, so its delivery is retried
// with backoff (and dead-lettered after the last attempt). WEBHOOK_SECRET is the endpoint's whsec_ secret.

import { createHmac, timingSafeEqual } from "node:crypto";
import { createServer } from "node:http";

const port = Number(process.env.PORT ?? 8080);
const secret = process.env.WEBHOOK_SECRET ?? "";
if (!secret.startsWith("whsec_")) {
  console.error(JSON.stringify({ level: "error", message: "WEBHOOK_SECRET must be a whsec_ secret" }));
  process.exit(1);
}
const key = Buffer.from(secret.slice("whsec_".length), "base64");
const toleranceSeconds = 5 * 60;
const seen = new Set();
const recent = [];

function verify(id, timestamp, signature, body) {
  const age = Math.abs(Date.now() / 1000 - Number(timestamp));
  if (!id || !Number.isFinite(age) || age > toleranceSeconds) return "stale_or_missing_timestamp";
  const expected = createHmac("sha256", key).update(`${id}.${timestamp}.${body}`).digest();
  // The header may carry several space-separated signatures, during a secret rotation for example.
  const matches = (signature ?? "").split(" ").some((candidate) => {
    const [version, value] = candidate.split(",");
    const given = Buffer.from(value ?? "", "base64");
    return version === "v1" && given.length === expected.length && timingSafeEqual(given, expected);
  });
  return matches ? null : "bad_signature";
}

function record(entry) {
  const line = { time: new Date().toISOString(), ...entry };
  console.log(JSON.stringify(line));
  recent.unshift(line);
  recent.length = Math.min(recent.length, 50);
}

const server = createServer((request, response) => {
  if (request.method === "GET" && request.url === "/health") {
    response.writeHead(200, { "content-type": "text/plain" }).end("ok");
    return;
  }
  if (request.method === "GET" && request.url === "/events") {
    response.writeHead(200, { "content-type": "application/json" }).end(JSON.stringify(recent, null, 2));
    return;
  }
  if (request.method !== "POST" || request.url !== "/webhooks") {
    response.writeHead(404).end();
    return;
  }

  const chunks = [];
  request.on("data", (chunk) => chunks.push(chunk));
  request.on("end", () => {
    const body = Buffer.concat(chunks).toString("utf8");
    const id = request.headers["webhook-id"];
    const context = {
      id,
      correlation_id: request.headers["x-correlation-id"],
      traceparent: request.headers["traceparent"],
    };
    const problem = verify(id, request.headers["webhook-timestamp"], request.headers["webhook-signature"], body);
    if (problem) {
      record({ outcome: problem, ...context });
      response.writeHead(401).end();
      return;
    }

    const event = JSON.parse(body);
    const summary = {
      type: event.type,
      payment_id: event.data?.payment_id,
      status: event.data?.status,
      merchant_reference: event.data?.merchant_reference,
    };
    if (summary.merchant_reference?.startsWith("webhook-fail")) {
      record({ outcome: "failing_on_purpose", ...context, ...summary });
      response.writeHead(503).end();
      return;
    }
    if (seen.has(id)) {
      record({ outcome: "duplicate_ignored", ...context, ...summary });
      response.writeHead(204).end();
      return;
    }
    seen.add(id);
    if (seen.size > 10_000) seen.delete(seen.values().next().value);
    record({ outcome: "verified", ...context, ...summary });
    response.writeHead(204).end();
  });
});

server.listen(port, () => console.log(JSON.stringify({ level: "info", message: `webhook receiver on :${port}` })));
process.on("SIGTERM", () => server.close(() => process.exit(0)));
