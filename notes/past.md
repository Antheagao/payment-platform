# Past

A log of what has been built, in order.
Each entry names the commit(s) so the diff is one `git show` away.
Update this file when a task lands; keep the present and future notes in sync.

## 2026-10-28: repository base (T1)

- `repo: initialize repository` (`c8a4e32`): README, `.gitignore` for .NET, Next.js and Bicep output, LF line endings, root `.editorconfig`.
  Pushed to `github.com/Antheagao/payment-platform` as a public repository.
- `notes: add past, present, and future notes with rotation hooks` (`e0cb13d`): the three notes and the `.githooks/` guards that keep them honest; a test commit without `notes/present.md` was refused as intended.
- Stack decision recorded in the brief: SQL Server rather than PostgreSQL, to match the hiring team's Azure SQL.
- `agents: add agent rules, batch-loop skill, and pull-requests overlay`: `AGENTS.md` with orientation, ports, gates, and the guide table; `.claude/CLAUDE.md` imports it with `@../AGENTS.md`; the `batch-loop` skill and the `payment-platform-pull-requests` overlay.
- Lesson: in PowerShell, `git commit -F -` does not read a here-string; commit through the Bash tool with a quoted heredoc instead.
