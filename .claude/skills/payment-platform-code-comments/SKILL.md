---
name: payment-platform-code-comments
description: Payment Platform comment rules for C# XML doc comments, EF Core migrations, T-SQL files, and TODO markers. Use when documenting a public type, explaining a non-obvious decision, or adding a migration or stored procedure here.
---

# Payment Platform code-comments overlay

Load the global `code-comments` guide for when to comment and when not to; its TypeScript section applies to `frontend/` unchanged.
This file translates the Rust and SQL sections.

## C# doc comments

- `/// <summary>` on public types and members whose purpose or contract is not obvious from the name; one sentence when one is enough.
  Not on every public member by rule: many types are public only so tests and other projects in the solution can reach them.
- `<remarks>` for a contract callers must respect ("runs inside the caller's transaction", "never logs the token").
- `<exception cref="...">` only when callers are expected to catch it; `<returns>` only when it says more than the signature.
- Link related code with `<see cref="..."/>`, and point at a decision instead of restating it: `See ADR 007.`
- A file-level rule (for example, "this type is persisted; renaming a member is a migration") goes in the type's summary.

## Inline comments

- Above the line they explain, never at the end of it.
- Explain why, a business rule, or a library quirk; never narrate what the code already says.

## Migrations and T-SQL

- EF Core generates migration files; edit only the class's `/// <summary>` (what changes and why, with the task id) before the migration is pushed, and nothing after.
- Every `.sql` file under `Persistence/Sql/` starts with a `--` header: what it does, why it is T-SQL rather than LINQ, the task or ADR id.
- Comment each non-obvious column, index, or locking hint (`READPAST`, `UPDLOCK`) next to it.

## TODO markers

- `// TODO(T23): what is missing.` with the task id from `notes/future.md`; never a bare `TODO`, `FIXME`, or `HACK`.
- Prefer a row in `notes/future.md` over a marker in code.
