# Run and test Hearth locally

The app uses .NET 10 SDK (see `global.json`) and Node.js. Docker provides an isolated database, Redis, and an email catcher. No public hosting or paid services are required.

## Start

Start Docker Desktop's Linux engine. From `C:\Users\ozanu\source\repos\DiscordClone`:

```powershell
docker compose up -d --wait postgres redis mailpit
dotnet run --project src/Hearth.Api -- --migrate
dotnet run --project src/Hearth.Api
```

In a second terminal:

```powershell
cd C:\Users\ozanu\source\repos\DiscordClone\web
npm run dev
```

Open **http://localhost:5173**. Use `localhost` consistently for the frontend: `127.0.0.1` is a different origin and won't pass the configured live-connection origin checks.

The API runs on port 5080. Its `/health/ready` endpoint should report `healthy` and `redis: true`. The project's database uses port 15432, so it can coexist with your installed PostgreSQL service.

## Create accounts

Register through the app. Open **http://localhost:8025** to read the verification email, follow its link, verify, and sign in. All development emails stay in this local inbox.

For two independent users, use a normal Chrome window and an Incognito window. Tabs in one browser profile share a login; this is useful for testing multi-tab behavior, but not for logging in as two different users.

For sample accounts and conversations, run `node scripts/demo.mjs` from the repository root while the API is running. The script prints the two email addresses and password. Sign in with those credentials to explore the sample server. Each run creates a fresh demo.

## Try the features

1. Create a server with the plus button on the server rail. It starts with a `general` channel.
2. Choose **Invite your people**, create a link, and open it in the second user's window. Accept the invitation.
3. Send messages in both windows. Try Markdown, replies, reactions, editing, deleting, and attaching an image, PDF, or text file.
4. Click another person in the member panel to start a DM. In Home, find users by username, send/accept friend requests, and try blocking/unblocking.
5. Open the server menu for settings. Add a role, assign permissions under Members, and create a private channel with a member or role allowlist. Verify the other user only sees it while allowed.
6. Remove the second user's private-channel access while their window is open. The channel disappears, and history/file URLs are denied. Try kick, ban, unban, ownership transfer, and leaving using throwaway test servers.
7. Open Account settings to edit your display name, enable friends-only DMs, or set up an authenticator. Save recovery codes when enabling two-factor authentication. **Sign out on all devices** also signs out the user's other open tabs.
8. Leave a conversation in one tab, send it a message from another user, then read it in a second tab of the recipient's profile. Unread counts update across tabs.
9. Temporarily go offline through browser developer tools, send from the other user, and reconnect. The missed message appears without a page refresh.
10. Request a password reset from the sign-in screen. Use its link in Mailpit and verify the previous password/session stops working.

## Stop

Press **Ctrl+C in each terminal running the API or frontend**. Closing a browser tab does not stop either server.

From the repository root, stop the background dependencies with:

```powershell
docker compose stop
```

Start them again with the command above. Database data and uploads remain on disk. Avoid `down --volumes` unless you intend to erase the local database.

## Run automated checks

Stop the API before tests rebuild it on Windows. Keep Docker Desktop running:

```powershell
dotnet test Hearth.slnx
```

Restart the API, then run in `web`:

```powershell
npm run generate:api
npm run build
npm run test
$env:E2E_BROWSER_CHANNEL = 'chrome'
npm run test:e2e
```

Playwright starts and stops its own frontend unless one is already running. The current verified results are recorded in [verification.md](verification.md).
