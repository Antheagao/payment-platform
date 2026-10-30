import { NextResponse, type NextRequest } from "next/server";
import { inOperatorArea, staffArea } from "@/lib/areas";

// The session cookies' names, repeated from src/lib/session.ts and src/lib/staff-session.ts, which are
// server-only and cannot load here.
const sessionCookie = "pp_session";
const staffSessionCookie = "pp_staff_session";

/** Pages a browser without a session may open: each area's sign-in. */
const publicPaths = new Set(["/login", `${staffArea}/login`]);

/** The policy for one response: scripts and styles only from this site and only with this request's nonce. */
export function contentSecurityPolicy(nonce: string, development: boolean): string {
  return [
    "default-src 'self'",
    // React needs eval in development only, to rebuild server error stacks in the browser.
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${development ? " 'unsafe-eval'" : ""}`,
    `style-src 'self' 'nonce-${nonce}'`,
    "img-src 'self' blob: data:",
    "font-src 'self'",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join("; ");
}

/**
 * Runs before every page (ADR 009): a fresh nonce and the policy that requires it, and a redirect to the
 * area's sign-in for a browser without that area's session (ADR 011): a merchant's session opens no operator
 * page, and a staff session no merchant page. The redirect is only a convenience: a cookie is not proof of a
 * session, so every read checks the token with the API itself.
 */
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const operator = inOperatorArea(pathname);
  const cookie = operator ? staffSessionCookie : sessionCookie;
  if (!publicPaths.has(pathname) && !request.cookies.has(cookie)) {
    const home = operator ? staffArea : "/";
    const login = new URL(operator ? `${staffArea}/login` : "/login", request.url);
    // Home needs no way back, unless it was asked for with a query that should survive the sign-in.
    if (pathname !== home || search) login.searchParams.set("next", `${pathname}${search}`);
    return NextResponse.redirect(login);
  }

  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const policy = contentSecurityPolicy(nonce, process.env.NODE_ENV === "development");
  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-nonce", nonce);
  // Server components cannot see the path; the header's session menu needs it to stay quiet on the login page.
  requestHeaders.set("x-pathname", pathname);
  // Next.js reads the nonce from the request's policy and puts it on every script it renders.
  requestHeaders.set("Content-Security-Policy", policy);
  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set("Content-Security-Policy", policy);
  return response;
}

export const config = {
  matcher: [
    {
      // Not the build's static files, which need no policy, nor prefetches, which render nothing.
      source: "/((?!_next/static|_next/image|favicon.ico).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
