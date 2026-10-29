import { expect, test, type Page } from "@playwright/test";

// The golden path through the whole composed stack, as a merchant walks it: sign in, find a payment, read its
// history, capture and refund one, and see the signed webhooks the worker sent for both moves. It reads the
// seeded demo data (DemoPayments) and works on any authorized payment, so it passes on a used volume too.

const receiver = process.env.WEBHOOK_RECEIVER_URL ?? "http://localhost:8090";

type ReceivedEvent = { type: string; payment_id: string; outcome: string };

async function webhooksFor(paymentId: string): Promise<ReceivedEvent[]> {
  const response = await fetch(`${receiver}/events`);
  const events = (await response.json()) as ReceivedEvent[];
  return events.filter((event) => event.payment_id === paymentId);
}

const history = (page: Page) => page.getByRole("region", { name: "History" }).getByRole("listitem");

test("a merchant signs in, reads a payment's history, captures and refunds one, and is sent its webhooks", async ({
  page,
}) => {
  await test.step("sign in, and see the API ready", async () => {
    await page.goto("/payments");
    await expect(page).toHaveURL(/\/login\?next=%2Fpayments/);
    await page.getByLabel("Email").fill("demo@example.com");
    await page.getByLabel("Password").fill("LocalOnly_Demo_2026!"); // DemoSeed's local-only login
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL("/payments");
    await expect(page.getByRole("banner").getByText("Demo Merchant")).toBeVisible();
    await expect(page.getByRole("banner").getByRole("status")).toHaveText(/API ready/);
  });

  await test.step("find INV-1004 and read its whole lifecycle", async () => {
    await page.getByText("Filters", { exact: true }).click();
    await page.getByLabel("Reference").fill("INV-1004");
    await page.getByRole("button", { name: "Apply filters" }).click();
    await expect(page).toHaveURL("/payments?reference=INV-1004");
    await page.getByRole("main").getByRole("link", { name: "INV-1004" }).click();

    await expect(page.getByRole("heading", { level: 1, name: "INV-1004" })).toBeVisible();
    await expect(history(page)).toHaveText([
      /Created as pending/,
      /Pending to authorized/,
      /Authorized to captured/,
      /Captured to settled.*The settlement job/,
      /Settled to refunded/,
    ]);
  });

  let paymentId = "";
  await test.step("open an authorized payment and capture it", async () => {
    await page.goto("/payments?status=authorized");
    const first = page.getByRole("main").locator("tbody tr").first().getByRole("link");
    await first.click();
    await expect(page.getByText("Approved; the funds are reserved.")).toBeVisible();
    paymentId = page.url().split("/").at(-1)!;
    const before = await history(page).count();

    await page.getByRole("button", { name: /^Capture / }).click();
    await expect(page.getByRole("main").getByRole("status")).toHaveText(
      "Captured. The funds are claimed and settle with the next batch.",
    );
    await expect(history(page)).toHaveCount(before + 1);
    await expect(history(page).last()).toContainText("Authorized to captured");
    await expect(history(page).last()).toContainText(/Correlation id [0-9a-f]{32}/);
  });

  await test.step("refund it, after the second step", async () => {
    await page.getByText("Refund…", { exact: true }).click();
    await page.getByRole("button", { name: /^Refund / }).click();
    await expect(page.getByRole("main").getByRole("status")).toHaveText(
      "Refunded. The money is on its way back to the cardholder.",
    );
    await expect(history(page).last()).toContainText("Captured to refunded");
    await expect(page.getByText("Returned to the cardholder.")).toBeVisible();
  });

  await test.step("the merchant's endpoint received both moves, signed", async () => {
    // Through the outbox and the worker, so a few seconds after the moves (ADR 003).
    await expect
      .poll(
        async () =>
          (await webhooksFor(paymentId)).map((event) => `${event.type} ${event.outcome}`).sort(),
        {
          timeout: 30_000,
        },
      )
      .toEqual(expect.arrayContaining(["payment.captured verified", "payment.refunded verified"]));
  });

  await test.step("an id that is not the merchant's is not found, and sign-out ends the session", async () => {
    await page.goto("/payments/019a2c00-0000-7000-8000-000000009999");
    await expect(page.getByRole("heading", { level: 1, name: "Payment not found" })).toBeVisible();

    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page).toHaveURL("/login");
    await page.goto(`/payments/${paymentId}`);
    await expect(page).toHaveURL(/\/login\?next=/);
  });
});
