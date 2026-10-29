// The signed-in session: the API's access token in a cookie only the dashboard's server reads (ADR 004).
// Browser code never sees the token: the cookie is HttpOnly, and every API call is made from server code.
import "server-only";
import { cookies } from "next/headers";

export const sessionCookie = "pp_session";

/** The access token, or undefined when the browser sent no session cookie. */
export async function readToken(): Promise<string | undefined> {
  return (await cookies()).get(sessionCookie)?.value || undefined;
}

/**
 * Keeps the token until it expires. `Strict` keeps the cookie off every request another site starts, and
 * `Secure` applies on plain http too for `localhost`, which browsers treat as a secure context.
 */
export async function saveToken(token: string, expiresAt: Date): Promise<void> {
  (await cookies()).set(sessionCookie, token, {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    expires: expiresAt,
  });
}

export async function clearToken(): Promise<void> {
  (await cookies()).delete(sessionCookie);
}
