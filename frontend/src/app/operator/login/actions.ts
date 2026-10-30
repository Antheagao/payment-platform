"use server";

import { redirect } from "next/navigation";
import { staffSignIn } from "@/lib/auth-api";
import { signInFromForm, type SignInState } from "@/lib/sign-in-form";
import { clearStaffToken, saveStaffToken, staffArea } from "@/lib/staff-session";

/** Signs a platform operator in (ADR 011), and back to the operator page they asked for, or its home. */
export async function staffSignInAction(
  _previous: SignInState,
  form: FormData,
): Promise<SignInState> {
  return signInFromForm(form, {
    signIn: staffSignIn,
    save: saveStaffToken,
    home: staffArea,
    // A staff session opens only the operator area; a merchant page needs a merchant's sign-in.
    allows: (path) => path === staffArea || path.startsWith(`${staffArea}/`),
  });
}

/** Forgets the staff token on this browser; it stays valid until it expires, like a merchant's (ADR 004). */
export async function staffSignOutAction(): Promise<void> {
  await clearStaffToken();
  redirect(`${staffArea}/login`);
}
