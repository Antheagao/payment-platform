import { defineConfig, devices } from "@playwright/test";

// Golden paths against a production build (`next build` + `next start`), never the dev server.
// API_ORIGIN and WORKER_ORIGIN point at a closed port, so the shell is tested with both down; flows
// that need a running API run against the composed stack once they exist (T29).
//
// Its own port, never the dashboard's 3080: locally, `reuseExistingServer` would otherwise test the
// composed dashboard, which talks to a live API, instead of this build.
const port = 3090;

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
  webServer: {
    command: `npm run build && npx next start -p ${port}`,
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: { API_ORIGIN: "http://127.0.0.1:9", WORKER_ORIGIN: "http://127.0.0.1:9" },
  },
});
