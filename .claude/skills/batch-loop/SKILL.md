---
name: batch-loop
description: Work the Payment Platform roadmap in notes/future.md as committed batches, one task slice per commit with the notes rotated and the commit pushed. Use for "work the roadmap", "next task", "run the batch loop" (once, N, continuous).
---

# Batch Loop (Payment Platform)

The `campaign-loop` protocol from the claude-brain repository, mapped onto this repository's notes.
State is committed, so any session on any machine resumes from `git pull` alone.

## Where the state lives

| campaign-loop role | Payment Platform file |
| --- | --- |
| Queue (ranked rows, each with scope and gate) | `notes/future.md`: rows are taken from its sections in order: `Now`, then `Next`, then `Later` |
| Last updated / current state | `notes/present.md`: its "Last updated" line, task board, and how it runs |
| Archive of finished batches | `notes/past.md`: one dated entry per batch, naming the commit |

A row marked `[USER-GATED: ...]` or `[BLOCKED: ...]` is skipped; the text after the colon is its unblock condition.

## Start of a run

1. `git status --short --branch`.
   The tree must be clean apart from files this run will own; if someone else's work is uncommitted, stop and say what it is.
2. `git config core.hooksPath` must print `.githooks`; if not, set it, since the hooks are what keep the notes honest.
3. Set the loop-mode status marker as the global AGENTS.md requires (`standard` for `once` or `N`, `infinite` for `continuous`), and clear it whenever the loop stops.
4. Make sure Docker Desktop is running if any row in reach needs the integration tests or compose.

## One batch

1. **Load state.** Read `notes/present.md` and `notes/future.md` in full; reconstruct from git and the notes, never from an older transcript summary.
   Take the top workable row.
2. **Scope it.** If a row is too big for one batch, pick the smallest slice that ships on its own and is verifiable, and name it in one sentence before writing code.
   A design decision is a valid batch; its deliverable is a `docs/decisions/` update.
3. **Build it.** Follow `AGENTS.md`, the ADRs, and the overlay for each area touched.
   Read `frontend/node_modules/next/dist/docs/` before touching Next.js APIs.
   When the row names an overlay, write it in the same batch, stating only facts that are now true.
4. **Gate it, scoped to what changed:**
   - Run the row's gate and the `AGENTS.md` gates for the parts touched (they are what CI runs).
   - User-visible behavior touched: run the stack, exercise the flow in a real browser in light and dark at 360px and 1440px, then stop everything started.
   Write long build and test output to a scratch file and grep it rather than printing it into the transcript.
   A failing gate gets two evidence-driven attempts.
   On the second failure, revert only this batch's paths, mark the row `[BLOCKED: <reason>]`, and move to the next unaffected row.
5. **Rotate the notes.**
   - `past.md`: a dated entry for what landed, naming the commit and any lesson a later batch would want.
   - `present.md`: rewrite the affected sections, move the task on the board, bump "Last updated", and re-measure the numbers you carry forward (test counts, CI status) rather than copying them.
   - `future.md`: remove or shrink the row, re-rank if the batch changed priorities, bump "Last updated".
6. **Commit once, code and notes together.** Stage exact paths, never `git add -A`.
   Subject `<area>: <what landed>` per `payment-platform-pull-requests`, no co-author trailer.
   Never bypass the hooks with `NOTES_SKIP` or `--no-verify`.
   The commit hash is not known until after the commit, so `past.md` names the subject; a later batch may add the short hash.
7. **Push** after every batch; the owner asked for the history to show progress task by task.
   If CI exists, check the run for the pushed commit before starting a row that depends on it.
8. **Pre-seed the next batch:** make sure the new top row of `future.md` states its next slice clearly enough that a fresh session can start on it.

## Arguments

- none or `once`: one batch, then report.
- `N`: N batches back to back, each its own commit and push.
- `continuous`: keep taking rows until a stop condition fires.

## Stop conditions

- Only user-gated or blocked rows remain: run the close-out below.
- Two consecutive batches end blocked: something systemic; report instead of continuing.
- A batch would need something the owner has not granted: a paid service, an Azure subscription, a deployment, or a destructive action.
  Mark the row `[USER-GATED: ...]` with exactly what is needed, then continue with the next row.
- A batch that changed no files and produced no learning is a failure; stop rather than manufacture work.
- Commits not made by this run appear on the branch, or files change underneath it: a second session is working this checkout.
  Stop rather than race it.

A context boundary is not a stop condition: the state is already committed.

## Queue-empty close-out

1. Every remaining row carries its unblock condition.
2. `future.md` says plainly that no workable rows remain and why.
3. Commit that with the notes rotation as a real commit.
4. Report what is blocked and on whom.

## Report

At the end, report the batches run and the commits made (`git log --oneline <start>..HEAD`).
List rows newly blocked or user-gated, with their conditions, and anything left uncommitted.
Confirm that every server, container, and watcher the run started has been stopped.
