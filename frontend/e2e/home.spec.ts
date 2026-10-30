import { expect, test } from "@playwright/test";

test.describe("dashboard shell", () => {
  // Signed in as the stub's demo user: the proxy lets a session cookie through, and the header asks /me.
  test.beforeEach(async ({ context, baseURL }) => {
    await context.addCookies([{ name: "pp_session", value: "stub-e2e", url: baseURL! }]);
  });

  test("renders the heading and the full lifecycle legend", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("heading", { level: 1, name: "Merchant dashboard" })).toBeVisible();
    const legend = page.getByRole("region", { name: "Payment lifecycle" });
    for (const label of ["Pending", "Authorized", "Captured", "Settled", "Failed", "Refunded"]) {
      await expect(legend.getByText(label, { exact: true })).toBeVisible();
    }
  });

  test("tells a merchant only that payments are delayed, and nothing of the services behind it", async ({
    page,
  }) => {
    // The stub drops every health probe, so to the dashboard the API is unreachable.
    await page.goto("/");

    await expect(page.getByRole("main").getByRole("status")).toContainText("Payments are delayed.");
    await expect(page.getByRole("region", { name: "Service status" })).toHaveCount(0);
    await expect(page.getByText(/database|worker/i)).toHaveCount(0);
    await expect(
      page.getByRole("banner").getByText(/API (unreachable|ready|not ready)/),
    ).toHaveCount(0);
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
    expect(headers["content-security-policy"]).toMatch(
      /script-src 'self' 'nonce-[^']+' 'strict-dynamic'/,
    );
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
