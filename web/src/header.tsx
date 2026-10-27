import { render } from 'preact';
import { Icon } from './components/Icon';
import { serverId } from './jellyfin';
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
      return `#/list?parentId=${encodeURIComponent(tab.target || '')}&serverId=${serverId()}`;
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
      return Boolean(tab.target) && hash.includes(tab.target!);
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
let searchHooked: Element | null = null;
let replaceSearch = false;

function onSearchClick(event: Event) {
  if (!replaceSearch) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  window.location.hash = pageHash('search');
}

/** Adds (or refreshes) our navigation inside Jellyfin's top bar. */
export function syncHeader(layout: ClientLayout | null) {
  const headerTop = document.querySelector('.skinHeader .headerTop');
  const enabled = Boolean(layout?.enabled && layout.header.enabled && layout.header.tabs.length) && !isExcludedRoute();
  document.documentElement.classList.toggle('hp-header', enabled);

  if (!enabled || !headerTop || !layout) {
    if (navElement) {
      render(null, navElement);
      navElement.remove();
      navElement = null;
    }
    return;
  }

  if (!navElement || navElement.parentElement !== headerTop) {
    navElement?.remove();
    navElement = document.createElement('nav');
    navElement.className = 'hp-nav';
    navElement.setAttribute('aria-label', 'Navigation');
    const left = headerTop.querySelector('.headerLeft');
    headerTop.insertBefore(navElement, left ? left.nextSibling : headerTop.firstChild);
  }

  // The Search tab already exists in our bar: keep the header button pointing to the combined search.
  replaceSearch = layout.header.replaceSearch;
  const searchButton = document.querySelector('.skinHeader .headerSearchButton');
  if (searchButton && searchButton !== searchHooked) {
    searchButton.addEventListener('click', onSearchClick, true);
    searchHooked = searchButton;
  }

  render(<Nav tabs={layout.header.tabs} />, navElement);
}
