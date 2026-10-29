import { expect, test } from "@playwright/test";

test.describe("dashboard shell", () => {
  test("renders the heading and the full lifecycle legend", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("heading", { level: 1, name: "Merchant dashboard" })).toBeVisible();
    const legend = page.getByRole("region", { name: "Payment lifecycle" });
    for (const label of ["Pending", "Authorized", "Captured", "Settled", "Failed", "Refunded"]) {
      await expect(legend.getByText(label, { exact: true })).toBeVisible();
    }
  });

  test("says plainly when the API cannot be reached", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("status")).toHaveText("API unreachable");
  });

  test("shows each service on its own, and the database as unknown while the API is down", async ({
    page,
  }) => {
    await page.goto("/");

    const tiles = page.getByRole("region", { name: "Service status" }).getByRole("listitem");
    await expect(tiles).toHaveCount(3);
    await expect(tiles.nth(0)).toContainText("Payments API");
    await expect(tiles.nth(0)).toContainText("Unreachable");
    await expect(tiles.nth(1)).toContainText("Database");
    await expect(tiles.nth(1)).toContainText("Unknown");
    await expect(tiles.nth(2)).toContainText("Background worker");
    await expect(tiles.nth(2)).toContainText("Unreachable");
  });

  test("lets live updates be paused and resumed", async ({ page }) => {
    await page.goto("/");

    const panel = page.getByRole("region", { name: "Service status" });
    await expect(panel.getByText("Live, every 5 seconds")).toBeVisible();

    await panel.getByRole("button", { name: "Pause" }).click();
    await expect(panel.getByText("Updates paused")).toBeVisible();

    await panel.getByRole("button", { name: "Resume" }).click();
    await expect(panel.getByText("Live, every 5 seconds")).toBeVisible();
  });

  test("offers a skip link on the first Tab", async ({ page }) => {
    await page.goto("/");

    await page.keyboard.press("Tab");

    const skip = page.getByRole("link", { name: "Skip to content" });
    await expect(skip).toBeFocused();
    await expect(skip).toBeVisible();
  });

  test("sends the security headers and hides the framework", async ({ page }) => {
    const response = await page.goto("/");
    const headers = response?.headers() ?? {};

    expect(headers["x-frame-options"]).toBe("DENY");
    expect(headers["x-content-type-options"]).toBe("nosniff");
    expect(headers["content-security-policy"]).toContain("frame-ancestors 'none'");
    expect(headers["x-powered-by"]).toBeUndefined();
  });

  test("never scrolls sideways", async ({ page }) => {
    await page.goto("/");

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });

  test("answers unknown paths with the not-found page", async ({ page }) => {
    const response = await page.goto("/no-such-page");

    expect(response?.status()).toBe(404);
    await expect(page.getByRole("heading", { level: 1, name: "Page not found" })).toBeVisible();
  });
});
