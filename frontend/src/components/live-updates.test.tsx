import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { LiveUpdates, refreshIntervalMs } from "./live-updates";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

function setVisibility(state: DocumentVisibilityState) {
  Object.defineProperty(document, "visibilityState", { configurable: true, get: () => state });
}

describe("LiveUpdates", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    refresh.mockClear();
    setVisibility("visible");
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("refreshes the page every interval while visible", () => {
    render(<LiveUpdates />);

    act(() => vi.advanceTimersByTime(refreshIntervalMs * 2));

    expect(refresh).toHaveBeenCalledTimes(2);
    expect(screen.getByText("Live, every 5 seconds")).toBeVisible();
  });

  it("skips refreshes while the tab is hidden", () => {
    setVisibility("hidden");
    render(<LiveUpdates />);

    act(() => vi.advanceTimersByTime(refreshIntervalMs * 3));

    expect(refresh).not.toHaveBeenCalled();
  });

  it("stops when paused and starts again when resumed", () => {
    render(<LiveUpdates />);

    fireEvent.click(screen.getByRole("button", { name: "Pause" }));
    act(() => vi.advanceTimersByTime(refreshIntervalMs * 3));

    expect(refresh).not.toHaveBeenCalled();
    expect(screen.getByText("Updates paused")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Resume" }));
    act(() => vi.advanceTimersByTime(refreshIntervalMs));

    expect(refresh).toHaveBeenCalledTimes(1);
  });
});
