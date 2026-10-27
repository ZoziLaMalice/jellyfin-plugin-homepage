import { render } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import './styles.css';
import { DetailsModal } from './components/DetailsModal';
import { Toasts } from './components/Toasts';
import { syncHeader } from './header';
import { api, client, currentUserId } from './jellyfin';
import { DiscoverPage, GridPage } from './pages/DiscoverPage';
import { HomePage, clearSectionCache } from './pages/HomePage';
import { RequestsPage } from './pages/RequestsPage';
import { SearchPage } from './pages/SearchPage';
import { SettingsPage } from './pages/SettingsPage';
import { currentRoute, isExcludedRoute, pageHash, type Route } from './route';
import { layoutStore, modalStore, useStore } from './store';
import type { ClientLayout } from './types';

declare const __HP_VERSION__: string;
const VERSION = __HP_VERSION__;

// ----- Page switcher -----------------------------------------------------------------------

function App({ route }: { route: Route }) {
  const layout = useStore(layoutStore);
  if (!layout) return null;

  switch (route.page) {
    case 'settings':
      return <SettingsPage layout={layout} />;
    case 'discover':
      return <DiscoverPage layout={layout} page="Discover" />;
    case 'movies':
      return <DiscoverPage layout={layout} page="Movies" />;
    case 'tv':
      return <DiscoverPage layout={layout} page="Tv" />;
    case 'grid':
      return <GridPage key={route.params.get('source')} source={route.params.get('source') || ''} title={route.params.get('title') || ''} />;
    case 'search':
      return <SearchPage layout={layout} initial={route.params.get('q') || ''} />;
    case 'requests':
      return <RequestsPage />;
    default:
      return <HomePage layout={layout} />;
  }
}

function Overlays() {
  const modal = useStore(modalStore);
  // Close the dialog when navigating away.
  useEffect(() => {
    const onHash = () => modalStore.set(null);
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);
  return (
    <>
      {modal && <DetailsModal />}
      <Toasts />
    </>
  );
}

function Routed() {
  const [route, setRoute] = useState(currentRoute());
  useEffect(() => {
    const update = () => setRoute(currentRoute());
    window.addEventListener('hashchange', update);
    return () => window.removeEventListener('hashchange', update);
  }, []);
  return <App route={route} key={`${route.page}`} />;
}

// ----- Mounting into Jellyfin's home view --------------------------------------------------

const state = {
  userId: null as string | null,
  loading: false,
  root: null as HTMLElement | null,
  overlays: null as HTMLElement | null,
  scheduled: 0,
  lastPage: '',
  observer: null as MutationObserver | null,
};

function createRoot(): HTMLElement {
  // Jellyfin's items container starts playback for children carrying the itemAction attributes.
  const template = document.createElement('template');
  template.innerHTML = '<div is="emby-itemscontainer" class="hp-root itemsContainer" data-contextmenu="false" data-multiselect="false"></div>';
  const root = template.content.firstElementChild as HTMLElement;
  return root;
}

function ensureOverlays() {
  if (state.overlays && state.overlays.isConnected) return;
  state.overlays = document.createElement('div');
  state.overlays.className = 'hp-overlays';
  document.body.appendChild(state.overlays);
  render(<Overlays />, state.overlays);
}

function unmountRoot() {
  if (state.root) {
    render(null, state.root);
    state.root.remove();
    state.root = null;
  }
  document.documentElement.classList.remove('hp-active');
}

async function loadLayout(force = false) {
  const userId = currentUserId();
  if (!userId) return;
  if (!force && state.userId === userId && layoutStore.get()) return;
  if (state.loading) return;
  state.loading = true;
  try {
    const layout = await api<ClientLayout>('Homepage/Layout');
    state.userId = userId;
    clearSectionCache();
    layoutStore.set(layout);
  } catch (error) {
    console.warn('Homepage: layout unavailable', error);
  } finally {
    state.loading = false;
    schedule();
  }
}

function syncPreferencesLink() {
  if (!/^#\/mypreferencesmenu/i.test(window.location.hash)) return;
  const page = document.querySelector('.page:not(.hide) .readOnlyContent, .page:not(.hide) .verticalSection');
  if (!page || document.querySelector('.hp-prefs-link')) return;
  const link = document.createElement('a');
  link.className = 'listItem-border emby-button hp-prefs-link';
  link.href = pageHash('settings');
  link.innerHTML =
    '<div class="listItem"><span class="material-icons listItemIcon listItemIcon-transparent home" aria-hidden="true"></span>' +
    '<div class="listItemBody"><div class="listItemBodyText">Accueil personnalisé (Homepage)</div></div></div>';
  page.insertBefore(link, page.firstChild);
}

function reconcile() {
  state.scheduled = 0;
  const userId = currentUserId();
  if (!userId) {
    syncHeader(null);
    unmountRoot();
    layoutStore.set(null);
    state.userId = null;
    return;
  }

  if (state.userId !== userId || !layoutStore.get()) {
    void loadLayout(true);
  }

  const layout = layoutStore.get();
  syncHeader(layout);
  syncPreferencesLink();
  ensureOverlays();

  const route = currentRoute();
  const homeTab = document.querySelector<HTMLElement>('#indexPage:not(.hide) #homeTab');
  const wanted = Boolean(layout && route.isHome && route.tab === 0 && homeTab && !isExcludedRoute() && (layout.enabled || route.page === 'settings'));

  if (!wanted || !homeTab) {
    unmountRoot();
    return;
  }

  document.documentElement.classList.add('hp-active');
  document.documentElement.dataset.hpPage = route.page;

  if (!state.root || state.root.parentElement !== homeTab) {
    unmountRoot();
    document.documentElement.classList.add('hp-active');
    state.root = createRoot();
    homeTab.insertBefore(state.root, homeTab.firstChild);
    window.CustomElements?.upgradeSubtree?.(state.root);
    render(<Routed />, state.root);
  }

  measureTopOffset();

  const pageKey = `${route.page}|${route.params.get('source') || ''}`;
  if (pageKey !== state.lastPage) {
    if (state.lastPage) window.scrollTo({ top: 0 });
    state.lastPage = pageKey;
  }
}

/** The hero slides under Jellyfin's transparent top bar: measure how far down the home tab starts. */
function measureTopOffset() {
  const host = state.root?.parentElement;
  if (!host) return;
  const top = Math.max(0, Math.round(host.getBoundingClientRect().top + window.scrollY));
  const value = `${top}px`;
  if (document.documentElement.style.getPropertyValue('--hp-top-offset') !== value) {
    document.documentElement.style.setProperty('--hp-top-offset', value);
  }
}

function schedule() {
  if (state.scheduled) return;
  state.scheduled = window.setTimeout(reconcile, 30);
}

function start() {
  if (!client()) {
    window.setTimeout(start, 250);
    return;
  }
  window.HomepagePlugin?.stop();
  window.addEventListener('hashchange', schedule);
  window.addEventListener('popstate', schedule);
  window.addEventListener('resize', measureTopOffset);
  document.addEventListener('viewshow', schedule);
  state.observer = new MutationObserver((records) => {
    // Our own renders (hero slides, rows) do not require a reconcile.
    const ours = records.every((r) => {
      const el = r.target instanceof Element ? r.target : r.target.parentElement;
      return Boolean(el?.closest('.hp-root, .hp-nav, .hp-overlays'));
    });
    if (!ours) schedule();
  });
  state.observer.observe(document.body, { childList: true, subtree: true });
  // Layout changes made in the admin page apply without reload when the user comes back.
  window.addEventListener('focus', () => void loadLayout(true));
  window.HomepagePlugin = {
    version: VERSION,
    stop() {
      state.observer?.disconnect();
      window.removeEventListener('hashchange', schedule);
      window.removeEventListener('popstate', schedule);
      document.removeEventListener('viewshow', schedule);
      syncHeader(null);
      unmountRoot();
      if (state.overlays) {
        render(null, state.overlays);
        state.overlays.remove();
      }
    },
  };
  schedule();
}

start();
