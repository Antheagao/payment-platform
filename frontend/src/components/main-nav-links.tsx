"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { classNames } from "@/lib/class-names";

const links = [{ href: "/payments", label: "Payments" }] as const;

/**
 * The section links, marked by the path the browser is on now. The header sits in the root layout, which the
 * router keeps across navigations, so the server's idea of the path goes stale after the first click; the
 * browser's does not.
 */
export function MainNavLinks() {
  const pathname = usePathname();
  return (
    <ul className="flex items-center gap-1">
      {links.map(({ href, label }) => {
        const current = pathname === href || pathname.startsWith(`${href}/`);
        return (
          <li key={href}>
            <Link
              href={href}
              // "page" on the section itself; within it, such as on one payment, the link is the current section.
              aria-current={pathname === href ? "page" : current ? "true" : undefined}
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
  );
}
