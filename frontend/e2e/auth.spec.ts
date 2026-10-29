import { expect, test, type Page } from "@playwright/test";

// Sign-in against e2e/stub-api.mjs, which spells its answers as the real API does (ADR 004).

/** Fails the test on any script or style the Content-Security-Policy refused. */
function watchPolicy(page: Page): string[] {
  const refused: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error" && message.text().includes("Content Security Policy")) {
      refused.push(message.text());
    }
  });
  return refused;
}

async function signIn(page: Page, email: string, password: string) {
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

test.describe("sign-in", () => {
  test("sends a signed-out browser to the login page and back where it was going", async ({
    page,
  }) => {
    const refused = watchPolicy(page);

    await page.goto("/no-such-page?x=1");
    await expect(page).toHaveURL("/login?next=%2Fno-such-page%3Fx%3D1");
    await expect(page.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();

    await signIn(page, "demo@example.com", "LocalOnly_Demo_2026!");

    await expect(page).toHaveURL("/no-such-page?x=1");
    await expect(page.getByRole("heading", { level: 1, name: "Page not found" })).toBeVisible();
    expect(refused).toEqual([]);
  });

  test("signs in from the home page and shows who is signed in", async ({ page, context }) => {
    const refused = watchPolicy(page);

    await page.goto("/");
    await expect(page).toHaveURL("/login");
    await signIn(page, "demo@example.com", "LocalOnly_Demo_2026!");

    await expect(page).toHaveURL("/");
    const header = page.getByRole("banner");
    await expect(header.getByRole("button", { name: "Sign out" })).toBeVisible();
    const cookie = (await context.cookies()).find((candidate) => candidate.name === "pp_session");
    expect(cookie).toMatchObject({ httpOnly: true, secure: true, sameSite: "Strict" });
    expect(await page.evaluate(() => document.cookie)).not.toContain("pp_session");
    expect(refused).toEqual([]);
  });

  test("names the merchant in the header on a wide screen", async ({ page, isMobile }) => {
    test.skip(isMobile, "the name is hidden below the sm breakpoint");
    await page.goto("/login");
    await signIn(page, "demo@example.com", "LocalOnly_Demo_2026!");

    await expect(page.getByRole("banner").getByText("Demo Merchant")).toBeVisible();
  });

  test("keeps the email and says why when the password is wrong", async ({ page }) => {
    await page.goto("/login");

    await signIn(page, "demo@example.com", "not-the-password");

    await expect(page.getByRole("main").getByRole("alert")).toHaveText(
      "That email and password do not match an account.",
    );
    await expect(page.getByLabel("Email")).toHaveValue("demo@example.com");
    await expect(page).toHaveURL("/login");
  });

  test("marks the field at fault when it is missing", async ({ page }) => {
    await page.goto("/login");

    await signIn(page, "demo@example.com", "");

    const password = page.getByLabel("Password");
    await expect(password).toHaveAttribute("aria-invalid", "true");
    await expect(password).toHaveAccessibleDescription("Enter your password.");
  });

  test("says how long to wait when sign-in is rate limited", async ({ page }) => {
    await page.goto("/login");

    await signIn(page, "limited@example.com", "anything");

    await expect(page.getByRole("main").getByRole("alert")).toHaveText(
      "Too many sign-in attempts. Try again in 42 seconds.",
    );
  });

  test("signs out, and the home page then asks for sign-in again", async ({ page }) => {
    await page.goto("/login");
    await signIn(page, "demo@example.com", "LocalOnly_Demo_2026!");
    await expect(page).toHaveURL("/");

    await page.getByRole("banner").getByRole("button", { name: "Sign out" }).click();

    await expect(page).toHaveURL("/login");
    await page.goto("/");
    await expect(page).toHaveURL("/login");
  });

  test("offers sign-in again when the session has ended", async ({ page, context, baseURL }) => {
    await context.addCookies([{ name: "pp_session", value: "expired", url: baseURL! }]);
    await page.goto("/");

    await page.getByRole("banner").getByRole("link", { name: "Sign in" }).click();

    await expect(page).toHaveURL("/login?reason=expired");
    await expect(page.getByRole("status").filter({ hasText: "Your session ended" })).toBeVisible();
  });

  test("keeps the header quiet on the login page itself, even with an ended session", async ({
    page,
    context,
    baseURL,
  }) => {
    await context.addCookies([{ name: "pp_session", value: "expired", url: baseURL! }]);
    await page.goto("/login");

    const header = page.getByRole("banner");
    await expect(header.getByRole("link", { name: "Payment Platform" })).toBeVisible();
    await expect(header.getByRole("link", { name: "Sign in" })).toHaveCount(0);
    await expect(header.getByRole("button", { name: "Sign out" })).toHaveCount(0);
  });

  test("shows the local demo login where the stack turns it on", async ({ page }) => {
    await page.goto("/login");

    await expect(page.getByText("LocalOnly_Demo_2026!")).toBeVisible();
  });
});
