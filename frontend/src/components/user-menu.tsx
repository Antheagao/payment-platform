import { headers } from "next/headers";
import { cache } from "react";
import { signOutAction } from "@/app/login/actions";
import { buttonClasses } from "@/components/ui/button";
import { getCurrentUser } from "@/lib/auth-api";
import { readToken } from "@/lib/session";

// Once per request, however many components ask.
const currentUser = cache(getCurrentUser);

/**
 * Who is signed in, and the way out. It never redirects: the header renders on the login page too, where a
 * redirect for a refused token would loop. Pages that need the user decide that themselves.
 */
export async function UserMenu() {
  const token = await readToken();
  // On the login page, an ended session's "Sign in" would only point at the page itself.
  if (!token || (await headers()).get("x-pathname") === "/login") return null;

  const result = await currentUser(token);
  if (result.kind === "signed_out") {
    return (
      // A full page load: the header, which the root layout keeps across client navigations, must render
      // again for the sign-in page, where it stays quiet.
      <a
        href="/login?reason=expired"
        className={buttonClasses({ variant: "secondary", size: "sm" })}
      >
        Sign in
      </a>
    );
  }

  return (
    <div className="flex min-w-0 items-center gap-3">
      {result.kind === "user" && (
        <p className="hidden min-w-0 text-right text-sm leading-tight sm:block">
          <span className="block truncate font-medium">{result.user.merchantName}</span>
          <span className="block truncate text-muted">{result.user.email}</span>
        </p>
      )}
      <form action={signOutAction}>
        <button type="submit" className={buttonClasses({ variant: "secondary", size: "sm" })}>
          Sign out
        </button>
      </form>
    </div>
  );
}

/** Holds the button's space while the menu streams, so the header does not shift, and shows nothing itself. */
export function UserMenuFallback() {
  return <div aria-hidden="true" className="h-8 w-20" />;
}
