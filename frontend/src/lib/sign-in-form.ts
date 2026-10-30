// The sign-in form's server side, shared by the merchant login and the staff login (ADR 004, ADR 011): the
// same checks, the same answers by the API's code, and a redirect only to a path on this site.
import "server-only";
import { headers } from "next/headers";
import { redirect } from "next/navigation";
import type { SignInResult } from "@/lib/auth-api";
import { clientAddress } from "@/lib/client-address";
import { safeRedirectTarget } from "@/lib/redirect-target";

/** Why a sign-in did not go through, by the API's code or the form's own check. */
export type SignInError =
  | "email_required"
  | "email_invalid"
  | "password_required"
  | "invalid_credentials"
  | "rate_limited"
  | "unavailable";

export type SignInState = {
  error?: SignInError;
  /** The field the error belongs to, for `aria-invalid`; absent for an error about the whole form. */
  field?: "email" | "password";
  retryAfterSeconds?: number;
  /** What was typed, so a failed attempt never clears the email. */
  email: string;
};

// The API's own limits (MerchantUser and the login request); checked here only to answer sooner.
const emailMaxLength = 254;
const passwordMaxLength = 1024;

type SignInWith = {
  signIn: (email: string, password: string, clientAddress?: string) => Promise<SignInResult>;
  save: (token: string, expiresAt: Date) => Promise<void>;
  /** Where to go when the form names nowhere, or nowhere this login may send the browser. */
  home: string;
  /** Paths this login may return to, beside its home; the browser asked for them before it signed in. */
  allows: (path: string) => boolean;
};

/**
 * Signs in from a submitted form. A Server Action is a public endpoint, so it trusts nothing it is sent: the
 * redirect target is checked, and the token never leaves the server except in the HttpOnly cookie.
 */
export async function signInFromForm(form: FormData, login: SignInWith): Promise<SignInState> {
  const email = String(form.get("email") ?? "").trim();
  const password = String(form.get("password") ?? "");
  const asked = safeRedirectTarget(form.get("next"));
  const target = asked !== "/" && login.allows(asked) ? asked : login.home;

  if (!email) return { error: "email_required", field: "email", email };
  if (email.length > emailMaxLength || !email.includes("@"))
    return { error: "email_invalid", field: "email", email };
  if (!password) return { error: "password_required", field: "password", email };
  if (password.length > passwordMaxLength) return { error: "invalid_credentials", email };

  const address = clientAddress((await headers()).get("x-forwarded-for"));
  const result = await login.signIn(email, password, address);
  switch (result.kind) {
    case "signed_in":
      await login.save(result.token, result.expiresAt);
      redirect(target);
    case "invalid_credentials":
      return { error: "invalid_credentials", email };
    case "rate_limited":
      return { error: "rate_limited", retryAfterSeconds: result.retryAfterSeconds, email };
    case "invalid_request":
      return result.fields.email
        ? { error: "email_invalid", field: "email", email }
        : { error: "invalid_credentials", email };
    case "unavailable":
      return { error: "unavailable", email };
  }
}
