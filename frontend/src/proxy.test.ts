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
