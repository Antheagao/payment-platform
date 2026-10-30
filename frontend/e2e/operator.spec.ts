import { randomUUID } from "node:crypto";
import { expect, test, type BrowserContext } from "@playwright/test";

// The operator area against e2e/stub-api.mjs (ADR 011): its own sign-in and session, the service status panel,
// and the dead letters with their replay. A staff cookie's value picks the stub's behaviour, as the merchant
// one does: a `staff-` token is signed in, with three dead letters of its own.

async function staffSession(
  context: BrowserContext,
  baseURL: string,
  token = `staff-${randomUUID()}`,
) {
  // As the server sets it: on the operator area's path only. Given a URL, Playwright would put it on "/".
  await context.addCookies([
    {
      name: "pp_staff_session",
      value: token,
      domain: new URL(baseURL).hostname,
      path: "/operator",
    },
  ]);
}

test.describe("operator area", () => {
  test("signs staff in on its own page and lands on the operator area", async ({
    page,
    isMobile,
  }) => {
    await page.goto("/operator");
    await expect(page).toHaveURL("/operator/login");
    await expect(page.getByRole("heading", { level: 1, name: "Operator sign-in" })).toBeVisible();

    await page.getByLabel("Email").fill("operator@example.com");
    await page.getByLabel("Password").fill("LocalOnly_Operator_2026!");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL("/operator");
    await expect(page.getByRole("heading", { level: 1, name: "Operations" })).toBeVisible();
    // The header marks the area from sm up; below it the heading alone says where this is.
    await expect(page.getByRole("banner").getByText("Operator", { exact: true })).toBeVisible({
      visible: !isMobile,
    });
    // The cookie is the staff session's, sent to the operator area only, and out of reach of script.
    const cookies = await page.context().cookies();
    const staff = cookies.find((cookie) => cookie.name === "pp_staff_session");
    expect(staff).toMatchObject({ path: "/operator", httpOnly: true, sameSite: "Strict" });
    expect(cookies.find((cookie) => cookie.name === "pp_session")).toBeUndefined();
  });

  test("keeps a merchant's session out of the operator area, and a staff session out of the merchant's", async ({
    page,
    context,
    baseURL,
  }) => {
    await context.addCookies([{ name: "pp_session", value: "stub-e2e", url: baseURL! }]);
    await page.goto("/operator");
    await expect(page).toHaveURL("/operator/login");

    await context.clearCookies();
    await staffSession(context, baseURL!);
    await page.goto("/payments");
    await expect(page).toHaveURL("/login?next=%2Fpayments");
  });

  test("shows each service on its own, and the database as unknown while the API is down", async ({
    page,
    context,
    baseURL,
    isMobile,
  }) => {
    await staffSession(context, baseURL!);
    await page.goto("/operator");

    const tiles = page.getByRole("region", { name: "Service status" }).getByRole("listitem");
    await expect(tiles).toHaveCount(3);
    await expect(tiles.nth(0)).toContainText("Payments API");
    await expect(tiles.nth(0)).toContainText("Unreachable");
    await expect(tiles.nth(1)).toContainText("Database");
    await expect(tiles.nth(1)).toContainText("Unknown");
    await expect(tiles.nth(2)).toContainText("Background worker");
    await expect(tiles.nth(2)).toContainText("Unreachable");
    if (!isMobile) {
      await expect(page.getByRole("banner").getByRole("status")).toHaveText("API unreachable");
    }
  });

  test("lets live updates be paused and resumed", async ({ page, context, baseURL }) => {
    await staffSession(context, baseURL!);
    await page.goto("/operator");

    const panel = page.getByRole("region", { name: "Service status" });
    await expect(panel.getByText("Live, every 5 seconds")).toBeVisible();

    await panel.getByRole("button", { name: "Pause" }).click();
    await expect(panel.getByText("Updates paused")).toBeVisible();

    await panel.getByRole("button", { name: "Resume" }).click();
    await expect(panel.getByText("Live, every 5 seconds")).toBeVisible();
  });

  test("lists the dead letters and replays one, which leaves the list", async ({
    page,
    context,
    baseURL,
  }) => {
    await staffSession(context, baseURL!);
    await page.goto("/operator");

    const list = page.getByRole("region", { name: "Dead-lettered webhooks" }).getByRole("listitem");
    await expect(list).toHaveCount(3);
    await expect(list.first()).toContainText("payment.settled to hooks.example.com");
    await expect(list.first()).toContainText("12 attempts, last: HTTP 503");

    await list.first().getByRole("button", { name: "Replay" }).click();

    await expect(page.getByRole("main").getByRole("status")).toHaveText(
      "Delivery 103 is back on the queue; the worker sends it within a few seconds.",
    );
    await expect(list).toHaveCount(2);

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });

  test("keeps the service status on screen when the dead letters cannot be read", async ({
    page,
    context,
    baseURL,
  }) => {
    // In an outage the operator needs the panel most; a failed list must not take the page with it.
    await staffSession(context, baseURL!, "staff-down");
    await page.goto("/operator");

    await expect(
      page.getByRole("region", { name: "Service status" }).getByRole("listitem"),
    ).toHaveCount(3);
    await expect(
      page.getByRole("region", { name: "Dead-lettered webhooks" }).getByRole("alert"),
    ).toContainText("The dead letters could not be read.");
  });

  test("leaves the operator header behind on the way to the merchant sign-in", async ({
    page,
    isMobile,
  }) => {
    test.skip(isMobile, "the header's area badge shows from the sm breakpoint");
    await page.goto("/operator/login");
    await expect(page.getByRole("banner").getByText("Operator", { exact: true })).toBeVisible();

    await page.getByRole("link", { name: "the merchant sign-in" }).click();

    await expect(page).toHaveURL("/login");
    await expect(page.getByRole("banner").getByText("Operator", { exact: true })).toHaveCount(0);
    await expect(
      page.getByRole("banner").getByText(/API (ready|unreachable|not ready)/),
    ).toHaveCount(0);
  });

  test("signs out to the staff sign-in", async ({ page, context, baseURL, isMobile }) => {
    test.skip(isMobile, "the same button at every width; once is enough");
    await staffSession(context, baseURL!);
    await page.goto("/operator");

    await page.getByRole("button", { name: "Sign out" }).click();

    await expect(page).toHaveURL("/operator/login");
    await page.goto("/operator");
    await expect(page).toHaveURL("/operator/login");
  });
});
