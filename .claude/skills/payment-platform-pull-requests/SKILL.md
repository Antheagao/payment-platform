---
name: payment-platform-pull-requests
description: Payment Platform commit subjects, areas, branch policy, and what every commit carries. Use when writing a commit message, pushing, or reviewing a diff in this repository.
---

# Payment Platform pull-requests overlay

Load the global `pull-requests` guide for the rules; this file holds what is specific here.

## Commit subjects

- `<area>: <what landed>`, lowercase after the colon, no trailing period, at most 72 characters.
- Areas, matching the top-level layout: `repo`, `notes`, `agents` (`AGENTS.md` and `.claude/`), `docs`, `backend`, `frontend`, `infra`, `ci`.
  A commit that spans areas takes the area of its main change; the body lists the rest.
- The body is `-` bullets per area of what landed, then why for anything surprising.
- Name the task id (`T<n>`) in the body, not the subject, so the history reads well to someone who never opens the notes.

## Branches and history

- Scaffold and feature work commits straight to `main` and pushes after each task: the owner asked for the public history to show the progress task by task, and nobody else works this repository.
- History stays linear: no merge commits; never rewrite a pushed commit.
- If a reviewer or collaborator joins, switch to one branch and one pull request per task, rebase-merged so each task's commits survive.

## Every commit carries

- The notes rotation (`notes/present.md` at least), enforced by `.githooks/pre-commit`.
- The tests for the behavior it adds.
- Only exact staged paths; never `git add -A` or `git add .`.
