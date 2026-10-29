import { headers } from "next/headers";
import Link from "next/link";
import { classNames } from "@/lib/class-names";
import { readToken } from "@/lib/session";

const links = [{ href: "/payments", label: "Payments" }] as const;

/**
 * The signed-in sections, beside the brand, from sm up; at phone width the home page links to them instead.
 * The current section carries `aria-current`, so it is announced as well as shown.
 */
export async function MainNav() {
  const pathname = (await headers()).get("x-pathname") ?? "";
  if (!(await readToken()) || pathname === "/login") return null;

  return (
    <nav aria-label="Main" className="hidden sm:block">
      <ul className="flex items-center gap-1">
        {links.map(({ href, label }) => {
          const current = pathname === href || pathname.startsWith(`${href}/`);
          return (
            <li key={href}>
              <Link
                href={href}
                aria-current={current ? "page" : undefined}
                className={classNames(
                  "rounded-md px-3 py-2 text-sm font-medium hover:bg-surface-2",
                  current ? "bg-surface-2 text-fg" : "text-muted",
                )}
              >
                {label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
