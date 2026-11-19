# Security review — October 16, 2026

Scope: repository source, direct/transitive dependencies, frontend rendering, identity and authorization boundaries, uploads, SignalR, configuration, production containers, and local release/restore behavior. Intended deployment: **one app replica and at most ten approved users**. No production credentials or public deployment were used.

## Findings and changes

| Finding | Change |
| --- | --- |
| Known advisories in React Router, Vite, Vitest/mocker/tinypool/esbuild and NuGet cryptography/SSH dependencies | Updated packages and lockfile; pinned patched transitive NuGet versions. Both complete package audits now report zero known vulnerabilities. Some upstream advisories concerned features this SPA does not use, but the affected packages were still replaced. |
| .NET 9 approaching end of support; old runtime/container pins | Migrated backend, tooling, CI and images to .NET 10 LTS, currently SDK 10.0.402/runtime 10.0.13. Preserved the Identity schema version and verified the existing migration snapshot. |
| A slow unauthenticated request could hold the process-wide authorization lock before rate limits/CSRF | Moved locking to endpoint handlers, after authentication, limits, CSRF, validation and body parsing. Response/file streaming no longer holds the lock. Added a two-second lock wait, request timeout, API concurrency and connection bounds. |
| Signup and attachment storage were unbounded for an Internet-facing deployment | Required production email allowlist, capped accounts at ten in Compose, bounded per-account/global uploads, retained pending-file limits, and included orphaned files in the disk quota. |
| Verification/reset routes could exhaust free SMTP quotas or flood a known mailbox | Added a production email IP limit and five-minute account/purpose cooldown, with bounded SMTP execution and retry after failed delivery. |
| Hub methods could repeatedly query the database and connections were unbounded | Added per-connection token buckets and per-user/global connection caps; throttled legitimate client typing events. |
| Production origin/proxy/credentials validation was incomplete | Validated exact HTTPS origin, explicit allowed hosts and trusted proxy, authenticated TLS mail, and distinct strong PostgreSQL/Redis passwords. Disabled forwarded headers when no proxy is configured. Rejected plain HTTP production traffic and used host-prefixed cookies. |
| Oversized login/token/allowlist requests could consume unnecessary resources | Added DTO size limits and separate 64 KiB JSON/multipart body limits; required the CSRF header before attempting form parsing. |
| Supporting images contained vulnerable OpenSSL/zlib and embedded Go runtimes/modules | Added a dependency Dockerfile applying Alpine updates and rebuilding gosu/Caddy with patched Go/modules. App/PostgreSQL/Redis scan clean; Caddy's sole remaining module warning is inapplicable, with source/graph evidence in verification.md. |
| Production Caddy could collide with an automatically assigned container IP | Reserved a separate dynamic allocation range so the trusted static proxy IP stays available. Verified startup and forged-proxy rejection. |
| Production container/backup behavior had not been validated | Verified non-root/read-only app operation, bounded temporary storage/logs, fresh migration, HTTPS headers/cookies/origin checks, and restoring database/files/keys into an isolated stack. |

CI now audits NuGet/npm, builds and scans all production images, and has weekly dependency update configuration. The generated .NET 10 API types and frontend compile together; numeric JSON inputs are strict.

## Result and limits

The identified application/configuration issues are fixed and the project has passed the checks in [verification.md](verification.md), including the intended ten-user concurrency case. It is suitable to prepare a small private deployment using [free-hosting.md](free-hosting.md), provided the actual host's DNS, SMTP, secrets, firewall and backups are configured and verified.

The review does not certify that the project has no vulnerabilities. Keep one replica, restrict registration to your group, monitor disk/backup health, and apply security updates. Account deletion/export, legal retention requirements, durable email retries, malware scanning and sustained-load testing remain outside the implemented feature set; assess those if your usage expands. SMTP delivery and public certificate issuance must be tested with your real settings.
