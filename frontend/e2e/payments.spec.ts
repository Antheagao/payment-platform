import { expect, test, type BrowserContext, type Page } from "@playwright/test";

// The payments list against e2e/stub-api.mjs: 30 fixed payments, 25 to a page. The session cookie's value
// picks the stub's behavior: a stub token sees them all, "empty" none, "down" a failing API, "expired" a
// refused session.

async function session(context: BrowserContext, baseURL: string, token = "stub-e2e") {
  await context.addCookies([{ name: "pp_session", value: token, url: baseURL }]);
}

/** The rows in view, whichever layout the width chose: the table from lg, the cards below it. */
function rows(page: Page, isMobile: boolean) {
  return isMobile
    ? page.getByRole("main").getByRole("listitem")
    : page.getByRole("main").locator("tbody tr");
}

test.describe("payments list", () => {
  test("shows the newest 25, in each currency's own form, and pages to the rest", async ({
    page,
    context,
    baseURL,
    isMobile,
  }) => {
    await session(context, baseURL!);
    await page.goto("/payments");

    await expect(page.getByRole("heading", { level: 1, name: "Payments" })).toBeVisible();
    await expect(rows(page, isMobile)).toHaveCount(25);
    await expect(rows(page, isMobile).first()).toContainText("INV-3000");
    await expect(rows(page, isMobile).first()).toContainText("$10.00");
    await expect(rows(page, isMobile).filter({ hasText: "¥7,602" })).toHaveCount(1);

    await page.getByRole("link", { name: "Older payments" }).click();
    await expect(page).toHaveURL("/payments?cursor=25");
    await expect(rows(page, isMobile)).toHaveCount(5);
    await expect(page.getByRole("link", { name: "Older payments" })).toHaveCount(0);

    await page.getByRole("link", { name: "Newest payments" }).click();
    await expect(page).toHaveURL("/payments");
  });

  test("filters by status and currency, keeps them in the address, and counts them", async ({
    page,
    context,
    baseURL,
    isMobile,
  }) => {
    await session(context, baseURL!);
    await page.goto("/payments");

    await page.getByText("Filters", { exact: true }).click();
    await page.getByLabel("Failed").check();
    await page.getByLabel("Currency").selectOption("USD");
    await page.getByRole("button", { name: "Apply filters" }).click();

    await expect(page).toHaveURL("/payments?status=failed&currency=USD");
    await expect(page.getByText("2 active")).toBeVisible();
    await expect(page.getByLabel("Failed")).toBeChecked();
    for (const row of await rows(page, isMobile).all()) {
      await expect(row).toContainText("Failed");
      await expect(row).toContainText("$");
    }
    await expect(rows(page, isMobile)).not.toHaveCount(0);
  });

  test("marks a filter that cannot be used, and asks for nothing until it is fixed", async ({
    page,
    context,
    baseURL,
    isMobile,
  }) => {
    await session(context, baseURL!);
    await page.goto("/payments?min=10");

    const amount = page.getByLabel("Amount from");
    await expect(amount).toHaveAttribute("aria-invalid", "true");
    await expect(amount).toHaveAccessibleDescription("Choose a currency to filter by amount.");
    await expect(page.getByText("Fix the filters marked above to see payments.")).toBeVisible();
    await expect(rows(page, isMobile)).toHaveCount(0);
  });

  test("tells no match apart from no payments", async ({ page, context, baseURL }) => {
    await session(context, baseURL!);
    await page.goto("/payments?reference=NOPE");
    await expect(page.getByText("No payments match these filters.")).toBeVisible();
    await page.getByRole("main").getByRole("link", { name: "Clear filters" }).click();
    await expect(page).toHaveURL("/payments");

    await context.clearCookies();
    await session(context, baseURL!, "empty");
    await page.goto("/payments");
    await expect(page.getByText("No payments yet.")).toBeVisible();
  });

  test("says so when the API is down, and offers to try again", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, "down");
    await page.goto("/payments");

    await expect(
      page.getByRole("alert").filter({ hasText: "The payments service did not answer." }),
    ).toBeVisible();
    await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();
  });

  test("sends an ended session to sign in, and back to the same view", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!, "expired");
    await page.goto("/payments?status=settled");

    await expect(page).toHaveURL("/login?next=%2Fpayments%3Fstatus%3Dsettled&reason=expired");
    await page.getByLabel("Email").fill("demo@example.com");
    await page.getByLabel("Password").fill("LocalOnly_Demo_2026!");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL("/payments?status=settled");
  });

  test("marks Payments as the current section in the header", async ({
    page,
    context,
    baseURL,
    isMobile,
  }) => {
    test.skip(isMobile, "the header's sections show from the sm breakpoint");
    await session(context, baseURL!);
    await page.goto("/payments");

    await expect(
      page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Payments" }),
    ).toHaveAttribute("aria-current", "page");
  });

  test("reaches payments from the home page at any width, and never scrolls sideways", async ({
    page,
    context,
    baseURL,
  }) => {
    await session(context, baseURL!);
    await page.goto("/");
    await page.getByRole("link", { name: "View payments" }).click();

    await expect(page).toHaveURL("/payments");
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBe(0);
  });
});
