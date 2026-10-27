/**
 * Our pages live on Jellyfin's home route (#/home) with an `hp` query parameter, so the
 * Jellyfin router always shows the home view and we only swap what is inside it.
 */

export type PageName = 'home' | 'discover' | 'movies' | 'tv' | 'search' | 'grid' | 'requests' | 'settings';

export interface Route {
  isHome: boolean;
  /** Jellyfin home tab index (0 = home, 1 = favorites...). */
  tab: number;
  page: PageName;
  params: URLSearchParams;
}

const PAGES: PageName[] = ['home', 'discover', 'movies', 'tv', 'search', 'grid', 'requests', 'settings'];

export function currentRoute(): Route {
  const hash = window.location.hash || '';
  const match = hash.match(/^#!?\/(home(?:\.html)?)?(?:\?(.*))?$/i);
  const params = new URLSearchParams(match?.[2] || '');
  const raw = (params.get('hp') || 'home').toLowerCase() as PageName;
  return {
    isHome: Boolean(match),
    tab: Number.parseInt(params.get('tab') || '0', 10) || 0,
    page: PAGES.includes(raw) ? raw : 'home',
    params,
  };
}

export function pageHash(page: PageName, extra?: Record<string, string>): string {
  const params = new URLSearchParams();
  if (page !== 'home') params.set('hp', page);
  for (const [key, value] of Object.entries(extra || {})) params.set(key, value);
  const query = params.toString();
  return `#/home${query ? `?${query}` : ''}`;
}

export function isExcludedRoute(): boolean {
  const hash = (window.location.hash || '').toLowerCase();
  return (
    hash.startsWith('#/dashboard') ||
    hash.startsWith('#/configurationpage') ||
    hash.startsWith('#/video') ||
    hash.startsWith('#/login') ||
    hash.startsWith('#/selectserver') ||
    hash.startsWith('#/wizard') ||
    hash.startsWith('#/startup')
  );
}
