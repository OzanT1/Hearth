export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}
let token: string | undefined;
export function clearCsrf() { token = undefined; }
async function csrf() {
  if (!token) {
    const response = await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' });
    if (!response.ok) throw new ApiError(response.status, 'Could not establish a secure session. Please retry.');
    token = (await response.json()).token;
  }
  return token!;
}
export async function api<T = void>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  if (init.method && !['GET', 'HEAD'].includes(init.method)) headers.set('X-CSRF-TOKEN', await csrf());
  const response = await fetch(`/api/v1${path}`, { ...init, headers, credentials: 'same-origin' });
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    const errors = data.errors ? Object.values(data.errors).flat().join(' ') : '';
    throw new ApiError(response.status, errors || data.title || (response.status === 429 ? 'A little too fast. Please wait and retry.' : response.status === 401 ? 'Please sign in again.' : 'The request could not be completed.'));
  }
  return response.status === 204 ? undefined as T : await response.json() as T;
}
export function send<T = void>(path: string, body: unknown = {}, method = 'POST') { return api<T>(path, { method, body: JSON.stringify(body) }); }
