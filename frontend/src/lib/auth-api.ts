// Sign-in and the current user, against the API's /auth/login and /me (ADR 004). Errors are read by
// their problem-details `code`, never by their text (ADR 006).
import "server-only";
import { apiOrigin } from "@/lib/api";

export type SignInResult =
  | { kind: "signed_in"; token: string; expiresAt: Date }
  | { kind: "invalid_credentials" }
  | { kind: "invalid_request"; fields: Readonly<Record<string, readonly string[]>> }
  | { kind: "rate_limited"; retryAfterSeconds: number }
  | { kind: "unavailable" };

export type CurrentUser = {
  userId: string;
  merchantId: string;
  email: string;
  merchantName: string;
};

export type CurrentUserResult =
  | { kind: "user"; user: CurrentUser }
  | { kind: "signed_out"; expired: boolean }
  | { kind: "unavailable" };

const timeout = () => AbortSignal.timeout(5_000);

/** Exchanges an email and password for an access token. Never throws. */
export async function signIn(email: string, password: string): Promise<SignInResult> {
  let response: Response;
  try {
    response = await fetch(`${apiOrigin}/api/v1/auth/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
      signal: timeout(),
    });
  } catch {
    return { kind: "unavailable" };
  }

  const body = await readJson(response);
  if (response.ok) {
    const token = stringField(body, "access_token");
    const expiresAt = new Date(stringField(body, "expires_at") ?? "");
    return token && !Number.isNaN(expiresAt.getTime())
      ? { kind: "signed_in", token, expiresAt }
      : { kind: "unavailable" };
  }

  switch (stringField(body, "code")) {
    case "invalid_credentials":
      return { kind: "invalid_credentials" };
    case "rate_limited":
      return { kind: "rate_limited", retryAfterSeconds: retryAfter(response) };
    case "invalid_request":
      return { kind: "invalid_request", fields: fieldErrors(body) };
    default:
      return { kind: "unavailable" };
  }
}

/** Who the token belongs to; a refused token is signed out, a missing answer is unavailable. Never throws. */
export async function getCurrentUser(token: string): Promise<CurrentUserResult> {
  let response: Response;
  try {
    response = await fetch(`${apiOrigin}/api/v1/me`, {
      headers: { authorization: `Bearer ${token}` },
      cache: "no-store",
      signal: timeout(),
    });
  } catch {
    return { kind: "unavailable" };
  }

  const body = await readJson(response);
  if (response.status === 401 || response.status === 403) {
    return { kind: "signed_out", expired: stringField(body, "code") === "token_expired" };
  }
  const user = {
    userId: stringField(body, "user_id"),
    merchantId: stringField(body, "merchant_id"),
    email: stringField(body, "email"),
    merchantName: stringField(body, "merchant_name"),
  };
  return response.ok && user.userId && user.merchantId && user.email && user.merchantName
    ? { kind: "user", user: user as CurrentUser }
    : { kind: "unavailable" };
}

async function readJson(response: Response): Promise<unknown> {
  return response.json().catch(() => null);
}

function stringField(body: unknown, name: string): string | undefined {
  if (typeof body !== "object" || body === null || !(name in body)) return undefined;
  const value = (body as Record<string, unknown>)[name];
  return typeof value === "string" ? value : undefined;
}

function fieldErrors(body: unknown): Record<string, string[]> {
  if (typeof body !== "object" || body === null || !("errors" in body)) return {};
  const errors = (body as { errors: unknown }).errors;
  if (typeof errors !== "object" || errors === null) return {};
  const fields: Record<string, string[]> = {};
  for (const [field, codes] of Object.entries(errors)) {
    if (Array.isArray(codes)) fields[field] = codes.filter((code) => typeof code === "string");
  }
  return fields;
}

function retryAfter(response: Response): number {
  const seconds = Number(response.headers.get("retry-after"));
  return Number.isFinite(seconds) && seconds > 0 ? Math.ceil(seconds) : 60;
}
