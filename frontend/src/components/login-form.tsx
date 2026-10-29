"use client";

import { useActionState } from "react";
import { signInAction, type SignInError, type SignInState } from "@/app/login/actions";
import { buttonClasses } from "@/components/ui/button";

const messages: Record<SignInError, string> = {
  email_required: "Enter your email address.",
  email_invalid: "Enter an email address like name@example.com.",
  password_required: "Enter your password.",
  invalid_credentials: "That email and password do not match an account.",
  rate_limited: "Too many sign-in attempts.",
  unavailable: "The payments service did not answer. Try again in a moment.",
};

function messageFor(state: SignInState): string | undefined {
  if (!state.error) return undefined;
  if (state.error === "rate_limited" && state.retryAfterSeconds) {
    return `${messages.rate_limited} Try again in ${state.retryAfterSeconds} seconds.`;
  }
  return messages[state.error];
}

const inputClasses =
  "mt-1.5 block min-h-10 w-full rounded-md border border-border-strong bg-surface px-3 text-sm text-fg aria-invalid:border-danger";

/** The sign-in form: submits to the Server Action, keeps the email on a failure, and names the field at fault. */
export function LoginForm({ next }: { next: string }) {
  const [state, formAction, pending] = useActionState(signInAction, { email: "" });
  const message = messageFor(state);
  const emailError = state.field === "email" ? message : undefined;
  const passwordError = state.field === "password" ? message : undefined;
  const formError = state.field ? undefined : message;

  return (
    <form action={formAction} noValidate className="mt-6 space-y-5">
      <input type="hidden" name="next" value={next} />
      <div>
        <label htmlFor="email" className="block text-sm font-medium">
          Email
        </label>
        <input
          id="email"
          name="email"
          type="email"
          inputMode="email"
          autoComplete="username"
          required
          maxLength={254}
          defaultValue={state.email}
          aria-invalid={emailError ? true : undefined}
          aria-describedby={emailError ? "email-error" : undefined}
          className={inputClasses}
        />
        {emailError && (
          <p id="email-error" className="mt-1.5 text-sm text-danger">
            {emailError}
          </p>
        )}
      </div>
      <div>
        <label htmlFor="password" className="block text-sm font-medium">
          Password
        </label>
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          maxLength={1024}
          aria-invalid={passwordError ? true : undefined}
          aria-describedby={passwordError ? "password-error" : undefined}
          className={inputClasses}
        />
        {passwordError && (
          <p id="password-error" className="mt-1.5 text-sm text-danger">
            {passwordError}
          </p>
        )}
      </div>
      <div>
        {/* Kept mounted, so a screen reader announces each new message; it takes no space while empty. */}
        <p role="alert" className="mb-4 text-sm text-danger empty:mb-0">
          {formError}
        </p>
        <button type="submit" disabled={pending} className={buttonClasses({ className: "w-full" })}>
          {pending ? "Signing in…" : "Sign in"}
        </button>
      </div>
    </form>
  );
}
