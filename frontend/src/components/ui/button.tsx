import type { ButtonHTMLAttributes } from "react";
import { classNames } from "@/lib/class-names";

type Variant = "primary" | "secondary";
type Size = "md" | "sm";

const variantClasses: Record<Variant, string> = {
  primary: "bg-primary text-primary-fg hover:bg-primary-hover",
  secondary: "border border-border-strong bg-surface text-fg hover:bg-surface-2",
};

// Both sizes clear the 24px minimum target (WCAG 2.5.8); `sm` is for controls inside a section header.
const sizeClasses: Record<Size, string> = {
  md: "min-h-10 px-4",
  sm: "min-h-8 px-3",
};

type ButtonStyle = { variant?: Variant; size?: Size; className?: string };

/** The class list for a button, so a link that acts as a button looks exactly like one. */
export function buttonClasses({
  variant = "primary",
  size = "md",
  className,
}: ButtonStyle = {}): string {
  return classNames(
    "inline-flex items-center justify-center gap-2 rounded-md text-sm font-medium whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-60",
    variantClasses[variant],
    sizeClasses[size],
    className,
  );
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & Omit<ButtonStyle, "className">;

/** A button; defaults to `type="button"` so it never submits a form by accident, and the caller can override it. */
export function Button({ variant, size, className, ...props }: ButtonProps) {
  return (
    <button type="button" {...props} className={buttonClasses({ variant, size, className })} />
  );
}
