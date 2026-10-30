import { headers } from "next/headers";
import { MainNavLinks } from "@/components/main-nav-links";
import { readToken } from "@/lib/session";

/**
 * The signed-in sections, beside the brand, from sm up; at phone width the home page links to them instead.
 * The current section carries `aria-current`, so it is announced as well as shown (`MainNavLinks`).
 */
export async function MainNav() {
  const pathname = (await headers()).get("x-pathname") ?? "";
  if (!(await readToken()) || pathname === "/login") return null;

  return (
    <nav aria-label="Main" className="hidden sm:block">
      <MainNavLinks />
    </nav>
  );
}
