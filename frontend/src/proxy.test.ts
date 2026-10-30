import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { contentSecurityPolicy, proxy } from "./proxy";

function request(path: string, cookie?: string) {
  return new NextRequest(new URL(path, "http://localhost:3080"), {
    headers: cookie ? { cookie } : {},
  });
}

describe("proxy", () => {
  it("sends a browser with no session to the login page, keeping where it was going", () => {
    const response = proxy(request("/payments?status=captured"));

    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe(
      "http://localhost:3080/login?next=%2Fpayments%3Fstatus%3Dcaptured",
    );
  });

  it("sends the home page to the login page without a next parameter", () => {
    expect(proxy(request("/")).headers.get("location")).toBe("http://localhost:3080/login");
  });

  it("lets the login page through without a session", () => {
    const response = proxy(request("/login"));

    expect(response.headers.get("location")).toBeNull();
    expect(response.headers.get("content-security-policy")).toContain("script-src 'self' 'nonce-");
  });

  it("keeps each area to its own session (ADR 011)", () => {
    const location = (path: string, cookie?: string) =>
      proxy(request(path, cookie)).headers.get("location");

    // The operator area needs a staff session; a merchant's opens nothing there.
    expect(location("/operator")).toBe("http://localhost:3080/operator/login");
    expect(location("/operator?tab=x", "pp_session=merchant")).toBe(
      "http://localhost:3080/operator/login?next=%2Foperator%3Ftab%3Dx",
    );
    expect(location("/operator", "pp_staff_session=staff")).toBeNull();
    expect(location("/operator/login")).toBeNull();
    // And a staff session opens no merchant page.
    expect(location("/payments", "pp_staff_session=staff")).toBe(
      "http://localhost:3080/login?next=%2Fpayments",
    );
    // A path that only starts like the area is not in it.
    expect(location("/operators", "pp_session=merchant")).toBeNull();
  });

  it("gives every page a fresh nonce and a policy that requires it", () => {
    const first = proxy(request("/", "pp_session=token")).headers.get("content-security-policy")!;
    const second = proxy(request("/", "pp_session=token")).headers.get("content-security-policy")!;
    const nonce = (policy: string) => /'nonce-([^']+)'/.exec(policy)![1];

    expect(nonce(first)).not.toBe(nonce(second));
    expect(first).toContain("frame-ancestors 'none'");
    expect(first).toContain("object-src 'none'");
    expect(first).not.toContain("unsafe-inline");
  });
});

describe("contentSecurityPolicy", () => {
  it("allows eval only in development, where React needs it", () => {
    expect(contentSecurityPolicy("n", true)).toContain("'unsafe-eval'");
    expect(contentSecurityPolicy("n", false)).not.toContain("unsafe-eval");
  });
});
