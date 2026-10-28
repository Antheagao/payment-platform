---
name: payment-platform-design-principles
description: Payment Platform design rules translated to C# - enums and switch expressions, interfaces only at external seams, template-method jobs, parse-don't-validate types, DI lifetimes, results versus exceptions, and what to avoid in .NET. Use when adding a project, type, port, adapter, job, or ADR in this repository.
---

# Payment Platform design-principles overlay

Load the global `design-principles` guide for the rules; its TypeScript and React section applies to `frontend/` unchanged.
This file translates the Rust mechanisms to C# and names the examples that already exist here.

## Choosing the mechanism

| Situation | C# mechanism | Example here |
| --- | --- | --- |
| A fixed set of variants we own | `enum` and a `switch` expression with no catch-all where it can be avoided | `PaymentStatus`, `PaymentLifecycle.CanTransition` |
| An external system with a real and a simulated adapter | An interface (port) and DI | `IAcquirer`, `SimulatedAcquirer` |
| A loop that wraps a varying step | An abstract base with one abstract hook (template method) | `PeriodicJob.RunOnceAsync` |
| Behavior that varies in one call | A `Func<>` parameter | (none yet) |

- An interface with one implementation and no fake is indirection; interfaces live only at external seams.
- No interface over `PaymentsDbContext` and no generic repository (ADR 002).

## Patterns that exist

- **Parse, don't validate:** `Money.Create`, `Currency.Parse`, and `CorrelationIds.AcceptOrCreate` turn raw input into a type or a replacement once, at the edge.
- **Typed ids:** `PaymentId` and `MerchantId` are `readonly record struct`s, so a merchant id can never be passed where a payment id belongs.
- **Invariants inside the aggregate:** `Payment` has no status setter; every transition goes through a method that checks `PaymentLifecycle` and records a `PaymentStatusChanged`.
- **Results for expected failures:** `Result` and `Result<T>` with a `DomainError` code; exceptions only for the unexpected.
- **Deterministic adapters:** the simulated acquirer derives everything from its input, so every process gives the same answer.

## Dependency injection

- Singletons for stateless adapters and telemetry (`SimulatedAcquirer`, `WorkerTelemetry`); scoped for `PaymentsDbContext` and use case handlers; never a scoped service captured by a singleton (scope validation is on in Development).
- A job gets a fresh DI scope per run from `PeriodicJob` and resolves its `DbContext` and use cases from it.
- Options classes are validated at startup; no `IConfiguration` lookups outside registration code.

## Avoid in .NET

- MediatR, AutoMapper, and generic repositories: indirection and, for the first two, commercial licenses since 2025; map by hand in the endpoint or use case.
- Types named `*Manager`, `*Helper`, `*Utils`, or `*Service` without a domain noun.
- Static mutable state, service location, and `async void`.
- Extension methods that hide I/O behind a property-like name.

## Decision records

- Format and numbering follow the global guide; existing ADRs are listed in `payment-platform-architecture`.
- Local practice: a naming correction found while implementing an ADR on the day it was written is recorded as a dated entry under an `## Amendments` heading in each affected ADR (as ADR 005 shows); anything that changes a decision gets a new ADR that supersedes the old one.
