// A stand-in for the payment API, for the end-to-end tests of the dashboard alone (playwright.config.ts).
// It answers sign-in, the current user, and the payments list, as the real API spells them, and drops every
// health probe's connection, so the status panel shows the API as unreachable. The flows against the real API
// run on the composed stack (T29).
//
//   POST /api/v1/auth/login   demo@example.com / LocalOnly_Demo_2026! signs in; limited@example.com is
//                             rate limited; anything else is invalid_credentials
//   GET  /api/v1/me           a token this stub issued is the demo user; "expired" is token_expired
//   GET  /api/v1/payments     30 fixed payments, newest first, 25 to a page, filtered by status, currency,
//                             and reference prefix; "expired" is token_expired, "empty" has no payments,
//                             "down" makes the API answer 503

import { randomUUID } from "node:crypto";
import { createServer } from "node:http";

const port = Number(process.env.STUB_API_PORT ?? 9095);
const demo = { email: "demo@example.com", password: "LocalOnly_Demo_2026!" };

function send(response, status, body, headers = {}) {
  response.writeHead(status, { "content-type": "application/json", ...headers });
  response.end(JSON.stringify(body));
}

const problem = (status, code) => ({ type: "about:blank", title: code, status, code });

const statuses = ["authorized", "captured", "settled", "failed", "refunded", "pending"];
const currencies = ["USD", "EUR", "JPY"];
const payments = Array.from({ length: 30 }, (_, index) => {
  const created = new Date(Date.UTC(2026, 9, 29, 12) - index * 3_600_000).toISOString();
  return {
    id: `019a2c00-0000-7000-8000-${String(2000 + index).padStart(12, "0")}`,
    status: statuses[index % statuses.length],
    amount_minor: currencies[index % 3] === "JPY" ? 7600 + index : 1000 + index * 125,
    currency: currencies[index % 3],
    merchant_reference: `INV-${3000 + index}`,
    card: { brand: index % 2 ? "mastercard" : "visa", last4: index % 2 ? "4444" : "4242" },
    created_at: created,
    updated_at: created,
  };
});

function listPayments(url, token, response) {
  if (token === "down") return send(response, 503, problem(503, "service_unavailable"));
  const wanted = (url.searchParams.get("status") ?? "").split(",").filter(Boolean);
  const currency = url.searchParams.get("currency");
  const reference = url.searchParams.get("reference");
  const matching =
    token === "empty"
      ? []
      : payments.filter(
          (payment) =>
            (!wanted.length || wanted.includes(payment.status)) &&
            (!currency || payment.currency === currency) &&
            (!reference || payment.merchant_reference.startsWith(reference)),
        );
  const start = Number(url.searchParams.get("cursor") ?? 0);
  const page = matching.slice(start, start + 25);
  const next = start + 25 < matching.length ? String(start + 25) : null;
  send(response, 200, { data: page, next_cursor: next });
}

createServer((request, response) => {
  if (request.url.startsWith("/health/")) {
    request.socket.destroy();
    return;
  }

  if (request.method === "POST" && request.url === "/api/v1/auth/login") {
    let raw = "";
    request.on("data", (chunk) => (raw += chunk));
    request.on("end", () => {
      const { email, password } = JSON.parse(raw || "{}");
      if (email === "limited@example.com") {
        send(response, 429, problem(429, "rate_limited"), { "retry-after": "42" });
      } else if (email === demo.email && password === demo.password) {
        send(response, 200, {
          access_token: `stub-${randomUUID()}`,
          token_type: "Bearer",
          expires_at: new Date(Date.now() + 30 * 60_000).toISOString(),
          expires_in: 1800,
        });
      } else {
        send(response, 401, problem(401, "invalid_credentials"));
      }
    });
    return;
  }

  const url = new URL(request.url, "http://stub");
  const token = (request.headers.authorization ?? "").replace(/^Bearer /, "");
  if (request.method === "GET" && url.pathname === "/api/v1/payments") {
    if (token === "expired" || !token) return send(response, 401, problem(401, "token_expired"));
    return listPayments(url, token, response);
  }

  if (request.method === "GET" && request.url === "/api/v1/me") {
    if (token.startsWith("stub-") || token === "empty" || token === "down") {
      send(response, 200, {
        user_id: "019a2c00-0000-7000-8000-0000000000d2",
        merchant_id: "019a2c00-0000-7000-8000-0000000000d1",
        email: demo.email,
        merchant_name: "Demo Merchant",
      });
    } else {
      send(response, 401, problem(401, token === "expired" ? "token_expired" : "unauthorized"));
    }
    return;
  }

  send(response, 404, problem(404, "not_found"));
}).listen(port, "127.0.0.1", () => console.log(`stub API on :${port}`));
