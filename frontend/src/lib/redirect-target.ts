/**
 * Where to send the browser after sign-in: the path it asked for, if that is a path on this site, or the
 * home page. Anything else (another origin, a protocol-relative `//host`, a backslash that some browsers
 * read as a slash, a control character) would make the login page an open redirect.
 */
export function safeRedirectTarget(value: unknown): string {
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//")) return "/";
  if (/[\\\u0000-\u001f\u007f]/.test(value)) return "/";
  if (value === "/login" || value.startsWith("/login?") || value.startsWith("/login/")) return "/";
  return value;
}
