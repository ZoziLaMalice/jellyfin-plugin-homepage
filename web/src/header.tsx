import { render } from 'preact';
import { Icon } from './components/Icon';
import { currentRoute, isExcludedRoute, pageHash } from './route';
import type { ClientLayout, ClientTab } from './types';

const ICONS: Record<ClientTab['type'], string> = {
  Home: 'home',
  Favorites: 'favorite_border',
  Discover: 'explore',
  DiscoverMovies: 'movie',
  DiscoverTv: 'live_tv',
  Search: 'search',
  Requests: 'playlist_add_check',
  Library: 'video_library',
  Link: 'link',
};

function tabHref(tab: ClientTab): string {
  switch (tab.type) {
    case 'Home':
      return pageHash('home');
    case 'Favorites':
      return '#/home?tab=1';
    case 'Discover':
      return pageHash('discover');
    case 'DiscoverMovies':
      return pageHash('movies');
    case 'DiscoverTv':
      return pageHash('tv');
    case 'Search':
      return pageHash('search');
    case 'Requests':
      return pageHash('requests');
    case 'Library':
      return tab.target || '#/home';
    case 'Link':
      return tab.target || '#/home';
  }
}

function isActive(tab: ClientTab): boolean {
  const route = currentRoute();
  const hash = window.location.hash;
  switch (tab.type) {
    case 'Home':
      return route.isHome && route.tab === 0 && route.page === 'home';
    case 'Favorites':
      return route.isHome && route.tab === 1;
    case 'Discover':
      return route.isHome && route.page === 'discover';
    case 'DiscoverMovies':
      return route.isHome && route.page === 'movies';
    case 'DiscoverTv':
      return route.isHome && route.page === 'tv';
    case 'Search':
      return route.isHome && route.page === 'search';
    case 'Requests':
      return route.isHome && route.page === 'requests';
    case 'Library':
      return Boolean(tab.target) && hash.startsWith(tab.target!.split('&')[0]);
    case 'Link':
      return Boolean(tab.target) && hash === tab.target;
  }
}

function Nav({ tabs }: { tabs: ClientTab[] }) {
  return (
    <>
      {tabs.map((tab) => {
        const href = tabHref(tab);
        const external = /^https?:\/\//.test(href);
        return (
          <a
            key={`${tab.type}-${tab.title}`}
            class={`hp-nav-link ${isActive(tab) ? 'is-active' : ''} ${tab.type === 'Search' ? 'hp-nav-search' : ''}`}
            href={href}
            target={external ? '_blank' : undefined}
            rel={external ? 'noopener noreferrer' : undefined}
            title={tab.title}
          >
            <Icon name={ICONS[tab.type] || 'link'} />
            <span>{tab.title}</span>
          </a>
        );
      })}
    </>
  );
}

let navElement: HTMLElement | null = null;
let replaceSearch = false;
let searchHooked = false;

/** Jellyfin 12 has two top bars: the MUI app bar (current layout) and the legacy skinHeader. */
function findHeaderHost(): { container: Element; before: Node | null; kind: 'modern' | 'legacy' } | null {
  const toolbar = document.querySelector('header.MuiAppBar-root .MuiToolbar-root');
  if (toolbar && toolbar.getBoundingClientRect().width > 0) {
    // Keep the menu button (and the native library links, if any) on the left of our links.
    const children = Array.from(toolbar.children).filter((c) => !c.classList.contains('hp-nav'));
    const stack = children.find((c) => c.classList.contains('MuiStack-root'));
    const anchor = stack || children[0] || null;
    return { container: toolbar, before: anchor ? anchor.nextSibling : toolbar.firstChild, kind: 'modern' };
  }
  const headerTop = document.querySelector('.skinHeader .headerTop');
  if (headerTop && headerTop.getBoundingClientRect().width > 0) {
    const left = headerTop.querySelector('.headerLeft');
    return { container: headerTop, before: left ? left.nextSibling : headerTop.firstChild, kind: 'legacy' };
  }
  return null;
}

function onDocumentClick(event: MouseEvent) {
  if (!replaceSearch || !navElement) return;
  const target = event.target as Element | null;
  const search = target?.closest('header.MuiAppBar-root a[href="#/search"], .skinHeader .headerSearchButton');
  if (!search) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  window.location.hash = pageHash('search');
}

/** Adds (or refreshes) our navigation inside Jellyfin's top bar. */
export function syncHeader(layout: ClientLayout | null) {
  const enabled = Boolean(layout?.enabled && layout.header.enabled && layout.header.tabs.length) && !isExcludedRoute();
  const host = enabled ? findHeaderHost() : null;
  document.documentElement.classList.toggle('hp-header', Boolean(host));
  document.documentElement.classList.toggle('hp-hide-native', Boolean(host && layout?.header.hideNativeLinks));

  if (!host || !layout) {
    document.documentElement.classList.remove('hp-replace-search');
    if (navElement) {
      render(null, navElement);
      navElement.remove();
      navElement = null;
    }
    return;
  }

  if (!navElement || navElement.parentElement !== host.container) {
    navElement?.remove();
    navElement = document.createElement('nav');
    navElement.setAttribute('aria-label', 'Navigation');
    host.container.insertBefore(navElement, host.before);
  }
  navElement.className = `hp-nav hp-nav-${host.kind}`;

  replaceSearch = layout.header.replaceSearch;
  document.documentElement.classList.toggle('hp-replace-search', replaceSearch && Boolean(document.querySelector('header.MuiAppBar-root a[href="#/search"], .skinHeader .headerSearchButton')));
  if (!searchHooked) {
    document.addEventListener('click', onDocumentClick, true);
    searchHooked = true;
  }

  render(<Nav tabs={layout.header.tabs} />, navElement);
}
