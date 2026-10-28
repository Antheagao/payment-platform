import type { ButtonHTMLAttributes } from "react";
import { classNames } from "@/lib/class-names";

type Variant = "primary" | "secondary";

const variantClasses: Record<Variant, string> = {
  primary: "bg-primary text-primary-fg hover:bg-primary-hover",
  secondary: "border border-border-strong bg-surface text-fg hover:bg-surface-2",
};

/** The class list for a button, so a link that acts as a button looks exactly like one. */
export function buttonClasses(variant: Variant = "primary", className?: string): string {
  return classNames(
    "inline-flex min-h-10 items-center justify-center gap-2 rounded-md px-4 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60",
    variantClasses[variant],
    className,
  );
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant };

/** A button; defaults to `type="button"` so it never submits a form by accident, and the caller can override it. */
export function Button({ variant = "primary", className, ...props }: ButtonProps) {
  return <button type="button" {...props} className={buttonClasses(variant, className)} />;
}
