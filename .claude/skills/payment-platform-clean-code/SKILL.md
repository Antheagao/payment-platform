---
name: payment-platform-clean-code
description: Payment Platform clean code rules for C# and .NET - names across C#, SQL, and JSON, nullable types, records, async, options, time, analyzers, and the lint gate. Use when writing or refactoring backend code or fixing an analyzer or dotnet format warning here.
---

# Payment Platform clean-code overlay

Load the global `clean-code` guide for the rules; its TypeScript section applies to `frontend/` unchanged.
This file translates the Rust rules to C# and lists what the build enforces.

## Names

- One product word per concept in every layer: `AmountMinor` in C# and SQL is `amount_minor` in JSON; `Captured` is `captured`.
- Units in names when the type does not carry them: `AmountMinor`, `TimeoutSeconds` in configuration; a `TimeSpan` or `DateTimeOffset` already carries its unit.
- Standard .NET casing: PascalCase types and members, `_camelCase` private fields, `I` prefix on interfaces, acronyms as words (`HttpClient`, `ApiError`).
- Async methods end in `Async`.

## Types

- Classes are `sealed` unless designed for inheritance; immutable data is a `record`; small values are `readonly record struct`.
- Closed sets are enums; an id that crosses more than one function is a typed id (`PaymentId`), parsed once at the edge.
- Nullable reference types are on everywhere; the null-forgiving `!` needs a comment saying why it cannot be null.
- `required` and `init` for objects that must be complete at construction; constructors validate and throw `ArgumentException` for programmer errors.

## Errors and control flow

- Expected outcomes (not found, invalid transition, conflict) are typed results the caller must handle; exceptions are for the unexpected.
- Never catch `Exception` except at a host boundary that logs it once (the exception handler, a worker job loop).
- Return early instead of nesting; prefer `switch` expressions over `if` chains for closed sets, so the compiler flags a missing case.

## Async, time, and configuration

- Async all the way: every async method takes a `CancellationToken` last and passes it on; never `.Result`, `.Wait()`, or `async void`.
- Time comes from an injected `TimeProvider`; `DateTime.UtcNow` and friends are banned by `backend/BannedSymbols.txt`.
- Configuration binds to options classes validated at startup (`ValidateDataAnnotations().ValidateOnStart()`), so a malformed value fails the process, not the first request.
- Dependencies arrive through constructors (primary constructors are fine); no service locator, no static mutable state.

## Lint gates (enforced)

- `Directory.Build.props` sets warnings as errors, `AnalysisLevel` `latest-recommended`, and code style in build; `.editorconfig` holds the style rules.
- `dotnet format --verify-no-changes` decides formatting; do not hand-format against it.
- A justified suppression is a `[SuppressMessage(..., Justification = "...")]` on the smallest member, or an `.editorconfig` entry with a comment for a whole folder; never a bare `#pragma warning disable`.
- Test projects allow underscores in names (CA1707 is off under `backend/tests/`), because test names are sentences.
