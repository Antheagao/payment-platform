"use server";

import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { signIn } from "@/lib/auth-api";
import { clientAddress } from "@/lib/client-address";
import { safeRedirectTarget } from "@/lib/redirect-target";
import { clearToken, saveToken } from "@/lib/session";

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

/**
 * Signs in from the form. A Server Action is a public endpoint, so it trusts nothing it is sent: the
 * redirect target is checked, and the token never leaves the server except in the HttpOnly cookie.
 */
export async function signInAction(_previous: SignInState, form: FormData): Promise<SignInState> {
  const email = String(form.get("email") ?? "").trim();
  const password = String(form.get("password") ?? "");
  const target = safeRedirectTarget(form.get("next"));

  if (!email) return { error: "email_required", field: "email", email };
  if (email.length > emailMaxLength || !email.includes("@"))
    return { error: "email_invalid", field: "email", email };
  if (!password) return { error: "password_required", field: "password", email };
  if (password.length > passwordMaxLength) return { error: "invalid_credentials", email };

  const address = clientAddress((await headers()).get("x-forwarded-for"));
  const result = await signIn(email, password, address);
  switch (result.kind) {
    case "signed_in":
      await saveToken(result.token, result.expiresAt);
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

/** Forgets the token on this browser. The token itself stays valid until it expires (ADR 004). */
export async function signOutAction(): Promise<void> {
  await clearToken();
  redirect("/login");
}
