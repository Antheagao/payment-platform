#!/usr/bin/env node
// Captures the screenshots and the outage GIF in docs/images/ from the running stack, so they are
// remade after a UI change instead of drifting from it.
//
//   docker compose up --build -d --wait && sh scripts/check-stack.sh
//   node scripts/capture-screenshots.mjs
//
// It drives Playwright's Chromium from frontend/ (run `npm ci` and `npx playwright install chromium`
// there first) and turns the recording into a GIF with ffmpeg; without ffmpeg on the PATH it takes
// the stills only. To show the outage states it stops and starts the sqlserver and api containers,
// and starts them again even when a step fails. Honors the port variables check-stack.sh honors.
import { execFileSync, spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(root, "docs/images");
const { chromium } = createRequire(join(root, "frontend/package.json"))("@playwright/test");

// Only the port variables from .env; the file may hold anything else.
const envFile = existsSync(join(root, ".env"))
  ? Object.fromEntries(
      readFileSync(join(root, ".env"), "utf8")
        .split(/\r?\n/)
        .map((line) => line.match(/^(DASHBOARD_PORT|API_PORT|ASPIRE_DASHBOARD_PORT)=(\d+)$/))
        .filter(Boolean)
        .map(([, name, value]) => [name, value]),
    )
  : {};
const origin = (name, fallback) => `http://localhost:${process.env[name] ?? envFile[name] ?? fallback}`;
const dashboard = origin("DASHBOARD_PORT", 3080);
const api = origin("API_PORT", 5080);
const aspire = origin("ASPIRE_DASHBOARD_PORT", 18888);

// Retina-sharp stills at the widths the README shows; the recording is 1x, and only as tall as the
// status panel and its caption, to keep the GIF small.
const desktop = { viewport: { width: 1280, height: 800 }, deviceScaleFactor: 2 };
const phone = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };
const recording = { width: 1200, height: 560 };
// Statuses settle within one live update (5 s) of a change; a restarted SQL Server takes longer.
const settleTimeout = 120_000;

const compose = (...args) => execFileSync("docker", ["compose", ...args], { cwd: root, stdio: "ignore" });
const hasFfmpeg = spawnSync("ffmpeg", ["-version"], { stdio: "ignore" }).status === 0;
const saved = [];

async function shoot(page, name, options = {}) {
  const path = join(out, name);
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path, ...options });
  saved.push(path);
}

const tile = (page, name) =>
  page.getByRole("region", { name: "Service status" }).getByRole("listitem").filter({ hasText: name });

async function waitForStatus(page, { api: apiLabel, database, worker }) {
  const wait = { timeout: settleTimeout };
  // The header's API badge shows from the sm breakpoint; at phone width the panel's own tile says it.
  const badge = page.getByRole("banner").getByRole("status");
  if ((page.viewportSize()?.width ?? 0) >= 640) await badge.filter({ hasText: apiLabel }).waitFor(wait);
  await tile(page, "Database").filter({ hasText: database }).waitFor(wait);
  await tile(page, "Background worker").filter({ hasText: worker }).waitFor(wait);
}
const healthy = { api: "API ready", database: "Connected", worker: "Ready" };

async function waitForReady(url) {
  const deadline = Date.now() + settleTimeout;
  while (Date.now() < deadline) {
    const answered = await fetch(url).then((response) => response.ok, () => false);
    if (answered) return;
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(`${url} was not ready within ${settleTimeout / 1000} s`);
}

/**
 * Signs in at `path` with one of DemoSeed's local-only logins, the merchant's or the operator's (ADR 011); the
 * context keeps the session.
 */
async function signIn(page, path, email, password, landing) {
  await page.goto(`${dashboard}${path}`);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL(`${dashboard}${landing}`);
}

/**
 * One sign-in per kind for the whole run, shared by every context through its storage state: the API allows
 * five sign-ins per email in five minutes, and a run with one per context would spend them all.
 */
const sessions = {};
async function sessionOf(browser, kind) {
  if (!sessions[kind]) {
    const first = await browser.newContext(desktop);
    const page = await first.newPage();
    if (kind === "staff") {
      await signIn(page, "/operator/login", "operator@example.com", "LocalOnly_Operator_2026!", "/operator");
    } else {
      await signIn(page, "/login", "demo@example.com", "LocalOnly_Demo_2026!", "/");
    }
    sessions[kind] = await first.storageState();
    await first.close();
  }
  return sessions[kind];
}

/** A context signed in as the demo merchant. */
async function signedIn(browser, options) {
  return browser.newContext({ ...options, storageState: await sessionOf(browser, "merchant") });
}

/** A context signed in as the demo operator, for the operator area. */
async function signedInStaff(browser, options) {
  return browser.newContext({ ...options, storageState: await sessionOf(browser, "staff") });
}

/** The operator area, where the service status panel lives (ADR 011). */
async function openOperator(context) {
  const page = await context.newPage();
  await page.goto(`${dashboard}/operator`);
  // The dead letters stream in after the panel; a still taken before them shows their loading shape.
  await page
    .getByRole("region", { name: "Dead-lettered webhooks" })
    .getByText(/No dead-lettered deliveries|could not be read|attempts, last/)
    .first()
    .waitFor({ timeout: settleTimeout });
  return page;
}

/** Narration for the recording: a caption pinned to the bottom of the page, outside React's tree. */
async function caption(page, text) {
  await page.evaluate((text) => {
    let note = document.getElementById("capture-caption");
    if (!note) {
      note = document.createElement("div");
      note.id = "capture-caption";
      Object.assign(note.style, {
        position: "fixed",
        left: "50%",
        bottom: "24px",
        transform: "translateX(-50%)",
        padding: "10px 16px",
        borderRadius: "8px",
        background: "rgb(17 24 39 / 0.94)",
        color: "white",
        font: "500 15px/1.4 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
        boxShadow: "0 8px 24px rgb(0 0 0 / 0.25)",
        zIndex: "100",
        whiteSpace: "nowrap",
      });
      document.documentElement.append(note);
    }
    note.textContent = text;
  }, text);
}

async function dashboardStills(browser) {
  const signedOut = await browser.newContext({ ...desktop, colorScheme: "light", reducedMotion: "reduce" });
  const login = await signedOut.newPage();
  await login.goto(`${dashboard}/login`);
  // Merchants are shown no readiness (ADR 011), so the page is ready when its form is.
  await login.getByRole("button", { name: "Sign in" }).waitFor();
  await shoot(login, "dashboard-login.png");
  await signedOut.close();

  for (const colorScheme of ["light", "dark"]) {
    // The operator area: the service status, and the dead letters (none on a fresh stack).
    const wide = await signedInStaff(browser, { ...desktop, colorScheme, reducedMotion: "reduce" });
    const page = await openOperator(wide);
    await waitForStatus(page, healthy);
    await shoot(page, `operator-${colorScheme}.png`, { fullPage: true });
    await wide.close();

    const narrow = await signedInStaff(browser, { ...phone, colorScheme, reducedMotion: "reduce" });
    const mobile = await openOperator(narrow);
    await waitForStatus(mobile, healthy);
    await shoot(mobile, `operator-mobile-${colorScheme}.png`);
    await narrow.close();
  }

  // The merchant's home: the way in to the payments and the lifecycle, and no infrastructure.
  const homeWide = await signedIn(browser, { ...desktop, colorScheme: "light", reducedMotion: "reduce" });
  const home = await homeWide.newPage();
  await home.goto(dashboard);
  await home.getByRole("region", { name: "Payment lifecycle" }).waitFor();
  await shoot(home, "dashboard-home.png", { fullPage: true });
  await homeWide.close();

  // The payments list over the seeded payments: the table at desktop width, the cards at phone width.
  const listWide = await signedIn(browser, { ...desktop, colorScheme: "light", reducedMotion: "reduce" });
  const list = await listWide.newPage();
  await list.goto(`${dashboard}/payments`);
  await list.locator("tbody tr").first().waitFor();
  await shoot(list, "dashboard-payments.png");
  await listWide.close();

  const listNarrow = await signedIn(browser, { ...phone, colorScheme: "dark", reducedMotion: "reduce" });
  const cards = await listNarrow.newPage();
  await cards.goto(`${dashboard}/payments`);
  await cards.getByRole("main").getByRole("listitem").first().waitFor();
  await shoot(cards, "dashboard-payments-mobile.png");
  await listNarrow.close();

  // One payment's page, opened from its row: INV-1004 has walked the whole lifecycle, settlement included.
  const detailWide = await signedIn(browser, { ...desktop, colorScheme: "light", reducedMotion: "reduce" });
  const detail = await detailWide.newPage();
  await detail.goto(`${dashboard}/payments?reference=INV-1004`);
  await detail.getByRole("main").getByRole("link", { name: "INV-1004" }).click();
  await detail.getByRole("region", { name: "History" }).getByRole("listitem").nth(4).waitFor();
  await shoot(detail, "dashboard-payment-detail.png", { fullPage: true });
  await detailWide.close();
}

/** Stops the database, then the API, recording the first and taking a still of each. */
async function outage(browser) {
  const videoDir = mkdtempSync(join(tmpdir(), "payment-platform-video-"));
  // Reduced motion stills the live dot, so frames change only when a status does.
  const recorder = await signedInStaff(browser, {
    viewport: recording,
    colorScheme: "light",
    reducedMotion: "reduce",
    recordVideo: { dir: videoDir, size: recording },
  });
  const recordingStarted = Date.now();
  const stills = await signedInStaff(browser, { ...desktop, colorScheme: "light", reducedMotion: "reduce" });
  try {
    const live = await openOperator(recorder);
    const watch = await openOperator(stills);
    await waitForStatus(live, healthy);
    const trimStart = (Date.now() - recordingStarted) / 1000;
    await caption(live, "Everything ready; the panel asks again every 5 seconds");
    await live.waitForTimeout(3_500);

    await caption(live, "$ docker compose stop sqlserver");
    compose("stop", "sqlserver");
    const databaseDown = { api: "API not ready", database: "Unavailable", worker: "Not ready" };
    await waitForStatus(live, databaseDown);
    await caption(live, "The API stays up but stops taking traffic; the database is what is down");
    await waitForStatus(watch, databaseDown);
    await shoot(watch, "dashboard-database-down.png");
    await live.waitForTimeout(4_000);

    await caption(live, "$ docker compose start sqlserver");
    compose("start", "sqlserver");
    await waitForStatus(live, healthy);
    await caption(live, "Ready again, with no restart");
    await live.waitForTimeout(3_500);
    const video = await live.video().path();
    await recorder.close();

    compose("stop", "api");
    await waitForStatus(watch, { api: "API unreachable", database: "Unknown", worker: "Ready" });
    await shoot(watch, "dashboard-api-down.png");
    compose("start", "api");
    // Not on the page: in compose the restarted API signs with a new key, so the operator's session has ended
    // and the operator area rightly asks for a sign-in again. Readiness says when the API is back.
    await waitForReady(`${api}/health/ready`);

    if (hasFfmpeg) toGif(video, join(out, "outage.gif"), trimStart);
  } finally {
    compose("start", "sqlserver", "api");
    await stills.close();
    await recorder.close();
    rmSync(videoDir, { recursive: true, force: true });
  }
}

function toGif(video, path, trimStart) {
  // Frames that repeat the one before are dropped and the rest keep their timing (variable frame
  // rate), one palette serves the whole clip, and only changed rectangles are re-dithered, so the
  // static page does not shimmer and the file stays small.
  const filters =
    "scale=960:-1:flags=lanczos,mpdecimate,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";
  execFileSync("ffmpeg", [
    ...["-y", "-loglevel", "error", "-ss", trimStart.toFixed(2), "-i", video],
    ...["-vf", filters, "-fps_mode", "vfr", path],
  ]);
  saved.push(path);
}

/** A JSON POST to the API that must succeed; returns the parsed body. */
async function postJson(path, body, headers = {}) {
  const response = await fetch(`${api}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`POST ${path} answered ${response.status}`);
  return response.json();
}

/** One request with a correlation id, followed into Aspire's trace, error logs, and metrics. */
async function observability(browser) {
  const response = await fetch(`${api}/api/v1/payments/123`, {
    headers: { "X-Correlation-Id": "demo-1" },
  });
  const { trace_id: traceId } = await response.json();
  const context = await browser.newContext({ ...desktop, colorScheme: "dark" });
  const page = await context.newPage();
  // The "Endpoint is unsecured" banner is expected locally (docs/demo.md); dismissed once, it stays
  // away for the session. Its close control is a fluent-button custom element that Playwright's role
  // queries do not match, so it is found by class.
  const dismissBanner = async () => {
    await page.locator(".dashboard-message-bar-dismiss").click({ timeout: 5_000 }).catch(() => {});
    await page.getByText("Endpoint is unsecured").waitFor({ state: "hidden", timeout: 5_000 }).catch(() => {});
  };

  // The span reaches Aspire with the next OTLP export, a few seconds after the request.
  const span = page.getByRole("gridcell", { name: /payment-api.*: GET/ }).first();
  for (let attempt = 0; attempt < 15 && !(await span.isVisible()); attempt += 1) {
    await page.goto(`${aspire}/traces/detail/${traceId}`);
    await span.waitFor({ timeout: 2_000 }).catch(() => {});
  }
  await dismissBanner();
  await span.click();
  await page.getByText("correlation.id").waitFor();
  await shoot(page, "aspire-trace.png");

  // A payment sent with a trace id chosen here, so its trace can be opened directly; the worker's
  // dispatch of its payment.authorized event and the signed delivery to compose's webhook receiver join
  // that trace a second or two later (ADR 003).
  const paymentTraceId = randomBytes(16).toString("hex");
  const { access_token: token } = await postJson("/api/v1/auth/login", {
    email: "demo@example.com",
    password: "LocalOnly_Demo_2026!", // DemoSeed's local-only demo login
  });
  await postJson(
    "/api/v1/payments",
    {
      amount_minor: 4999,
      currency: "USD",
      merchant_reference: "screenshot",
      card: { number: "4242 4242 4242 4242", exp_month: 12, exp_year: 2030, cvv: "123" },
    },
    {
      Authorization: `Bearer ${token}`,
      "Idempotency-Key": `screenshot-${paymentTraceId}`,
      "X-Correlation-Id": "demo-outbox",
      traceparent: `00-${paymentTraceId}-${randomBytes(8).toString("hex")}-01`,
    },
  );
  const delivery = page.getByRole("gridcell", { name: /payment-worker.*: webhook deliver/ }).first();
  for (let attempt = 0; attempt < 15 && !(await delivery.isVisible()); attempt += 1) {
    await page.goto(`${aspire}/traces/detail/${paymentTraceId}`);
    await delivery.waitFor({ timeout: 2_000 }).catch(() => {});
  }
  await delivery.click();
  await page.getByText("webhook.attempt").waitFor();
  // The details pane narrows the grid under the pointer; parked, it draws no hover label over a row.
  await page.mouse.move(0, 0);
  await shoot(page, "aspire-outbox-trace.png");

  await page.goto(`${aspire}/structuredlogs/resource/payment-api?logLevel=error`);
  await page.getByText(/Health check database/).first().waitFor();
  await shoot(page, "aspire-logs.png");

  await page.goto(
    `${aspire}/metrics/resource/payment-api?meter=Microsoft.AspNetCore.Hosting&instrument=http.server.request.duration&duration=5&view=Graph`,
  );
  await page.getByText("Duration of HTTP server requests.").waitFor();
  // The chart draws after its data arrives.
  await page.waitForTimeout(2_000);
  await shoot(page, "aspire-metrics.png");

  // The worker's webhook delivery attempts by outcome (R20). Metrics reach Aspire with the next export, up to a
  // minute after the deliveries, so the table is polled until it has a row. The table, not the graph: the graph
  // draws the interval not yet exported as a fall to zero, which reads as a counter going backwards.
  const attempts = (view) =>
    `${aspire}/metrics/resource/payment-worker?meter=PaymentPlatform.Outbox&instrument=webhook.delivery.attempts&duration=5&view=${view}`;
  const exported = page.getByRole("gridcell", { name: /\d{1,2}\/\d{1,2}\/\d{4}/ }).first();
  for (let attempt = 0; attempt < 20 && !(await exported.isVisible()); attempt += 1) {
    await page.goto(attempts("Table"));
    await exported.waitFor({ timeout: 5_000 }).catch(() => {});
  }
  await page.getByText("Webhook delivery attempts, by how they ended").waitFor();
  await page.mouse.move(0, 0);
  await shoot(page, "aspire-webhook-metrics.png");
  await context.close();
}

mkdirSync(out, { recursive: true });
const browser = await chromium.launch();
try {
  await dashboardStills(browser);
  await outage(browser);
  await observability(browser);
} finally {
  await browser.close();
}
console.log(saved.map((path) => `  ${relative(root, path).replaceAll("\\", "/")}`).join("\n"));
if (!hasFfmpeg) console.log("ffmpeg is not on the PATH, so outage.gif was not remade.");
