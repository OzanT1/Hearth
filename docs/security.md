# Security decisions

## Identity and sessions

ASP.NET Core Identity owns password hashing, email verification/reset tokens, authenticator verification, lockout, recovery codes, and security stamps. Registration passwords have a 12-character minimum and a 128-character request maximum; composition rules do not force predictable substitutions. Recovery and resend endpoints return generic responses. Authentication routes have IP-based request limits and failed logins increment lockout counters.

Production signup requires an explicit email allowlist and is capped at 10 accounts by the supplied Compose configuration. Email-producing endpoints allow five requests per IP per 15 minutes and one email per account/purpose per five minutes. Failed delivery releases the cooldown. The cooldown is process-local; the account cap and allowlist still apply after restart. Development permits 1000 local test accounts and relaxes the email IP limit.

The browser uses an HTTP-only, Secure-in-production, SameSite=Strict application cookie with an eight-hour non-sliding lifetime. Every authenticated HTTP request revalidates the security stamp. Password recovery, 2FA changes, and all-device logout invalidate sessions and terminate registered hub connections. Authenticator setup refreshes the initiating session after rotating its key. Recovery codes are shown once by the UI and should be kept outside the app.

The frontend fetches an antiforgery token and includes it on every state-changing API request, including unauthenticated account requests. SignalR allows the configured public origin, or same-origin browser fetch metadata for non-WebSocket requests without Origin. There is no wildcard CORS policy. Server-rendered response headers set CSP, nosniff, no-referrer, and authenticated no-store caching. Only the explicitly configured proxy can supply trusted forwarded headers.

## Authorization matrix

| Operation | Requirement |
| --- | --- |
| Read public channel | Current server membership |
| Read private channel | Membership and role/member allowlist, or server ownership |
| Manage a channel | ManageChannels permission plus read access for existing channels |
| Manage roles/assignments, transfer/delete server | Owner only |
| Create/revoke invitations | CreateInvites permission |
| Kick/ban | Owner, or ModerateMembers against a non-owner, non-moderator target |
| Edit message | Author, active message, current send eligibility |
| Delete channel message | Author or ModerateMessages, with current channel access |
| Read old DM | Conversation participant, including after blocking |
| Send/react in DM | Participant, no block in either direction, and accepted friendship or both users permitting a currently shared server |
| Download attachment | Conversation read access, plus active message or ownership of an unbound upload |

Custom role permissions are additive. Moderation/channel-management permissions do not grant private-channel reading. Private grants referencing other servers are rejected. A member leaving or being removed loses assignments and direct channel grants. There is no admin/global role that silently bypasses these checks.

## Uploads and rendering

Files are capped at 10 MiB by default, with five attachments per message and a pending-upload limit. The server checks actual bytes for PNG/JPEG/WebP/PDF signatures or valid nonbinary UTF-8 text. Storage keys are server-generated UUIDs; original names never determine paths. Files remain outside `wwwroot` and every retrieval is authorized. PDFs and text are downloaded rather than embedded. Content deleted from messages cannot be downloaded, and hourly cleanup reclaims old abandoned/deleted files.

Upload storage is bounded to 256 MiB per account and 2 GiB overall by default; the global check includes orphaned files on disk. Deleted/pending files count until cleanup. JSON bodies are capped at 64 KiB on Kestrel; uploads retain their separate multipart limit. Request timeouts, concurrency limits, and a two-second authorization-gate wait bound resource consumption. Authentication, rate limits, CSRF, and multipart parsing run before the gate; response/file streaming runs after it. SignalR allows five connections per account, 50 total, and throttles hub invocations before database access.

Production requires an HTTPS origin without credentials/path/query/fragment, explicit allowed hosts, an exact trusted proxy, SMTP TLS and credentials, and distinct strong database/Redis passwords. Untrusted forwarded headers cannot upgrade HTTP to HTTPS; production rejects plain HTTP application traffic. Production session, 2FA, and antiforgery cookies use the `__Host-` prefix. The supplied app container runs without root or capabilities, with a read-only root filesystem, bounded temporary storage, and log rotation. Supporting images rebuild older Go executables and apply available Alpine security updates.

Signature validation is not malware scanning. Before accepting untrusted public communities at meaningful scale, add quarantine and malware scanning through the storage boundary. Never render PDF/HTML/SVG uploads directly in the application's origin. Markdown has no raw HTML, no embedded external images, and uses safe link protocols with noopener/noreferrer.

## Operational limitations

- One application replica is required for the current authorization gate, local attachment storage, rate limits, and session termination registry.
- See [verification.md](verification.md) for completed checks and remaining host-specific release checks.
- Account deletion, data export, legal retention policy, recovery-code regeneration, and passkeys are not implemented.
- SMTP is synchronous: if delivery fails, users can retry verification/reset; there is no durable email outbox yet.
- Data Protection keys are protected by volume permissions, not an external KMS. Protect backups and host storage. Losing keys invalidates cookies and outstanding verification/reset tokens.
- NuGet restore audits transitive packages; CI also audits npm and scans the production image for high/critical findings. Weekly Dependabot updates are configured. An audit finding no known vulnerabilities is not a guarantee that none exist.

Logs omit chat bodies, file contents, passwords, invitation values, and authentication tokens. Do not enable verbose HTTP-body or query-string logging in production. Moderation audit rows contain identifiers/actions only.
