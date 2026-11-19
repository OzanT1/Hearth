// Seed a LOCAL running development app through its public API and Mailpit.
// No authentication or authorization bypass is installed in the application.
const base = process.env.HEARTH_URL ?? 'http://localhost:5080';
if (!['localhost', '127.0.0.1'].includes(new URL(base).hostname)) throw new Error('Demo seeding is restricted to a local development app.');
const mail = process.env.MAILPIT_URL ?? 'http://localhost:8025';
const web = process.env.HEARTH_WEB_URL ?? 'http://localhost:5173';
if (!['localhost', '127.0.0.1'].includes(new URL(web).hostname)) throw new Error('Demo web destinations must be local.');
const password = 'Hearth demo passphrase 2026!';
function session() {
  const cookies = new Map();
  async function request(path, body, method = 'POST') {
    let token;
    if (method !== 'GET') token = (await request('/auth/csrf', undefined, 'GET')).token;
    const headers = { Cookie: [...cookies].map(([k,v]) => `${k}=${v}`).join('; '), ...(token ? { 'X-CSRF-TOKEN': token, 'Content-Type': 'application/json' } : {}) };
    const response = await fetch(`${base}/api/v1${path}`, { method, headers, body: method === 'GET' ? undefined : JSON.stringify(body ?? {}) });
    for (const cookie of response.headers.getSetCookie()) { const first = cookie.split(';')[0]; const index = first.indexOf('='); cookies.set(first.slice(0,index), first.slice(index+1)); }
    if (!response.ok) throw new Error(`${path}: ${response.status} ${await response.text()}`);
    return response.status === 204 ? undefined : response.json();
  }
  return request;
}
const suffix = Date.now().toString(36);
async function account(name) {
  const request = session(); const email = `${name}.${suffix}@example.test`; const userName = `${name}_${suffix}`;
  await request('/auth/register', { email, userName, password });
  const inbox = await (await fetch(`${mail}/api/v1/messages`)).json(); const message = inbox.messages.find(m => m.To.some(t => t.Address === email));
  if (!message) throw new Error('Verification email not found in Mailpit.');
  const detail = await (await fetch(`${mail}/api/v1/message/${message.ID}`)).json();
  const link = detail.Text.match(/https?:\/\/[^\s]+\/verify\?[^\s]+/)[0]; const params = new URL(link).searchParams;
  await request('/auth/verify', Object.fromEntries(params)); await request('/auth/login', { email, password });
  await request('/auth/me', { displayName: name === 'jamie' ? 'Jamie Davis' : 'Alex Morgan', allowSharedServerDms: true }, 'PATCH');
  console.log(`Demo account: ${email} / ${password}`);
  return request;
}
const jamie = await account('jamie'); const alex = await account('alex');
const server = await jamie('/servers/', { name: 'The Sunday Club' });
const invite = await jamie(`/servers/${server.id}/invites/`, { expiresInHours: 168 }); await alex('/invites/redeem', { token: invite.token });
for (const [position, name] of ['introductions','the-good-stuff','project-corner','off-topic'].entries()) await jamie(`/servers/${server.id}/channels`, { name, position: position + 1, isPrivate: false, roleIds: [], userIds: [] });
const channels = await jamie(`/servers/${server.id}/channels`, undefined, 'GET'); const general = channels.find(c => c.name === 'general');
const messages = [[jamie, 'Welcome to the Sunday Club! A little space for good ideas and even better company. ☕'], [alex, 'Made it! This already feels like home.'], [jamie, 'What’s everyone working on this week? I’m finally giving that side project some love.'], [alex, 'A tiny habit tracker. Keeping it simple: React on the front, .NET in the back. 🚀']];
for (const [request, content] of messages) await request(`/conversations/${general.id}/messages`, { clientId: crypto.randomUUID(), content, attachmentIds: [] });
console.log(`Demo ready. Open ${web}. Invitation: ${web}/invite/${invite.token}`);
