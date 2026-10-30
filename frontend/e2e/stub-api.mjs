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
//   GET  /api/v1/payments/{id}           one of them with its card's expiry and its history; "down" is 503
//   POST /api/v1/payments/{id}/capture   and /refund move it as the lifecycle allows, separately for each
//                             token, so tests that sign in with their own token never see each other's
//                             moves; a key sent again replays the first answer. A token that starts with
//                             "stub-decline" has every move declined, "stub-acquirer-down" the network down
//   POST /api/v1/staff/auth/login   operator@example.com / LocalOnly_Operator_2026! signs in as staff (ADR 011)
//   GET  /api/v1/staff/me           a staff token this stub issued is the operator; a merchant's token is 403
//   GET  /api/v1/staff/webhook-deliveries   three fixed dead letters, newest first, less those this staff token
//                             replayed; "staff-down" has the list answer 503
//   POST /api/v1/staff/webhook-deliveries/{id}/replay   takes one off that token's list; again is 409

import { randomUUID } from "node:crypto";
import { createServer } from "node:http";

const port = Number(process.env.STUB_API_PORT ?? 9095);
const demo = { email: "demo@example.com", password: "LocalOnly_Demo_2026!" };
const operator = {
  email: "operator@example.com",
  password: "LocalOnly_Operator_2026!",
  id: "019a2c00-0000-7000-8000-0000000000d4",
};

// Three deliveries that used up their attempts, newest first, and what each staff token has replayed.
const deadLetters = [103, 102, 101].map((id, index) => ({
  id,
  merchant_id: "019a2c00-0000-7000-8000-0000000000d1",
  payment_id: `019a2c00-0000-7000-8000-${String(2000 + index).padStart(12, "0")}`,
  event_type: "payment.settled",
  attempts: 12,
  last_status_code: 503,
  last_error: "HTTP 503",
  endpoint_host: "hooks.example.com",
  created_at: new Date(Date.UTC(2026, 9, 29, 9) - index * 3_600_000).toISOString(),
}));
const replayed = new Map();
const replayedBy = (token) => replayed.get(token) ?? replayed.set(token, new Set()).get(token);

function staffRoute(request, url, token, response) {
  if (!token) return send(response, 401, problem(401, "unauthorized"));
  if (!token.startsWith("staff-")) return send(response, 403, problem(403, "forbidden"));
  const done = replayedBy(token);
  if (request.method === "GET" && url.pathname === "/api/v1/staff/me") {
    return send(response, 200, { staff_id: operator.id, email: operator.email });
  }
  if (request.method === "GET" && url.pathname === "/api/v1/staff/webhook-deliveries") {
    if (token === "staff-down") return send(response, 503, problem(503, "service_unavailable"));
    if (url.searchParams.get("status") !== "dead_lettered") {
      return send(response, 400, {
        ...problem(400, "invalid_request"),
        errors: { status: ["invalid_status"] },
      });
    }
    return send(response, 200, {
      data: deadLetters.filter(({ id }) => !done.has(id)),
      next_cursor: null,
    });
  }
  const replay = url.pathname.match(/^\/api\/v1\/staff\/webhook-deliveries\/(\d+)\/replay$/);
  if (request.method === "POST" && replay) {
    const id = Number(replay[1]);
    if (!deadLetters.some((row) => row.id === id))
      return send(response, 404, problem(404, "not_found"));
    if (done.has(id)) return send(response, 409, problem(409, "delivery_not_dead_lettered"));
    done.add(id);
    return send(response, 200, {
      id,
      status: "pending",
      attempts: 0,
      next_attempt_at: new Date().toISOString(),
    });
  }
  return send(response, 404, problem(404, "not_found"));
}

function send(response, status, body, headers = {}) {
  response.writeHead(status, { "content-type": "application/json", ...headers });
  response.end(JSON.stringify(body));
}

const problem = (status, code) => ({ type: "about:blank", title: code, status, code });

const statuses = ["authorized", "captured", "settled", "failed", "refunded", "pending"];
const currencies = ["USD", "EUR", "JPY"];
// How each status was reached, from creation, as the seeded demo walks it (DemoPayments.Build).
const paths = {
  pending: ["pending"],
  authorized: ["pending", "authorized"],
  captured: ["pending", "authorized", "captured"],
  settled: ["pending", "authorized", "captured", "settled"],
  failed: ["pending", "failed"],
  refunded: ["pending", "authorized", "captured", "refunded"],
};
const user = { kind: "merchant_user", id: "019a2c00-0000-7000-8000-0000000000d2" };
const settlement = { kind: "system", id: "settlement" };
const moves = {
  capture: { from: ["authorized"], to: "captured" },
  refund: { from: ["captured", "settled"], to: "refunded" },
};

// Each change to a payment comes this long after the one before it.
const step = 20 * 60_000;
const after = (iso, steps) => new Date(Date.parse(iso) + steps * step).toISOString();

const payments = Array.from({ length: 30 }, (_, index) => {
  const created = new Date(Date.UTC(2026, 9, 29, 12) - index * 3_600_000).toISOString();
  const status = statuses[index % statuses.length];
  return {
    id: `019a2c00-0000-7000-8000-${String(2000 + index).padStart(12, "0")}`,
    status,
    amount_minor: currencies[index % 3] === "JPY" ? 7600 + index : 1000 + index * 125,
    currency: currencies[index % 3],
    merchant_reference: `INV-${3000 + index}`,
    card: { brand: index % 2 ? "mastercard" : "visa", last4: index % 2 ? "4444" : "4242" },
    created_at: created,
    updated_at: after(created, paths[status].length - 1),
  };
});

// Per token: the payments it has moved, by id, and its answers, by idempotency key.
const sessions = new Map();
function stateOf(token) {
  if (!sessions.has(token)) sessions.set(token, { moved: new Map(), answers: new Map() });
  return sessions.get(token);
}

// A payment as this token sees it: its status and last change after the token's own moves.
function asMovedBy(payment, token) {
  const moved = stateOf(token).moved.get(payment.id);
  return moved
    ? { ...payment, status: moved.status, updated_at: moved.history.at(-1).occurred_at }
    : payment;
}

function detailOf(payment, token) {
  const moved = stateOf(token).moved.get(payment.id);
  const steps = paths[payment.status];
  const history = steps.map((to, index) => ({
    from: index ? steps[index - 1] : null,
    to,
    actor: to === "settled" ? settlement : user,
    occurred_at: after(payment.created_at, index),
    reason: to === "failed" ? "card_declined" : null,
    correlation_id: to === "settled" ? null : `0af7651916cd43dd8448eb211c80319${index}`,
  }));
  const authorized = steps.includes("authorized");
  return {
    ...asMovedBy(payment, token),
    card: { ...payment.card, exp_month: 12, exp_year: 2028 },
    authorization_reference: authorized ? `sim_auth_${payment.id.replaceAll("-", "")}` : null,
    failure_reason: payment.status === "failed" ? "card_declined" : null,
    history: [...history, ...(moved?.history ?? [])],
  };
}

function movePayment(payment, operation, request, token, response) {
  const key = request.headers["idempotency-key"];
  if (!key) return send(response, 400, problem(400, "idempotency_key_required"));
  const state = stateOf(token);
  const replay = state.answers.get(`${payment.id}/${operation}/${key}`);
  if (replay) return send(response, 200, replay, { "idempotent-replayed": "true" });

  const detail = detailOf(payment, token);
  const { from, to } = moves[operation];
  if (!from.includes(detail.status)) {
    const code = operation === "capture" ? "payment_not_capturable" : "payment_not_refundable";
    return send(response, 409, problem(409, code));
  }
  if (token.startsWith("stub-decline")) {
    return send(response, 422, problem(422, `${operation}_declined`));
  }
  if (token.startsWith("stub-acquirer-down")) {
    return send(response, 503, problem(503, "acquirer_unavailable"), { "retry-after": "5" });
  }

  const earlier = state.moved.get(payment.id)?.history ?? [];
  const change = {
    from: detail.status,
    to,
    actor: user,
    occurred_at: new Date().toISOString(),
    reason: null,
    correlation_id: randomUUID().replaceAll("-", ""),
  };
  state.moved.set(payment.id, { status: to, history: [...earlier, change] });
  const answer = detailOf(payment, token);
  state.answers.set(`${payment.id}/${operation}/${key}`, answer);
  send(response, 200, answer);
}

function listPayments(url, token, response) {
  if (token === "down") return send(response, 503, problem(503, "service_unavailable"));
  const wanted = (url.searchParams.get("status") ?? "").split(",").filter(Boolean);
  const currency = url.searchParams.get("currency");
  const reference = url.searchParams.get("reference");
  const matching =
    token === "empty"
      ? []
      : payments
          .map((payment) => asMovedBy(payment, token))
          .filter(
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

  if (request.method === "POST" && request.url === "/api/v1/staff/auth/login") {
    let raw = "";
    request.on("data", (chunk) => (raw += chunk));
    request.on("end", () => {
      const { email, password } = JSON.parse(raw || "{}");
      if (email === operator.email && password === operator.password) {
        send(response, 200, {
          access_token: `staff-${randomUUID()}`,
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
  if (url.pathname.startsWith("/api/v1/staff/")) return staffRoute(request, url, token, response);
  if (request.method === "GET" && url.pathname === "/api/v1/payments") {
    if (token === "expired" || !token) return send(response, 401, problem(401, "token_expired"));
    return listPayments(url, token, response);
  }

  const one = url.pathname.match(/^\/api\/v1\/payments\/([0-9a-f-]{36})(?:\/(capture|refund))?$/);
  if (one && (request.method === "GET" ? !one[2] : request.method === "POST" && one[2])) {
    if (token === "expired" || !token) return send(response, 401, problem(401, "token_expired"));
    if (token === "down") return send(response, 503, problem(503, "service_unavailable"));
    const payment = token === "empty" ? undefined : payments.find(({ id }) => id === one[1]);
    if (!payment) return send(response, 404, problem(404, "not_found"));
    return one[2]
      ? movePayment(payment, one[2], request, token, response)
      : send(response, 200, detailOf(payment, token));
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
