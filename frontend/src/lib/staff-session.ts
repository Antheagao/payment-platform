// A platform operator's session (ADR 011): the API's staff token in a cookie of its own, apart from a merchant
// user's, and sent only to the operator area, so no merchant page ever receives it.
import "server-only";
import { cookies } from "next/headers";
import { staffArea } from "@/lib/areas";

export { staffArea };

export const staffSessionCookie = "pp_staff_session";

/** The staff token, or undefined when the browser sent no staff session cookie. */
export async function readStaffToken(): Promise<string | undefined> {
  return (await cookies()).get(staffSessionCookie)?.value || undefined;
}

/** Keeps the token until it expires, with the merchant session's flags (src/lib/session.ts). */
export async function saveStaffToken(token: string, expiresAt: Date): Promise<void> {
  (await cookies()).set(staffSessionCookie, token, {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: staffArea,
    expires: expiresAt,
  });
}

export async function clearStaffToken(): Promise<void> {
  (await cookies()).delete({ name: staffSessionCookie, path: staffArea });
}
