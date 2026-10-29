"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { classNames } from "@/lib/class-names";

export const refreshIntervalMs = 5_000;

/**
 * Re-renders the page's server components every five seconds while the tab is visible, so the
 * statuses follow an outage and its recovery without a reload. The numbers it changes are content
 * that updates on its own, so it carries a control to pause it (WCAG 2.2.2).
 */
export function LiveUpdates() {
  const router = useRouter();
  const [paused, setPaused] = useState(false);

  useEffect(() => {
    if (paused) return;
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") router.refresh();
    }, refreshIntervalMs);
    return () => clearInterval(timer);
  }, [paused, router]);

  return (
    <div className="flex items-center gap-3">
      <span className="flex items-center gap-2 text-sm text-muted">
        <span aria-hidden="true" className="relative flex size-2">
          {!paused && (
            <span className="absolute inline-flex size-full rounded-full bg-success opacity-60 motion-safe:animate-ping" />
          )}
          <span
            className={classNames(
              "relative inline-flex size-2 rounded-full",
              paused ? "bg-border-strong" : "bg-success",
            )}
          />
        </span>
        {paused ? "Updates paused" : "Live, every 5 seconds"}
      </span>
      <Button variant="secondary" size="sm" onClick={() => setPaused((value) => !value)}>
        {paused ? "Resume" : "Pause"}
      </Button>
    </div>
  );
}
