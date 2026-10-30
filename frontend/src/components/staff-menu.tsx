import { headers } from "next/headers";
import { staffSignOutAction } from "@/app/operator/login/actions";
import { buttonClasses } from "@/components/ui/button";
import { getCurrentStaff } from "@/lib/auth-api";
import { readStaffToken } from "@/lib/staff-session";

/**
 * Who is signed in to the operator area, and the way out: the staff counterpart of `UserMenu` (ADR 011). Like
 * it, it never redirects, and stays quiet on its own sign-in page.
 */
export async function StaffMenu() {
  const token = await readStaffToken();
  if (!token || (await headers()).get("x-pathname") === "/operator/login") return null;

  const result = await getCurrentStaff(token);
  if (result.kind === "signed_out") {
    return (
      // A full page load: the header, which the root layout keeps across client navigations, must render
      // again for the sign-in page, where it stays quiet.
      <a
        href="/operator/login?reason=expired"
        className={buttonClasses({ variant: "secondary", size: "sm" })}
      >
        Sign in
      </a>
    );
  }

  return (
    <div className="flex min-w-0 items-center gap-3">
      {result.kind === "staff" && (
        <p className="hidden min-w-0 text-right text-sm leading-tight sm:block">
          <span className="block truncate font-medium">Platform staff</span>
          <span className="block truncate text-muted">{result.staff.email}</span>
        </p>
      )}
      <form action={staffSignOutAction}>
        <button type="submit" className={buttonClasses({ variant: "secondary", size: "sm" })}>
          Sign out
        </button>
      </form>
    </div>
  );
}
