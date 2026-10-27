import { useEffect, useRef, useState } from 'preact/hooks';
import { Card } from '../components/Card';
import { Row, useNearViewport } from '../components/Row';
import { api } from '../jellyfin';
import { pageHash } from '../route';
import type { ClientLayout, HomeCard, PagedCards } from '../types';

const rowCache = new Map<string, { at: number; items: HomeCard[] }>();

function DiscoverRow({ id, title }: { id: string; title: string }) {
  const cached = rowCache.get(id);
  const [items, setItems] = useState<HomeCard[] | null>(cached && Date.now() - cached.at < 5 * 60000 ? cached.items : null);
  const [error, setError] = useState<string | null>(null);
  const [ref, near] = useNearViewport<HTMLDivElement>();

  useEffect(() => {
    if (!near || items) return;
    let cancelled = false;
    api<PagedCards>(`Homepage/Seerr/Page?source=${encodeURIComponent(id)}&page=1`)
      .then((r) => {
        if (cancelled) return;
        rowCache.set(id, { at: Date.now(), items: r.items });
        setItems(r.items);
      })
      .catch((e: Error) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
    };
  }, [near]);

  if (error) return <p class="hp-error">{title} : {error}</p>;
  return (
    <div ref={ref}>
      <Row title={title} items={items || []} shape="Poster" loading={!items} moreHref={pageHash('grid', { source: id, title })} />
    </div>
  );
}

const TITLES: Record<string, string> = { Discover: 'Découvrir', Movies: 'Films', Tv: 'Séries' };

export function DiscoverPage({ layout, page }: { layout: ClientLayout; page: 'Discover' | 'Movies' | 'Tv' }) {
  const rows = layout.discoverRows.filter((r) => r.page === page);
  return (
    <div class="hp-page hp-page-discover">
      <h1 class="hp-page-title">{TITLES[page]}</h1>
      {!layout.seerrConfigured && <p class="hp-empty">Seerr n'est pas configuré. Un administrateur peut le faire dans la configuration du plugin Homepage.</p>}
      {rows.map((row) => (
        <DiscoverRow key={row.id} id={row.id} title={row.title} />
      ))}
    </div>
  );
}

/** Infinite grid for "see all" of a Seerr source (discover row or Seerr section). */
export function GridPage({ source, title }: { source: string; title: string }) {
  const [items, setItems] = useState<HomeCard[]>([]);
  const [page, setPage] = useState(0);
  const [total, setTotal] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sentinel = useRef<HTMLDivElement>(null);

  const loadMore = () => {
    if (loading || page >= total) return;
    setLoading(true);
    const next = page + 1;
    api<PagedCards>(`Homepage/Seerr/Page?source=${encodeURIComponent(source)}&page=${next}`)
      .then((r) => {
        setItems((current) => {
          const seen = new Set(current.map((c) => c.key));
          return current.concat(r.items.filter((c) => !seen.has(c.key)));
        });
        setPage(next);
        setTotal(r.totalPages || next);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    setItems([]);
    setPage(0);
    setTotal(1);
  }, [source]);

  useEffect(() => {
    const el = sentinel.current;
    if (!el || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver((entries) => entries.some((e) => e.isIntersecting) && loadMore(), { rootMargin: '800px' });
    observer.observe(el);
    return () => observer.disconnect();
  });

  return (
    <div class="hp-page hp-page-grid">
      <h1 class="hp-page-title">
        <a class="hp-back" href="javascript:history.back()" aria-label="Retour">
          <span class="material-icons arrow_back" aria-hidden="true" />
        </a>
        {title}
      </h1>
      <div class="hp-grid">
        {items.map((card) => (
          <Card key={card.key} card={card} shape="Poster" />
        ))}
      </div>
      {error && <p class="hp-error">{error}</p>}
      {loading && <div class="hp-spinner" />}
      {page < total && <div ref={sentinel} class="hp-sentinel" />}
    </div>
  );
}
