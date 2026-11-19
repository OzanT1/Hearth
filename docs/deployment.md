# Deploy and operate

This guide targets one Linux host running Docker Compose. Public deployment and purchasing infrastructure are not performed automatically.

For provider choices, a free hostname/email setup, and a small-VM walkthrough, read [free hosting](free-hosting.md).

## First release

1. Run the complete CI suite. In particular, verify the initial migration snapshot matches the runtime model and that two-browser tests pass.
2. Point a domain at the host. Allow inbound 80/443; leave PostgreSQL and Redis unexposed. Verify the fixed `172.30.0.0/24` Docker subnet does not overlap an existing host network; if changed, update `App__TrustedProxy` and Caddy's address together.
3. Copy `.env.example` to `.env`, run `chmod 600 .env`, and set the domain, distinct random database/Redis passwords (use `openssl rand -hex 32` twice), SMTP credentials, and `APPROVED_EMAIL_1` through `APPROVED_EMAIL_10`. Unused addresses stay empty. Keep `.env` outside Git. The SMTP host must support STARTTLS on the configured port, normally 587.
4. Build and initialize dependencies, run migration once, then start the application:

```sh
docker compose -f compose.production.yaml build --pull --no-cache
docker compose -f compose.production.yaml up -d --wait postgres redis
docker compose -f compose.production.yaml --profile tools run --rm migrate
docker compose -f compose.production.yaml up -d app caddy
curl -fsS https://your-domain.example/health/ready
```

Caddy obtains HTTPS certificates and proxies HTTP/WebSocket traffic. The app runs as UID 1654 with a read-only root filesystem; its writable `/data` volume contains uploads and persistent ASP.NET Data Protection keys. Production mail, public origin, explicit host/proxy settings, and approved signup addresses are mandatory. Signup is capped at ten accounts. Database and Redis ports are not published. The development mail catcher is excluded.

The dependency Dockerfile patches Alpine packages and rebuilds Caddy/gosu with a current Go toolchain and patched modules. Build **all** services, not just `app`, and keep the resulting images under the same release tag. `--no-cache` ensures package updates run even when the upstream image has not been republished. The Docker dynamic address range excludes Caddy's trusted static address. If modifying the subnet, update the dynamic range as well. The private PostgreSQL connection uses password authentication without Kerberos; use verified TLS when moving PostgreSQL across hosts.

Do not run multiple application replicas. A release migration is a separate one-off process; ordinary web startup does not apply schema changes. Missing migrations make readiness fail rather than silently creating a schema.

## Subsequent releases

Build a new image with an explicit `RELEASE_TAG`. Back up data first. Review generated migration SQL. For migrations requiring exclusive schema access, stop the app, run the migration job, deploy the new app, then check readiness and perform a test sign-in/message exchange. Do not assume an old image can run against a new schema; rollback may require restoring both database and files.

Use a secret manager and managed PostgreSQL/object storage when needed; the interfaces and environment settings support moving dependencies without redesigning the UI. Do not place production credentials in Docker images.

## Backup

Back up **both** the database and `/data` as a matched set. Stop application writes while capturing them. Redis is ephemeral and does not need a backup. Commands below run on the Linux deployment host; protect and encrypt the resulting backup directory.

```sh
mkdir -p backups
chmod 700 backups
umask 077
docker compose -f compose.production.yaml stop app
docker compose -f compose.production.yaml exec -T postgres pg_dump -U hearth -d hearth -Fc > backups/hearth.dump
docker compose -f compose.production.yaml run --rm --no-deps --user 0 --entrypoint tar app -C /data -czf - . > backups/app-data.tar.gz
docker compose -f compose.production.yaml start app
```

Schedule backups through your host/backup service and routinely test restoring them to a **separate** environment. Set your retention and encryption requirements explicitly. Restrict backup access because files and keys contain private data.

## Restore into a separate environment

Start a clean PostgreSQL service and initialize the application volume. Keep the app stopped during restoration. Restoring over an existing database/volume is destructive; use a fresh isolated Compose project and separate hostname for a drill.

```sh
docker compose -p hearth-restore -f compose.production.yaml up -d --wait postgres redis
docker compose -p hearth-restore -f compose.production.yaml exec -T postgres pg_restore -U hearth -d hearth --no-owner < backups/hearth.dump
docker compose -p hearth-restore -f compose.production.yaml run --rm -T --no-deps --user 0 --entrypoint tar app -C /data -xzf - < backups/app-data.tar.gz
```

The `run` restore command requires stdin to remain open (`-T` if your Compose configuration allocates a TTY). Check file ownership is UID/GID 1654. Apply any later release migrations, start the app behind an isolated proxy, and verify login, channel history, and attachments. Do not start the supplied Caddy service with production domain/ports during a restore drill on the same host. Change the isolated project's network subnet as well because the supplied production network uses a fixed subnet.

## Health, logs, and telemetry

- `/health/live`: process is accepting HTTP requests.
- `/health/ready`: PostgreSQL reachable and schema current; Redis loss is reported as degraded, allowing durable HTTP chat to continue.
- JSON logs: centralized errors, outbox retry failures, and attachment-cleanup failures. Message bodies and secrets are not logged.
- OpenTelemetry: ASP.NET/HTTP traces and metrics plus `hearth.outbox.delivered` and `hearth.outbox.failures`. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to send to your own collector.
- Inspect pending `Outbox` rows and their age/attempt count when alerts show delivery failures. Do not delete pending rows to hide a problem. Reconnecting clients refetch authoritative database state.

Add host-level monitoring for disk usage, database connections, backup success, container restarts, HTTP error rates, and certificate renewal. Test realistic concurrent users and upload sizes before declaring a supported capacity.
