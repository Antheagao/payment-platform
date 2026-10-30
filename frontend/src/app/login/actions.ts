"use server";

import { redirect } from "next/navigation";
import { signIn } from "@/lib/auth-api";
import { clearToken, saveToken } from "@/lib/session";
import { signInFromForm, type SignInState } from "@/lib/sign-in-form";

/** Signs a merchant's user in, and back to the page they asked for, or home. */
export async function signInAction(_previous: SignInState, form: FormData): Promise<SignInState> {
  return signInFromForm(form, {
    signIn,
    save: saveToken,
    home: "/",
    // The operator area has a login of its own (ADR 011); a merchant's session opens nothing there.
    allows: (path) => path !== "/operator" && !path.startsWith("/operator/"),
  });
}

/** Forgets the token on this browser. The token itself stays valid until it expires (ADR 004). */
export async function signOutAction(): Promise<void> {
  await clearToken();
  redirect("/login");
}
