import axios, { AxiosError } from 'axios';
import type { AuthResponse, MfaChallenge } from './types';

/**
 * Token handling:
 *  - The access token lives only in memory (never localStorage) so XSS cannot persist it.
 *  - The refresh token is an HttpOnly cookie scoped to /api/auth; JavaScript never sees it.
 *  - Refreshes are single-flight per tab and serialized across tabs with the Web Locks API, so two tabs
 *    never present the same (rotating) refresh token at once.
 */

let accessToken: string | null = null;
let expiresAt = 0;

const authHttp = axios.create({ baseURL: '/api/auth', withCredentials: true, timeout: 15000 });
const channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('sm-auth') : null;

export const tokenStore = {
  get: () => accessToken,
  expiresAt: () => expiresAt,
  set(response: AuthResponse) {
    accessToken = response.accessToken;
    expiresAt = new Date(response.expiresAt).getTime();
  },
  clear() {
    accessToken = null;
    expiresAt = 0;
  },
};

export const isExpiringSoon = (skewMs = 30_000) => !accessToken || Date.now() >= expiresAt - skewMs;

async function withCrossTabLock<T>(fn: () => Promise<T>): Promise<T> {
  const locks = (navigator as Navigator & { locks?: LockManager }).locks;
  return locks ? locks.request('sm-auth-refresh', fn) : fn();
}

let inflight: Promise<AuthResponse> | null = null;

/** Exchanges the refresh cookie for a new access token (and a rotated refresh cookie). */
export function refreshSession(): Promise<AuthResponse> {
  inflight ??= withCrossTabLock(async () => {
    try {
      return await authHttp.post<AuthResponse>('/refresh').then((r) => r.data);
    } catch (error) {
      // Lost a race with another tab: the cookie has already been rotated, so one retry succeeds.
      if (error instanceof AxiosError && error.response?.status === 401) {
        await new Promise((r) => setTimeout(r, 250));
        return authHttp.post<AuthResponse>('/refresh').then((r) => r.data);
      }
      throw error;
    }
  })
    .then((response) => {
      tokenStore.set(response);
      return response;
    })
    .finally(() => {
      inflight = null;
    });
  return inflight;
}

/** Returns a non-expired access token, refreshing first if needed. */
export async function getValidAccessToken(): Promise<string> {
  if (isExpiringSoon()) await refreshSession();
  return accessToken ?? '';
}

export const authRequests = {
  login: (userName: string, password: string, code?: string) =>
    authHttp.post<AuthResponse | MfaChallenge>('/login', { userName, password, code: code || undefined }).then((r) => r.data),
  logout: () => authHttp.post('/logout').catch(() => undefined),
};

/** Cross-tab sign-out notification. */
export function broadcastLogout() {
  channel?.postMessage('logout');
}

export function onLogoutBroadcast(handler: () => void): () => void {
  if (!channel) return () => {};
  const listener = (e: MessageEvent) => e.data === 'logout' && handler();
  channel.addEventListener('message', listener);
  return () => channel.removeEventListener('message', listener);
}
