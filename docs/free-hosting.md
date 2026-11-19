# Free hosting for a small Hearth community

Checked **October 16, 2026**. This application needs one continuously available .NET process, PostgreSQL, Redis, persistent uploads and Data Protection keys, HTTPS, WebSockets, and an SMTP sender. Ten users do not remove those requirements.

## Options

| Option | Fit for this project | Limits and costs to check |
| --- | --- | --- |
| **Oracle Cloud Always Free VM** | Best fit without changing the architecture. Run the supplied Compose stack on one ARM VM. | Current documentation allows a combined 2 OCPUs / 12 GB ARM allocation and 200 GB combined boot/block storage. Capacity can be unavailable, and idle VMs can be reclaimed. Check the **Always Free** allocation in your own tenancy before creating resources. [Oracle limits](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm). |
| **Google Cloud Free Tier VM** | Possible with compromises; not the first choice for a strict zero-cost deployment. | One eligible US `e2-micro`, 30 GB standard disk and 1 GB outbound traffic are included. A public IPv4 address is separately charged, currently $0.005/hour after a one-hour allowance. Avoiding that needs IPv6 or another ingress arrangement; memory is also tight for this stack. [Compute limits](https://docs.cloud.google.com/free/docs/free-cloud-features), [IP pricing](https://cloud.google.com/vpc/network-pricing#ipaddress). |
| **Railway Free** | Small experiments, not a dependable always-on home for this stack. | $1/month resource credit, 512 MB maximum RAM per service and 0.5 GB volume limit. App, database, Redis, and files consume the allowance together. [Plan details](https://docs.railway.com/pricing/plans). |
| **AWS Free plan** | Temporary testing option. | New-account credits last up to six months or until exhausted. This is not an ongoing free hosting plan. [AWS Free Tier](https://aws.amazon.com/free/). |

Render Free cannot safely host the app unchanged: its filesystem is ephemeral, persistent disks are unavailable, SMTP ports are blocked, and its free database expires after 30 days. [Render's restrictions](https://render.com/docs/free). Fly.io has a short trial and no recurring free compute allowance for new accounts. [Fly pricing](https://docs.fly.io/about/pricing). Static-only hosting cannot run the .NET backend.

Free infrastructure has no guarantee of indefinite availability. Keep an off-host backup and stay within the provider's free allowances. Budget alerts alone do not stop charges; avoid enabling paid resources or automatic upgrades. Do not buy a load balancer, managed database, Kubernetes cluster, or paid domain for this small deployment.

## Recommended setup: one Oracle VM

1. Create an Always Free eligible **Ubuntu ARM** VM in your home region, for example **2 OCPUs, 6 GB RAM, 50 GB boot disk**. This is within the documented allocation; confirm the console's estimate and eligibility. Use SSH keys. Restrict inbound SSH to your own IP; allow TCP **80/443** publicly and optionally UDP **443**. Apply the same rules in the cloud security list and the host firewall. PostgreSQL, Redis, and the app's port 8080 must remain private.
2. Install Docker Engine and the Compose plugin using [Docker's Ubuntu instructions](https://docs.docker.com/engine/install/ubuntu/), then copy or clone this repository to the VM. The Dockerfile supports building natively on ARM. No .NET or Node installation is needed on the VM.
3. Obtain a free hostname from [Duck DNS](https://www.duckdns.org/) and point it at the VM's public IP. An existing domain also works. Set `DOMAIN` to the hostname **without** `https://`, a port, or a path. Ensure DNS points to the VM before starting Caddy. Remove an incorrect AAAA record if IPv6 is not configured.
4. Configure `.env` as described below. Create distinct secrets with `openssl rand -hex 32`, once for PostgreSQL and once for Redis. Do not reuse passwords.
5. Run the release commands, then register an approved account, verify email, and test messaging and an attachment with a second user.
6. Set up encrypted off-host backups using [the operations guide](deployment.md), test restoring them, and apply host/package/container security updates regularly. One VM is sufficient; keep exactly one app replica.

On the VM, from the repository root:

```sh
cp .env.example .env
chmod 600 .env
# Edit .env with your hostname, actual credentials, and approved email addresses.

docker compose -f compose.production.yaml config --quiet
docker compose -f compose.production.yaml build --pull --no-cache
docker compose -f compose.production.yaml up -d --wait postgres redis
docker compose -f compose.production.yaml --profile tools run --rm migrate
docker compose -f compose.production.yaml up -d app caddy
curl --fail https://YOUR-HOSTNAME/health/ready
```

Use `sudo docker` if your Linux account does not have Docker access. The fixed Docker subnet `172.30.0.0/24` must not overlap the VM's other networks. If you change it, update Caddy's IP and `App__TrustedProxy` together in Compose. Caddy obtains and renews the HTTPS certificate and proxies WebSockets automatically. Leave its data volume intact across releases.

## Email without paying for a domain

A dedicated personal Gmail account with 2-Step Verification and an **app password**, if available for that account, can send the few verification/reset emails this group needs. Keep it separate from your personal mailbox. App passwords have broad mailbox access, so protect the secret and revoke it when no longer used. Google documents the prerequisites and account restrictions in [app-password help](https://support.google.com/accounts/answer/185833?hl=en).

Example `.env` mail settings (substitute your actual account and generated app password):

```dotenv
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_FROM=your-dedicated-account@gmail.com
SMTP_USER=your-dedicated-account@gmail.com
SMTP_PASSWORD=your-generated-app-password
```

Use STARTTLS on **587** ([Gmail SMTP settings](https://developers.google.com/workspace/gmail/imap/imap-smtp)); the current mail adapter does not use implicit TLS on 465. Verify delivery and spam-folder behavior before inviting users. A Gmail account/password change can revoke the app password. If your account cannot create app passwords, use a compatible SMTP provider instead.

[Brevo](https://www.brevo.com/products/transactional-email/) offers 300 free sends/day; sender/domain verification and account approval still apply. If you already control a domain, this is another option. Do not assume a free DNS hostname lets you authenticate the provider's parent domain for mail.

## Registration and storage settings

Set `APPROVED_EMAIL_1` through `APPROVED_EMAIL_10` to your users' actual addresses, leaving unused entries empty. Addresses are compared case-insensitively. The public registration form works only for those addresses, and every account must still verify email. The server returns the same generic response for rejected signups. Removing an address prevents a new signup; it does **not** delete or disable an existing account.

Production defaults are 10 registered accounts, 10 MiB per file, 256 MiB of uploads per account, and 2 GiB total upload storage. These are upload limits, not a limit on total database/log/backup disk usage. Monitor free disk space. Email endpoints permit five requests per IP per 15 minutes; onboarding ten people behind one IP may require two batches. Per-account mail cooldowns also apply.

The repository contains no production credentials. You must supply `.env` on the VM; never send it in chat, commit it, or bake it into the image. Test readiness, account verification, password reset, live messaging, and file persistence on the actual host before making it available to your group.
