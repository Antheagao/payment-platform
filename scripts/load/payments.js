// A load test of the payments API on the composed stack (k6, run by the `load` compose profile):
//
//   docker compose --profile load run --rm load                      # 20 checkouts a second for 1 minute
//   docker compose --profile load run --rm -e RATE=60 -e DURATION=2m load
//
// Each iteration is one checkout as a merchant's server makes it: create a payment (authorized by the simulated
// acquirer), read it back, capture every second one, and read the newest page of the list, as the dashboard
// does. So RATE checkouts a second is about 3.5 times as many requests. It signs in once, as the demo merchant,
// and writes real payments: reset the stack afterwards (`docker compose down -v`) to get the demo data back.
import http from "k6/http";
import { check } from "k6";
import { Trend } from "k6/metrics";

const api = __ENV.API_URL || "http://api:8080";
const rate = Number(__ENV.RATE || 20);

export const options = {
  scenarios: {
    checkout: {
      executor: "constant-arrival-rate",
      rate,
      timeUnit: "1s",
      duration: __ENV.DURATION || "1m",
      preAllocatedVUs: Math.max(10, rate * 2),
      maxVUs: Math.max(50, rate * 10),
    },
  },
  // The service objective this run is judged by: what a merchant's checkout can live with.
  thresholds: {
    http_req_failed: ["rate<0.01"],
    "http_req_duration{name:create}": ["p(95)<500"],
    "http_req_duration{name:read}": ["p(95)<200"],
    "http_req_duration{name:list}": ["p(95)<300"],
    "http_req_duration{name:capture}": ["p(95)<500"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

const checkoutDuration = new Trend("checkout_duration", true);

export function setup() {
  const login = http.post(
    `${api}/api/v1/auth/login`,
    // DemoSeed's local-only demo login.
    JSON.stringify({ email: "demo@example.com", password: "LocalOnly_Demo_2026!" }),
    { headers: { "Content-Type": "application/json" } },
  );
  if (login.status !== 200) throw new Error(`login answered ${login.status}`);
  return { token: login.json("access_token") };
}

export default function ({ token }) {
  const auth = { Authorization: `Bearer ${token}` };
  const started = Date.now();

  const created = http.post(
    `${api}/api/v1/payments`,
    JSON.stringify({
      amount_minor: 500 + Math.floor(Math.random() * 50_000),
      currency: "USD",
      merchant_reference: `load-${__VU}-${__ITER}`,
      // A simulator test card; the vault holds nothing else (ADR 008).
      card: { number: "4242 4242 4242 4242", exp_month: 12, exp_year: 2030, cvv: "123" },
    }),
    {
      headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": `load-${__VU}-${__ITER}-${started}` },
      tags: { name: "create" },
    },
  );
  if (!check(created, { "created 201": (response) => response.status === 201 })) return;
  const id = created.json("id");

  const read = http.get(`${api}/api/v1/payments/${id}`, { headers: auth, tags: { name: "read" } });
  check(read, { "read 200": (response) => response.status === 200 });

  if (__ITER % 2 === 0) {
    const captured = http.post(`${api}/api/v1/payments/${id}/capture`, null, {
      headers: { ...auth, "Idempotency-Key": `load-capture-${id}` },
      tags: { name: "capture" },
    });
    check(captured, { "captured 200": (response) => response.status === 200 });
  }

  const list = http.get(`${api}/api/v1/payments?limit=25`, { headers: auth, tags: { name: "list" } });
  check(list, { "list 200": (response) => response.status === 200 });

  checkoutDuration.add(Date.now() - started);
}
