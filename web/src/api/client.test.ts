import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, clearCsrf, send } from './client';
afterEach(() => { vi.unstubAllGlobals(); clearCsrf(); });
describe('secure API client', () => {
  it('obtains antiforgery token and sends it on mutations', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ token: 'secure' }))).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch); await send('/servers/', { name: 'friends' });
    expect(fetch.mock.calls[0][0]).toBe('/api/v1/auth/csrf');
    expect(fetch.mock.calls[1][1].headers.get('X-CSRF-TOKEN')).toBe('secure');
    expect(fetch.mock.calls[1][1].credentials).toBe('same-origin');
  });
  it('surfaces validation errors without swallowing them', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { Name: ['Name is required.'] } }), { status: 400 })));
    await expect(api('/servers/')).rejects.toThrow('Name is required.');
  });
});
