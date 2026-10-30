// The dashboard's two areas (ADR 011): the merchant's pages, and the platform staff's under one path, each with a
// session of its own. A plain module, so the proxy can use it as well as server components.

/** Where the operator area lives; its session cookie is sent on this path only. */
export const staffArea = "/operator";

/** True for a path in the operator area. */
export function inOperatorArea(pathname: string): boolean {
  return pathname === staffArea || pathname.startsWith(`${staffArea}/`);
}
