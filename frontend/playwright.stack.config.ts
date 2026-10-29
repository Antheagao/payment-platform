import { defineConfig, devices } from "@playwright/test";

// The golden path against the composed stack (`docker compose up --build --wait`): the real dashboard behind
// its edge, the real API, SQL Server, the worker, and the webhook receiver. Nothing is started here; the CI
// `stack` job runs it after `scripts/check-stack.sh`. The stub-API tests stay in playwright.config.ts.
//
//   DASHBOARD_URL          default http://localhost:3080
//   WEBHOOK_RECEIVER_URL   default http://localhost:8090
export default defineConfig({
  testDir: "./e2e-stack",
  // One sign-in for the whole run: the API allows five per email in five minutes.
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  // The worker delivers webhooks a second or two after a move; a cold stack answers its first pages slowly.
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: process.env.DASHBOARD_URL ?? "http://localhost:3080",
    trace: "retain-on-failure",
    ...devices["Desktop Chrome"],
    viewport: { width: 1440, height: 900 },
  },
});
