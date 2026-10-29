import { describe, expect, it } from "vitest";
import { clientAddress } from "./client-address";

describe("clientAddress", () => {
  it("takes the entry the proxy in front of the dashboard wrote, the last one", () => {
    expect(clientAddress("203.0.113.7")).toBe("203.0.113.7");
    // A browser can send its own header; the proxy appends the address it saw.
    expect(clientAddress("198.51.100.1, 203.0.113.7")).toBe("203.0.113.7");
    expect(clientAddress("2001:db8::1")).toBe("2001:db8::1");
  });

  it("drops the port App Service adds", () => {
    expect(clientAddress("203.0.113.7:51234")).toBe("203.0.113.7");
    expect(clientAddress("[2001:db8::1]:51234")).toBe("2001:db8::1");
    expect(clientAddress("[2001:db8::1]")).toBe("2001:db8::1");
  });

  it("counts back past each trusted proxy", () => {
    // Front Door appended the person's address, then App Service appended Front Door's.
    expect(clientAddress("198.51.100.1, 203.0.113.7, 192.0.2.10:443", 2)).toBe("203.0.113.7");
    expect(clientAddress("203.0.113.7", 2)).toBeUndefined();
    expect(clientAddress("203.0.113.7", 0)).toBeUndefined();
    expect(clientAddress("203.0.113.7", Number("not a number"))).toBeUndefined();
  });

  it("has none when the header is missing, empty, or not an address", () => {
    expect(clientAddress(null)).toBeUndefined();
    expect(clientAddress("")).toBeUndefined();
    expect(clientAddress("203.0.113.7, ")).toBeUndefined();
    expect(clientAddress("unknown")).toBeUndefined();
    expect(clientAddress("203.0.113.7, evil.example")).toBeUndefined();
    expect(clientAddress("999.0.0.1")).toBeUndefined();
  });
});
