# Verification status

Security and deployment checks completed on **October 16, 2026**, with .NET SDK **10.0.402**, Node.js **22.17.1**, Docker Desktop's Linux engine, and Chrome. No public cloud deployment was performed. See [security review](security-review.md) and [free deployment options](free-hosting.md).

| Check | Actual result |
| --- | --- |
| .NET 10 Release build | Passed, zero warnings/errors |
| NuGet audit, including transitive/test dependencies | No known vulnerabilities reported |
| npm audit, including development dependencies | Zero vulnerabilities at every severity |
| Backend tests | **45 passed**: 27 unit/model/configuration cases and 18 PostgreSQL/Redis integration scenarios |
| EF migration snapshot consistency | Passed on .NET 10; no schema change required |
| Local migration and fresh production-container migration | Passed |
| Types generated from live .NET 10 OpenAPI | Passed; production TypeScript build passed |
| Frontend tests | **5 passed** |
| Chrome browser scenarios | **4 passed** |
| Ten concurrent users sending to one conversation | Passed; ten distinct authors/messages persisted |
| Production Docker images | All four built successfully, including patched supporting images |
| App, PostgreSQL, Redis image scans at every severity | Zero findings |
| Caddy image scan | No high/critical/medium/low findings; one module-level OpenPGP warning assessed as inapplicable, below |
| Caddy source-aware `govulncheck` | Zero affected vulnerabilities/packages; one unused module advisory |
| Production HTTPS smoke checks | Passed with an isolated local test CA |
| Proxy/host and cookie controls | Forged forwarding headers rejected; host-prefixed Secure/HttpOnly/Strict cookie, HSTS, CSP, no-store, and nosniff verified |
| Production authorization and hub origin | Anonymous private API rejected; hostile hub origin rejected |
| Container restrictions | UID 1654, read-only root filesystem, memory/PID limits verified |
| Matched database/uploads/key backup and restore | Passed into a separate Compose project, network, and fresh volumes |
| Restore integrity | Database marker and attachment SHA-256 matched; pre-backup CSRF token accepted using restored keys |

The browser tests cover registration/email verification, live channel/DM messaging, reconnects, private-file revocation on an existing connection, edits/deletion, multi-tab reads, logout, and mobile navigation. Backend tests additionally exercise CSRF, account lockout, roles/moderation, bans, invitation races, idempotency, privacy/blocks, account recovery, authenticator/recovery-code flows, approved signup/account caps, email cooldowns, upload quotas, input limits, bounded lock waits, and production configuration validation.

## Scanner warning assessment

The rebuilt Caddy contains `golang.org/x/crypto` for maintained cryptographic packages. Trivy reports **GO-2026-5932**, an advisory about the separate obsolete `openpgp` packages. The actual build's 1,029-package dependency graph contains **no** `golang.org/x/crypto/openpgp` imports. Source-aware Go vulnerability analysis reports no affected code; the binary-only scan conservatively reports the module warning. The Dockerfile now fails if those OpenPGP imports appear and includes the compiled dependency manifest at `/usr/local/share/caddy-packages.txt`. The warning remains visible rather than being globally suppressed. See the [Go advisory](https://pkg.go.dev/vuln/GO-2026-5932) and [Go maintainers' explanation of module-scanner overreporting](https://github.com/golang/go/issues/80347).

## Checks required on your actual cloud host

Supply the real hostname, approved addresses, secrets, and SMTP account. Verify public DNS, certificate issuance/renewal, firewall rules, SMTP delivery/password recovery, and an off-host encrypted backup/restore. The local HTTPS drill used a test CA; it does not establish that a future public DNS name or SMTP account is configured correctly. ARM images are designed for native builds but this review ran on x64; verify the build and readiness on the chosen VM.

The concurrency check is a smoke test for the intended group size, not a sustained-load benchmark. Full failure injection and independent penetration testing were not performed. Signature validation is not malware scanning; the deployment is intended for a small approved group, not arbitrary public registrations/uploads. No review or dependency audit proves the absence of all vulnerabilities.

Run CI before releases, rebuild all images with current security updates, and follow [deployment.md](deployment.md) for migrations, backups, restore, monitoring, and updates. .NET 10 LTS is supported through November 14, 2028; keep applying runtime patches.
