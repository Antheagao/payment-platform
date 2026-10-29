import { randomUUID } from "node:crypto";
import { expect, test, type BrowserContext, type Page } from "@playwright/test";

// One payment's page against e2e/stub-api.mjs. Each test that moves money signs in with a token of its own,
// so the stub keeps its moves apart from every other test's; a token that starts with "stub-decline" has
// every move declined.

const authorized = "019a2c00-0000-7000-8000-000000002000"; // INV-3000, $10.00
const captured = "019a2c00-0000-7000-8000-000000002001"; // INV-3001, €11.25
const settled = "019a2c00-0000-7000-8000-000000002002"; // INV-3002, ¥7,602
const failed = "019a2c00-0000-7000-8000-000000002003"; // INV-3003, $13.75

async function session(context: BrowserContext, baseURL: string, token = "stub-e2e") {
  await context.addCookies([{ name: "pp_session", value: token, url: baseURL }]);
}

const history = (page: Page) => page.getByRole("region", { name: "History" }).getByRole("listitem");

/** What the page announces about a move; the route announcer is an alert too, so it is left out. */
const outcome = (page: Page, role: "status" | "alert") => page.getByRole("main").getByRole(role);

test.describe("payment detail", () => {
  test("opens from its row in the list, with its card and every change to it", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!);
    await page.goto("/payments");
    await page.getByRole("main").getByRole("link", { name: "INV-3001" }).click();

    await expect(page).toHaveURL(`/payments/${captured}`);
    await expect(page.getByRole("heading", { level: 1, name: "INV-3001" })).toBeVisible();
    // The list stays mounted, hidden, after the client navigation (Next.js keeps the route's state), so only
    // what is on screen counts.
    await expect(page.getByText("€11.25", { exact: true }).filter({ visible: true })).toBeVisible();
    await expect(page.getByText("Claimed; waiting for settlement.")).toBeVisible();
    await expect(page.getByText("Mastercard •••• 4444, expires 12/2028")).toBeVisible();
    await expect(history(page)).toHaveText([
      /Created as pending.*Merchant user 019a2c00/,
      /Pending to authorized/,
      /Authorized to captured.*Correlation id 0af7651916cd43dd8448eb211c803192/,
    ]);

    // Nothing is wider than the screen, at 360px as at 1440px.
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
      page.viewportSize()!.width,
    );

    await page.getByRole("link", { name: "← Payments" }).click();
    await expect(page).toHaveURL("/payments");
  });

  test("captures an authorized payment, then refunds it after asking again", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, `stub-${randomUUID()}`);
    await page.goto(`/payments/${authorized}`);

    await page.getByRole("button", { name: "Capture $10.00" }).click();
    await expect(outcome(page, "status")).toHaveText(
      "Captured. The funds are claimed and settle with the next batch.",
    );
    await expect(page.getByText("Claimed; waiting for settlement.")).toBeVisible();
    await expect(history(page).last()).toContainText("Authorized to captured");
    await expect(page.getByRole("button", { name: "Capture $10.00" })).toHaveCount(0);

    // The refund waits behind a second step.
    const refund = page.getByRole("button", { name: "Refund $10.00" });
    await expect(refund).toBeHidden();
    await page.getByText("Refund…", { exact: true }).click();
    await expect(page.getByText("A refund cannot be undone.")).toBeVisible();
    await refund.click();

    await expect(outcome(page, "status")).toHaveText(
      "Refunded. The money is on its way back to the cardholder.",
    );
    await expect(page.getByText("Returned to the cardholder.")).toBeVisible();
    await expect(history(page).last()).toContainText("Captured to refunded");
    await expect(page.getByText("Refund…", { exact: true })).toHaveCount(0);

    await page.reload();
    await expect(page.getByText("No moves are open for a payment in this status.")).toBeVisible();
  });

  test("says when the card network declines, and leaves the payment as it was", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, `stub-decline-${randomUUID()}`);
    await page.goto(`/payments/${authorized}`);

    await page.getByRole("button", { name: "Capture $10.00" }).click();
    await expect(outcome(page, "alert")).toHaveText(
      "The card network declined the capture. Nothing changed.",
    );
    await expect(page.getByText("Approved; the funds are reserved.")).toBeVisible();
    await expect(history(page)).toHaveCount(2);
    await expect(page.getByRole("button", { name: "Capture $10.00" })).toBeEnabled();
  });

  test("a page left open after the payment moved says so, and shows where it is now", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, `stub-${randomUUID()}`);
    const stale = await context.newPage();
    await stale.goto(`/payments/${authorized}`);
    await page.goto(`/payments/${authorized}`);
    await page.getByRole("button", { name: "Capture $10.00" }).click();
    await expect(outcome(page, "status")).toContainText("Captured.");

    await stale.getByRole("button", { name: "Capture $10.00" }).click();
    await expect(outcome(stale, "alert")).toHaveText(
      "This payment can no longer be captured: it is not authorized any more.",
    );
    await expect(stale.getByText("Claimed; waiting for settlement.")).toBeVisible();
    await expect(history(stale)).toHaveCount(3);
  });

  test("shows why a payment failed, and offers no move", async ({ page, context, baseURL }) => {
    await session(context, baseURL!);
    await page.goto(`/payments/${failed}`);

    await expect(page.getByText("Declined; nothing was charged.")).toBeVisible();
    await expect(
      page.getByRole("definition").filter({ hasText: "The card was declined." }),
    ).toBeVisible();
    await expect(history(page).last()).toContainText("Pending to failed");
    await expect(history(page).last()).toContainText("The card was declined.");
    await expect(page.getByText("No moves are open for a payment in this status.")).toBeVisible();
    await expect(page.getByRole("main").getByRole("button")).toHaveCount(0);
  });

  test("names the job that settled a payment", async ({ page, context, baseURL }) => {
    await session(context, baseURL!);
    await page.goto(`/payments/${settled}`);

    await expect(history(page).last()).toContainText("Captured to settled");
    await expect(history(page).last()).toContainText("The settlement job");
    await expect(page.getByText("Refund…", { exact: true })).toBeVisible();
  });

  test("is not found for an id that is not one of the merchant's, or not an id", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!);
    for (const id of ["019a2c00-0000-7000-8000-000000009999", "INV-3000"]) {
      // Every page streams (ADR 009), so the status is already 200 when the page finds nothing; Next.js
      // marks the page noindex instead (docs/tradeoffs.md).
      await page.goto(`/payments/${id}`);
      await expect(page.locator('meta[name="robots"][content="noindex"]').first()).toBeAttached();
      await expect(
        page.getByRole("heading", { level: 1, name: "Payment not found" }),
      ).toBeVisible();
    }
    await page.getByRole("link", { name: "Back to payments" }).click();
    await expect(page).toHaveURL("/payments");
  });

  test("says so when the API is down, and offers to try again", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, "down");
    await page.goto(`/payments/${captured}`);

    await expect(
      page.getByRole("alert").filter({ hasText: "The payments service did not answer." }),
    ).toBeVisible();
    await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();
  });

  test("sends an ended session to sign in, and back to the payment", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, "expired");
    await page.goto(`/payments/${captured}`);

    await expect(page).toHaveURL(
      `/login?next=${encodeURIComponent(`/payments/${captured}`)}&reason=expired`,
    );
    await page.getByLabel("Email").fill("demo@example.com");
    await page.getByLabel("Password").fill("LocalOnly_Demo_2026!");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(`/payments/${captured}`);
    await expect(page.getByRole("heading", { level: 1, name: "INV-3001" })).toBeVisible();
  });
});
