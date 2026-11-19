# Hearth

A full-stack community chat application built as an engineering portfolio project. Hearth combines a React interface with a feature-organized ASP.NET Core Minimal API, PostgreSQL, SignalR, and Redis.

<img width="1710" height="1060" alt="image" src="https://github.com/user-attachments/assets/af4fce55-fd52-4a16-9bfa-573a2daa36c9" />

Run it locally to explore the application. Local development uses a bundled email catcher and requires no API keys, external email account, or public domain. Optional production deployment files are included.

## Features

- Verified email/password accounts, password recovery, optional authenticator 2FA and recovery codes, HTTPS-only session cookies in production, CSRF protection, and all-device logout.
- Servers, ordered text channels, private-channel allowlists, custom additive roles, ownership transfer, kicks, bans, and moderation history.
- Expiring, revocable, usage-limited invitations; friends and requests; blocking; one-to-one DMs with shared-server privacy controls.
- Persistent messages, replies, edits, deletion tombstones, reactions, attachments, cursor pagination, unread counts, typing, and online presence.
- Transactional outbox notifications and idempotent message retries, with reconnect reconciliation.
- Responsive dark interface, keyboard-accessible dialogs, Docker deployment, CI, telemetry, and tests.

## Technology

| Area              | Stack                                                                     |
| ----------------- | ------------------------------------------------------------------------- |
| Backend           | .NET 10, ASP.NET Core Minimal APIs, EF Core, ASP.NET Core Identity        |
| Frontend          | React 19, TypeScript, Vite, Tailwind CSS, React Query                     |
| Persistence       | PostgreSQL 17; private attachments on the filesystem                      |
| Real-time updates | SignalR; Redis 7.4 for presence and typing                                |
| Email             | Mailpit locally; SMTP for optional production deployment                  |
| Operations        | Docker Compose, Caddy for production HTTPS, OpenTelemetry, GitHub Actions |
| Tests             | xUnit, Testcontainers, Vitest, Testing Library, Playwright                |

## Prerequisites

- .NET **10 LTS SDK** (10.0.402 or a newer 10.0 SDK, as selected by `global.json`).
- Node.js **22.12+** and npm. CI and container builds target Node 22. On Linux x64 with glibc, use **22.20+** within the 22.x line to satisfy all locked dependencies.
- Docker with **Linux containers** and the **Docker Compose plugin**. On Windows or macOS, use Docker Desktop with its engine running.

The [Docker-only setup](#entire-app-in-docker) builds .NET and Node inside the image, so only Docker and Compose are required on the host. The optional demo-seeding script still requires Node on the host.

The backend uses .NET 10 LTS, supported until **November 14, 2028**. Keep runtime and dependency patches current ([Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)).

## Run locally

From the repository root, start the dependencies, apply migrations, and run the API. The included development settings work without a `.env` file:

```powershell
docker compose up -d --wait postgres redis mailpit
dotnet restore Hearth.slnx
dotnet run --project src/Hearth.Api -- --migrate
dotnet run --project src/Hearth.Api
```

In another terminal:

```powershell
cd web
npm ci
npm run dev
```

Open **http://localhost:5173**. Register an account, open **http://localhost:8025** for its verification email, follow the link, and sign in. Use `localhost` consistently: authentication and real-time origin checks deliberately distinguish it from `127.0.0.1`.

Mailpit captures development emails locally; it does not deliver them to real inboxes. Registration passwords must contain at least 12 characters.

The Vite proxy forwards `/api` and `/hubs` to port 5080 so the browser uses a single origin. No cross-origin cookie setup is required. Redis outages degrade presence and typing while persistent HTTP messaging remains available.

The local Compose database uses port **15432** on your computer, leaving port 5432 available for an existing PostgreSQL installation. Containers still communicate on PostgreSQL's standard internal port.

The development API is available at **http://localhost:5080**, its OpenAPI document at **http://localhost:5080/openapi/v1.json**, and its readiness endpoint at **http://localhost:5080/health/ready**.

### Stop and start it later

When `npm run dev` is running in a terminal, press **Ctrl+C in that same terminal** to stop the web preview.

To stop the local database, Redis, and email catcher, run this from the repository root:

```powershell
docker compose stop
```

To start those services again:

```powershell
docker compose up -d --wait postgres redis mailpit
```

You can start the backend separately with `dotnet run --project src/Hearth.Api` and stop it with **Ctrl+C in its terminal**. The API must be running for sign-in, messaging, and the database-backed features; Vite alone only serves the frontend.

To stop every Compose service and remove the containers and network while **keeping your local database and uploaded files**:

```powershell
docker compose --profile full down
```

Docker's persistent volumes remain. To remove them as well and permanently erase the Docker-managed database, uploads, and application encryption keys:

```powershell
docker compose --profile full down --volumes
```

When running the API directly with `dotnet run`, its uploads and keys are stored separately under `src/Hearth.Api/data/`; removing Docker volumes does not remove that directory.

### Entire app in Docker

From the repository root, start all development dependencies, including Mailpit for account verification:

```powershell
docker compose up -d --wait postgres redis mailpit
docker compose --profile full build app
docker compose --profile full run --rm app --migrate
docker compose --profile full up -d app
```

Open **http://localhost:5080** for the app and **http://localhost:8025** for verification/reset emails. The .NET container serves the built frontend, so no separate Vite process is needed. Database migrations run explicitly with `--migrate`; ordinary application startup does not apply them.

### Seed a local demonstration

With the API and Mailpit running:

```powershell
node scripts/demo.mjs
```

The script creates two verified local test accounts, a server, five channels, and a short conversation through the public API. It prints the generated sign-in email addresses and the demo password. It refuses non-local app destinations and does not install an authentication bypass. Repeated runs create independent demo communities. By default, it calls the API at port 5080 and links to the frontend at port 5173.

For the complete local app container, set the frontend URL before seeding (PowerShell):

```powershell
$env:HEARTH_WEB_URL = 'http://localhost:5080'
node scripts/demo.mjs
```

## Development commands

```powershell
dotnet build Hearth.slnx

# Unit, model, and configuration checks; no Docker required.
dotnet test Hearth.slnx --filter "Category!=Integration"

# Full suite; requires the Docker engine for Testcontainers.
dotnet test Hearth.slnx
```

To create a new EF Core migration after a model change:

```powershell
dotnet tool restore
dotnet ef migrations add DescribeChange --project src/Hearth.Infrastructure --startup-project src/Hearth.Api
```

The full backend suite uses disposable PostgreSQL and Redis containers. It tests privacy boundaries, invite races, duplicate sends, blocking, CSRF, logout, and schema consistency.

From `web`:

```powershell
npm run typecheck
npm run build
npm run test
```

With the development API running, regenerate the API types and check them:

```powershell
npm run generate:api
npm run typecheck
```

`generate:api` reads the running development API's `/openapi/v1.json`; the frontend imports DTO types from its generated output. CI regenerates them and typechecks the UI to detect integration errors. `docs/openapi.bootstrap.json` is a historical DTO-only bootstrap and is not the API specification.

End-to-end tests require the API, PostgreSQL, Redis, and Mailpit to be running. Playwright starts Vite if needed locally and uses independent browser sessions for multi-user tests. Set `E2E_EXTERNAL=1`, `E2E_BASE_URL` to the frontend URL, and `MAILPIT_URL` to the test inbox URL when targeting a separate **test** deployment that accepts generated test accounts.

From `web`, install Playwright's Chromium browser once, then run the browser tests:

```powershell
npx playwright install chromium
npm run test:e2e
```

You can use your installed Chrome instead of downloading a Playwright browser:

```powershell
$env:E2E_BROWSER_CHANNEL = 'chrome'
npm run test:e2e
```

The [CI workflow](.github/workflows/ci.yml) builds and tests both applications, regenerates API types, audits dependencies, and builds/scans all four production images. See the dated [verification record](docs/verification.md) for previously completed checks and their limits.

## Repository

```text
src/
  Hearth.Domain/          Entities and permission/invitation rules
  Hearth.Infrastructure/  EF Core, Identity, migrations, Redis, storage, SMTP
  Hearth.Api/             Minimal API feature modules, access checks, SignalR, workers
web/                     React application, generated API DTO types, UI and browser tests
tests/Hearth.Tests/       xUnit and Testcontainers tests
deploy/                  Caddy HTTPS configuration and supporting-image builds
docs/                    Architecture, operations, verification status
scripts/                 Local demo seeding and offline domain-rule checks
.github/                 CI workflow and dependency-update configuration
```

Read the [architecture](docs/architecture.md), [security decisions](docs/security.md), and [deployment guide](docs/deployment.md) for the design rationale and operational boundaries.

## Configuration

Local development defaults live in [`appsettings.json`](src/Hearth.Api/appsettings.json), with development overrides in [`appsettings.Development.json`](src/Hearth.Api/appsettings.Development.json). The database password in these settings and `compose.yaml` is a public local-development default. Demo/test passwords are fixtures and must not be reused for real accounts.

ASP.NET Core settings can be overridden through environment variables such as `ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `App__PublicUrl`, `App__DataPath`, and `Mail__Password`. The double underscore separates nested settings. The optional production Compose setup reads values from `.env` and passes them into the app; `dotnet run` does not automatically load that file.

[`.env.example`](.env.example) contains placeholders for production database, Redis, and SMTP credentials, the public hostname, and approved registration addresses. Keep actual values in an ignored `.env` file or an external secret store. A `.env` file is plaintext and must remain private.

Generated uploads and ASP.NET Core Data Protection keys live under `data/` for a directly run API or in the `app-data` Docker volume. User account data and chat history live in PostgreSQL. Caddy's certificate keys live in its separate Docker volume. The [`.gitignore`](.gitignore) excludes `.env` files (except `.env.example`), `data/`, `.tools/`, backups, and build/test output. Keep these runtime files and database exports out of public uploads.

## Optional deployment

The supplied `compose.production.yaml` targets one app replica and defaults to up to ten approved users, with bounded uploads, email throttling, HTTPS, and persistent storage. Caddy accepts public HTTPS traffic and proxies it to the app; it is not used by either local setup above.

Deployment requires your hostname, distinct database/Redis passwords, SMTP credentials, and approved email addresses. Production startup validates the configuration and rejects development credentials. Set up backups separately as described in the [deployment guide](docs/deployment.md). See also the [hosting walkthrough](docs/free-hosting.md) and [security decisions](docs/security.md).
