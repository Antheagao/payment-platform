import type { HTMLAttributes } from "react";
import { classNames } from "@/lib/class-names";

export type Tone = "neutral" | "info" | "primary" | "success" | "warning" | "danger";

const toneClasses: Record<Tone, string> = {
  // Neutral pills often sit on raised (surface-2) rows, so they carry a hairline ring instead of a fill.
  neutral: "bg-surface text-muted ring-1 ring-border ring-inset",
  info: "bg-info-soft text-info",
  primary: "bg-primary-soft text-primary",
  success: "bg-success-soft text-success",
  warning: "bg-warning-soft text-warning",
  danger: "bg-danger-soft text-danger",
};

type BadgeProps = HTMLAttributes<HTMLSpanElement> & { tone?: Tone };

/**
 * A small status pill: a dot plus a text label, so meaning never rides on color alone.
 * Every tone pair is held to 4.5:1 by `scripts/check-contrast.mjs`.
 */
export function Badge({ tone = "neutral", className, children, ...props }: BadgeProps) {
  return (
    <span
      {...props}
      className={classNames(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs leading-none font-medium whitespace-nowrap",
        toneClasses[tone],
        className,
      )}
    >
      <span aria-hidden="true" className="size-1.5 rounded-full bg-current" />
      {children}
    </span>
  );
}
