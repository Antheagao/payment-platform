---
name: payment-platform-ci-cd
description: Payment Platform pipelines, container images, and infrastructure as code - the CI jobs and how to reproduce them, CodeQL, Dependabot policy, Docker and compose, Bicep validation, and the never-deploy rule. Use when changing a workflow, a Dockerfile, compose, Bicep, or anything CI runs in this repository.
---

# Payment Platform ci-cd overlay

There is no global guide for pipelines; this overlay is the rule set.
CI runs exactly the gates in `AGENTS.md`, so a change that passes them locally passes CI unless the toolchain differs.

## Workflows (`.github/workflows/`)

| Job | Runs | Reproduce locally |
| --- | --- | --- |
| `ci.yml` backend | locked and audited `dotnet restore`, `dotnet format --verify-no-changes`, Release build (warnings are errors), `dotnet test --solution` with Testcontainers on the runner's Docker | the same commands from `backend/` with Docker running |
| `ci.yml` frontend | `npm ci`, `typecheck`, `lint`, `format:check`, `test`, `build`, `npm audit --omit=dev` | the same scripts from `frontend/` |
| `ci.yml` e2e | Playwright on the production build, desktop and mobile; report uploaded on failure | `npm run e2e` |
| `ci.yml` repository | `node scripts/check-docs.mjs`, `node scripts/check-contrast.mjs` | the same |
| `ci.yml` containers | `docker compose config -q` with `.env.example`, then `docker compose build api worker frontend` | the same |
| `codeql.yml` | C# and TypeScript, `security-extended`, on push, pull request, and Mondays | none; results in Security, Code scanning |

- Permissions default to `contents: read`; only CodeQL gets `security-events: write`.
- Pushes to the same ref cancel the older run.
- Action majors in use: `checkout@v7`, `setup-dotnet@v6` (reads `backend/global.json`), `setup-node@v7`, `upload-artifact@v7`, `codeql-action@v4`.

## Toolchain drift

- CI resolves the newest SDK in the `10.0` line that `global.json` allows (10.0.403 on 2026-10-28); this machine has 10.0.101, which missed an unused using that CI's analyzers caught.
  Treat CI's analyzer result as the authority, and update the local SDK when a newer analyzer finds something.
- `CI=true` turns on locked restore; a lock file change belongs in the same commit as the package change.

## Dependabot (`.github/dependabot.yml`)

- Weekly; minor and patch grouped per ecosystem; security updates arrive at once and ungrouped.
- Majors that must move with a runtime or framework are ignored and upgraded on purpose: Node 22 LTS (image and `@types/node`), TypeScript and ESLint (whatever Next.js supports), the .NET images (the target framework).
- A Dependabot pull request merges only with every CI job green; never merge a major bump that the ignore list exists to stop.

## Containers

- `backend/Dockerfile` (targets `api` and `worker`, chiseled non-root runtime) and `frontend/Dockerfile` (standalone server on distroless Node 22, non-root, `NEXT_OUTPUT=standalone`).
- Images are built, never pushed: there is no registry, because nothing is deployed.

## Infrastructure as code

- Bicep for the Azure shape arrives with T17, validated in CI and never deployed (the owner's rule for portfolio work: validate and plan, never apply).

## Azure DevOps

- The team uses Azure DevOps; the GitHub Actions jobs map one to one onto Azure Pipelines stages (`DotNetCoreCLI` or script steps, `NodeTool`, `AzureCLI@2` for Bicep).
- A mirror pipeline is parked in `notes/future.md` until there is an Azure DevOps organization to run it in, because an unrun pipeline would be unverified.
