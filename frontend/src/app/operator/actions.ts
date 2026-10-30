"use server";

import { refresh } from "next/cache";
import { replayDelivery } from "@/lib/staff-api";

export type ReplayState = { tone?: "success" | "error"; message?: string };

const refusals: Record<string, string> = {
  delivery_not_dead_lettered: "That delivery is no longer dead-lettered: it was replayed already.",
  webhook_endpoint_disabled:
    "The merchant's webhook endpoint is disabled; a replay would fail again. Ask them to enable it.",
  not_found: "That delivery no longer exists.",
};

/** Replays one dead-lettered delivery from the operator page; the API checks the staff token and the delivery. */
export async function replayAction(_previous: ReplayState, form: FormData): Promise<ReplayState> {
  const id = Number(form.get("id"));
  if (!Number.isSafeInteger(id) || id < 1) {
    return {
      tone: "error",
      message: "That request was incomplete. Reload the page and try again.",
    };
  }

  const result = await replayDelivery(id, "/operator");
  switch (result.kind) {
    case "replayed":
      refresh();
      return {
        tone: "success",
        message: `Delivery ${id} is back on the queue; the worker sends it within a few seconds.`,
      };
    case "refused":
      refresh();
      return {
        tone: "error",
        message: refusals[result.code] ?? `The API refused the replay (${result.code}).`,
      };
    case "unavailable":
      return {
        tone: "error",
        message: "The API did not answer. Nothing changed; try the replay again.",
      };
  }
}
