import { defineConfig, devices } from "@playwright/test";

// Golden paths against a production build (`next build` + `next start`), never the dev server.
// API_ORIGIN points at e2e/stub-api.mjs, which answers sign-in and drops every health probe, and
// WORKER_ORIGIN at a closed port, so the shell is tested with both services down; flows against the real
// API run on the composed stack (T29).
//
// Its own port, never the dashboard's 3080: locally, `reuseExistingServer` would otherwise test the
// composed dashboard, which talks to a live API, instead of this build.
const port = 3090;
const stubApiPort = 9095;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${port}`,
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "desktop",
      use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } },
    },
    {
      name: "mobile",
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 360, height: 780 },
        isMobile: true,
        hasTouch: true,
      },
    },
  ],
  webServer: [
    {
      command: `node e2e/stub-api.mjs`,
      url: `http://127.0.0.1:${stubApiPort}/api/v1/me`,
      reuseExistingServer: !process.env.CI,
      env: { STUB_API_PORT: String(stubApiPort) },
    },
    {
      command: `npm run build && npx next start -p ${port}`,
      url: `http://localhost:${port}/login`,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
      env: {
        API_ORIGIN: `http://127.0.0.1:${stubApiPort}`,
        WORKER_ORIGIN: "http://127.0.0.1:9",
        DEMO_LOGIN_HINT: "true",
      },
    },
  ],
});
