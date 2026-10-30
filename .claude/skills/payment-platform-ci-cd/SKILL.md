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
| `ci.yml` stack | the README's one command on a clean runner with no `.env`: `docker compose config -q`, `docker compose up --build --detach --wait`, `sh scripts/check-stack.sh` (both readiness probes, OpenAPI, the dashboard showing "API ready", the Aspire dashboard, and no warning logged by the API or the worker while starting); its image build has `backend/` as its context, so the root `.editorconfig` is not there: an analyzer setting the container build needs (such as `generated_code` for migrations) lives in a nested `.editorconfig` beside the code; logs uploaded on failure; `down --volumes` always | the same commands from the root |
| `ci.yml` infra | `az bicep build`, `az bicep lint`, `az bicep build-params` | the Bicep CLI, see `infra/README.md` |
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
- Compose's dashboard has no port of its own: the `edge` service (`caddy:2.11.7-alpine`, `infra/edge/Caddyfile`) publishes `DASHBOARD_PORT` and writes each browser's address into `X-Forwarded-For` (ADR 010); the API and the dashboard share `DASHBOARD_KEY`, a public local-only default.
- Images are built, never pushed: there is no registry, because nothing is deployed.

## Infrastructure as code

- `infra/bicep/` (see `infra/README.md`) is validated on every push and never deployed (the owner's rule for portfolio work: validate and plan, never apply).
- `bicepconfig.json` raises the rules that matter to errors (unused parameters and variables, hard-coded cloud URLs, secrets in outputs, insecure parameter defaults, stable identifiers).
- A Bicep change ships with `infra/README.md` updated when it changes what a deployment would need.

## Azure DevOps

- The team uses Azure DevOps; the GitHub Actions jobs map one to one onto Azure Pipelines stages (`DotNetCoreCLI` or script steps, `NodeTool`, `AzureCLI@2` for Bicep).
- A mirror pipeline is parked in `notes/future.md` until there is an Azure DevOps organization to run it in, because an unrun pipeline would be unverified.
