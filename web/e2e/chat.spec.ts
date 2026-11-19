import { expect, test } from '@playwright/test';
import type { Page, APIRequestContext } from '@playwright/test';

async function write(page: Page, path: string, data: unknown = {}, method = 'POST') {
  const csrf = await (await page.request.get('/api/v1/auth/csrf')).json();
  return page.request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-CSRF-TOKEN': csrf.token } });
}

async function register(page: Page, request: APIRequestContext, suffix: string) {
  const username = `u${Date.now()}${suffix}`; const email = `${username}@example.test`;
  await page.goto('/register');
  await page.getByLabel('Username', { exact: true }).fill(username); await page.getByLabel('Email address').fill(email); await page.getByLabel('Password', { exact: true }).fill('a secure test passphrase');
  await page.getByRole('button', { name: 'Create your account' }).click();
  await expect(page.getByRole('status')).toContainText('Check your inbox');
  let link = '';
  await expect.poll(async () => {
    const response = await request.get(`${process.env.MAILPIT_URL ?? 'http://localhost:8025'}/api/v1/messages`);
    const data = await response.json(); const message = data.messages?.find((m: { To: { Address: string }[] }) => m.To.some(t => t.Address === email));
    if (!message) return false;
    const detail = await (await request.get(`${process.env.MAILPIT_URL ?? 'http://localhost:8025'}/api/v1/message/${message.ID}`)).json();
    link = detail.Text.match(/https?:\/\/[^\s]+\/verify\?[^\s]+/)?.[0] ?? ''; return !!link;
  }).toBe(true);
  await page.goto(link); await page.getByRole('button', { name: 'Verify my email' }).click();
  await page.getByRole('link', { name: 'Go to sign in' }).click();
  await page.getByLabel('Email address').fill(email); await page.getByLabel('Password', { exact: true }).fill('a secure test passphrase'); await page.getByRole('button', { name: 'Sign in to Hearth' }).click();
  await expect(page.getByRole('heading', { name: 'Friends', exact: true })).toBeVisible();
  return username;
}
test('two verified users join, chat live, react, reply, and message directly', async ({ browser, request }) => {
  const first = await browser.newContext(); const second = await browser.newContext();
  const owner = await first.newPage(); const member = await second.newPage();
  await register(owner, request, 'a'); const memberName = await register(member, request, 'b');
  await owner.getByRole('button', { name: 'Create a server', exact: true }).click();
  await owner.getByLabel('Server name').fill('The workshop'); await owner.getByRole('button', { name: 'Create server', exact: true }).click();
  await expect(owner.getByRole('heading', { name: 'general', exact: true })).toBeVisible();
  await owner.getByRole('button', { name: 'Invite your people' }).click(); await owner.getByRole('button', { name: 'Create invitation link' }).click();
  const link = await owner.getByLabel('Invitation link').inputValue(); await owner.getByRole('button', { name: 'Close', exact: true }).click();
  await member.goto(link); await member.getByRole('button', { name: 'Accept invitation' }).click();
  await expect(member.getByRole('heading', { name: 'general', exact: true })).toBeVisible();
  await expect(owner.locator('.connection-banner')).toHaveCount(0);
  await expect(member.locator('.connection-banner')).toHaveCount(0);
  await owner.getByLabel('Message', { exact: true }).fill('Hello from the workshop'); await owner.getByRole('button', { name: 'Send message' }).click();
  await expect(member.getByText('Hello from the workshop', { exact: true })).toBeVisible();
  const message = member.getByTestId('message').filter({ hasText: 'Hello from the workshop' });
  await message.hover(); await message.getByRole('button', { name: 'Reply', exact: true }).click();
  await member.getByLabel('Message', { exact: true }).fill('A reply, in real time'); await member.getByRole('button', { name: 'Send message' }).click();
  await expect(owner.getByText('A reply, in real time', { exact: true })).toBeVisible();
  await message.hover(); await message.getByRole('button', { name: 'Add reaction' }).click(); await message.getByRole('button', { name: '🎉', exact: true }).click();
  await expect(owner.getByRole('button', { name: 'React 🎉, 1' })).toBeVisible();
  await owner.locator('.member').filter({ hasText: memberName }).click();
  await expect(owner.getByRole('heading', { name: memberName, exact: true })).toBeVisible();
  await owner.getByLabel('Message', { exact: true }).fill('A private hello'); await owner.getByRole('button', { name: 'Send message' }).click();
  await member.getByRole('button', { name: 'Home', exact: true }).click(); await expect(member.locator('.dm-link')).toHaveCount(1); await member.locator('.dm-link').click();
  await expect(member.getByText('A private hello', { exact: true })).toBeVisible();
  await second.setOffline(true);
  await owner.getByLabel('Message', { exact: true }).fill('While you were disconnected'); await owner.getByRole('button', { name: 'Send message' }).click();
  await expect(owner.getByText('While you were disconnected', { exact: true })).toBeVisible();
  await second.setOffline(false);
  await expect(member.getByText('While you were disconnected', { exact: true })).toBeVisible();
  await owner.reload(); await expect(owner.getByText('A private hello', { exact: true })).toBeVisible();
  await first.close(); await second.close();
});

test('private content and uploads are revoked on an existing live connection', async ({ browser, request }) => {
  const first = await browser.newContext(); const second = await browser.newContext();
  const owner = await first.newPage(); const member = await second.newPage();
  const frames: string[] = [];
  const ownerFrames: string[] = [];
  member.on('websocket', socket => socket.on('framereceived', event => frames.push(String(event.payload))));
  owner.on('websocket', socket => socket.on('framereceived', event => ownerFrames.push(String(event.payload))));
  await register(owner, request, 'p'); await register(member, request, 'q');
  const memberId = (await (await member.request.get('/api/v1/auth/me')).json()).id;
  const server = await (await write(owner, '/servers/', { name: 'Private access test' })).json();
  const invite = await (await write(owner, `/servers/${server.id}/invites/`, { expiresInHours: 1 })).json();
  expect((await write(member, '/invites/redeem', { token: invite.token })).ok()).toBe(true);
  const config = { name: 'private-room', position: 1, isPrivate: true, roleIds: [], userIds: [memberId] };
  const channel = await (await write(owner, `/servers/${server.id}/channels`, config)).json();
  await owner.goto(`/app?server=${server.id}&channel=${channel.id}`);
  await member.goto(`/app?server=${server.id}&channel=${channel.id}`);
  await expect(member.getByRole('heading', { name: 'private-room', exact: true })).toBeVisible();
  await expect(member.locator('.connection-banner')).toHaveCount(0);
  await owner.locator('input[type=file]').setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('A private attachment') });
  await expect(owner.locator('.pending-files')).toContainText('notes.txt');
  await owner.getByLabel('Message', { exact: true }).fill('Private message with a file'); await owner.getByRole('button', { name: 'Send message' }).click();
  await expect(member.getByText('Private message with a file', { exact: true })).toBeVisible();
  const attachment = member.getByRole('link', { name: /notes.txt/ });
  const filePath = (await attachment.getAttribute('href'))!;
  const download = await member.request.get(filePath); expect(download.ok()).toBe(true); expect(await download.text()).toBe('A private attachment');
  expect((await write(owner, `/servers/${server.id}/channels/${channel.id}`, { ...config, userIds: [] }, 'PUT')).ok()).toBe(true);
  await expect(member.getByRole('button', { name: 'private-room', exact: true })).toHaveCount(0);
  expect((await member.request.get(`/api/v1/conversations/${channel.id}/messages`)).status()).toBe(404);
  expect((await member.request.get(filePath)).status()).toBe(404);
  const boundary = frames.length;
  const ownerBoundary = ownerFrames.length;
  await owner.getByLabel('Message', { exact: true }).fill('After private access was revoked'); await owner.getByRole('button', { name: 'Send message' }).click();
  await expect(owner.getByText('After private access was revoked', { exact: true })).toBeVisible();
  // Wait for the authorized owner's event to establish that the outbox dispatched.
  await expect.poll(() => ownerFrames.slice(ownerBoundary).some(frame => frame.includes(channel.id))).toBe(true);
  await expect(member.getByText('After private access was revoked', { exact: true })).toHaveCount(0);
  expect(frames.slice(boundary).filter(frame => frame.includes(channel.id))).toHaveLength(0);
  expect((await write(owner, `/servers/${server.id}/members/${memberId}/kick`)).ok()).toBe(true);
  await expect(member.getByRole('button', { name: 'Private access test', exact: true })).toHaveCount(0);
  expect((await member.request.get(`/api/v1/servers/${server.id}/channels`)).status()).toBe(404);
  await first.close(); await second.close();
});

test('editing, deletion, multi-tab reads, and logout update existing browser sessions', async ({ browser, request }) => {
  const first = await browser.newContext(); const second = await browser.newContext();
  const owner = await first.newPage(); const member = await second.newPage();
  await register(owner, request, 'e'); await register(member, request, 'f');
  const memberId = (await (await member.request.get('/api/v1/auth/me')).json()).id;
  const server = await (await write(owner, '/servers/', { name: 'Session test' })).json();
  const invite = await (await write(owner, `/servers/${server.id}/invites/`, { expiresInHours: 1 })).json();
  expect((await write(member, '/invites/redeem', { token: invite.token })).ok()).toBe(true);
  const channel = (await (await owner.request.get(`/api/v1/servers/${server.id}/channels`)).json())[0];
  await owner.goto(`/app?server=${server.id}&channel=${channel.id}`);
  await member.goto(`/app?server=${server.id}&channel=${channel.id}`);
  await expect(owner.getByRole('heading', { name: 'general', exact: true })).toBeVisible();
  await expect(member.locator('.connection-banner')).toHaveCount(0);
  await owner.getByLabel('Message', { exact: true }).fill('Original text'); await owner.getByRole('button', { name: 'Send message' }).click();
  await expect(member.getByText('Original text', { exact: true })).toBeVisible();
  const message = owner.getByTestId('message').filter({ hasText: 'Original text' });
  await message.hover(); await message.getByRole('button', { name: 'Edit message' }).click();
  await owner.getByRole('dialog').locator('textarea').fill('Edited text'); await owner.getByRole('button', { name: 'Save changes' }).click();
  await expect(member.getByText('Edited text', { exact: true })).toBeVisible();
  const edited = owner.getByTestId('message').filter({ hasText: 'Edited text' }); await edited.hover(); await edited.getByRole('button', { name: 'Delete message' }).click();
  await owner.getByRole('dialog').getByRole('button', { name: 'Delete message' }).click();
  await expect(member.getByText('This message was deleted.', { exact: true })).toBeVisible();
  await member.getByRole('button', { name: 'Home', exact: true }).click();
  const dm = await (await write(owner, '/dms', { userId: memberId })).json();
  expect((await write(owner, `/conversations/${dm.id}/messages`, { clientId: crypto.randomUUID(), content: 'Read in another tab', attachmentIds: [] })).ok()).toBe(true);
  await expect(member.locator('.dm-link .unread')).toHaveText('1');
  const otherTab = await second.newPage(); await otherTab.goto(`/app?channel=${dm.id}`);
  await expect(otherTab.getByText('Read in another tab', { exact: true })).toBeVisible();
  await expect(member.locator('.dm-link .unread')).toHaveCount(0);
  expect((await write(otherTab, '/auth/logout')).ok()).toBe(true);
  await expect(member).toHaveURL(/\/login$/);
  await expect(otherTab).toHaveURL(/\/login$/);
  expect((await member.request.get('/api/v1/auth/me')).status()).toBe(401);
  await first.close(); await second.close();
});

test('mobile navigation and conversation controls remain usable', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await register(page, request, 'm');
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.getByRole('button', { name: 'Create a server', exact: true }).click();
  await page.getByLabel('Server name').fill('Mobile workshop'); await page.getByRole('button', { name: 'Create server', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'general', exact: true })).toBeVisible();
  await expect(page.locator('.members-panel')).toHaveCount(0);
  await page.getByLabel('Message', { exact: true }).fill('Hello from a small screen'); await page.getByRole('button', { name: 'Send message' }).click();
  await expect(page.getByText('Hello from a small screen', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Toggle members' }).click();
  await expect(page.locator('.members-panel')).toBeVisible();
  await page.getByRole('button', { name: 'Close members' }).click();
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.getByRole('button', { name: 'Create channel', exact: true }).click();
  await page.getByLabel('Channel name (lowercase letters, numbers, hyphens)').fill('mobile-chat'); await page.getByRole('dialog').getByRole('button', { name: 'Create channel', exact: true }).click();
  await page.getByRole('button', { name: 'mobile-chat', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'mobile-chat', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  const layout = await page.evaluate(() => ['.main-panel', '.conversation-layout', '.chat', '.message-scroll', '.channel-welcome', '.composer'].map(selector => {
    const element = document.querySelector(selector)!;
    const rect = element.getBoundingClientRect();
    return { selector, right: rect.right, width: rect.width, viewport: window.innerWidth };
  }));
  expect(layout.filter(element => element.right > element.viewport + 1)).toEqual([]);
  await page.screenshot({ path: 'test-results/mobile-conversation.png', fullPage: true, animations: 'disabled' });
});
