import type { HTMLAttributes } from "react";
import { classNames } from "@/lib/class-names";

/** The one page column: centered, at most 72rem wide, with gutters that grow with the viewport. */
export function Container({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      {...props}
      className={classNames("mx-auto w-full max-w-6xl px-4 sm:px-6 lg:px-8", className)}
    />
  );
}
