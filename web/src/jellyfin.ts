/** Thin, defensive wrapper over the globals the Jellyfin web client exposes. */

interface JellyfinApiClient {
  getCurrentUserId(): string | null;
  accessToken(): string | null;
  serverAddress(): string;
  serverId(): string;
  getUrl(path: string, params?: Record<string, unknown>): string;
}

declare global {
  interface Window {
    ApiClient?: JellyfinApiClient;
    HomepagePlugin?: { version: string; stop: () => void };
    CustomElements?: { upgradeSubtree?: (el: Element) => void };
  }
}

export function client(): JellyfinApiClient | null {
  const c = window.ApiClient;
  return c && typeof c.getUrl === 'function' ? c : null;
}

export function currentUserId(): string | null {
  try {
    return client()?.getCurrentUserId() || null;
  } catch {
    return null;
  }
}

export function serverId(): string {
  try {
    return client()?.serverId() || '';
  } catch {
    return '';
  }
}

function authHeader(): Record<string, string> {
  const token = client()?.accessToken();
  return token ? { Authorization: `MediaBrowser Token="${token}"` } : {};
}

export class ApiError extends Error {
  constructor(message: string, public status: number) {
    super(message);
  }
}

export async function api<T>(path: string, init?: { method?: string; body?: unknown; signal?: AbortSignal }): Promise<T> {
  const c = client();
  if (!c) throw new ApiError('Jellyfin client not ready', 0);
  const response = await fetch(c.getUrl(path), {
    method: init?.method || 'GET',
    headers: {
      ...authHeader(),
      Accept: 'application/json',
      ...(init?.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: init?.body !== undefined ? JSON.stringify(init.body) : undefined,
    signal: init?.signal,
  });
  if (!response.ok) {
    let message = `${response.status}`;
    try {
      const data = await response.json();
      message = data?.detail || data?.message || data?.title || message;
    } catch {
      /* not json */
    }
    throw new ApiError(message, response.status);
  }
  return response.json() as Promise<T>;
}

/** Resolves a card image: Jellyfin-relative paths get the server address and a size; TMDB urls are used as is. */
export function imageUrl(path: string | undefined, maxWidth: number): string | undefined {
  if (!path) return undefined;
  if (/^https?:\/\//.test(path)) return path;
  const c = client();
  const base = (c?.serverAddress() || '').replace(/\/$/, '');
  return `${base}${path}&maxWidth=${Math.round(maxWidth * Math.min(window.devicePixelRatio || 1, 2))}&quality=90`;
}

export function detailsHash(itemId: string): string {
  const sid = serverId();
  return `#/details?id=${itemId}${sid ? `&serverId=${sid}` : ''}`;
}

export function goTo(hash: string) {
  if (window.location.hash === hash) return;
  window.location.hash = hash;
}
