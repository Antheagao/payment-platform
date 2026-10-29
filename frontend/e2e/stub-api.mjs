// A stand-in for the payment API, for the end-to-end tests of the dashboard alone (playwright.config.ts).
// It answers the two calls sign-in makes, as the real API spells them, and drops every health probe's
// connection, so the status panel shows the API as unreachable, as it did before sign-in existed. The flows
// against the real API run on the composed stack (T29).
//
//   POST /api/v1/auth/login   demo@example.com / LocalOnly_Demo_2026! signs in; limited@example.com is
//                             rate limited; anything else is invalid_credentials
//   GET  /api/v1/me           a token this stub issued is the demo user; "expired" is token_expired

import { randomUUID } from "node:crypto";
import { createServer } from "node:http";

const port = Number(process.env.STUB_API_PORT ?? 9095);
const demo = { email: "demo@example.com", password: "LocalOnly_Demo_2026!" };

function send(response, status, body, headers = {}) {
  response.writeHead(status, { "content-type": "application/json", ...headers });
  response.end(JSON.stringify(body));
}

const problem = (status, code) => ({ type: "about:blank", title: code, status, code });

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

  if (request.method === "GET" && request.url === "/api/v1/me") {
    const token = (request.headers.authorization ?? "").replace(/^Bearer /, "");
    if (token.startsWith("stub-")) {
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
