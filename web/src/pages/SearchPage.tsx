import { useEffect, useRef, useState } from 'preact/hooks';
import { Card } from '../components/Card';
import { Icon } from '../components/Icon';
import { api } from '../jellyfin';
import type { ClientLayout, SearchResult } from '../types';

export function SearchPage({ layout, initial }: { layout: ClientLayout; initial: string }) {
  const [term, setTerm] = useState(initial);
  const [result, setResult] = useState<SearchResult | null>(null);
  const [loading, setLoading] = useState(false);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    input.current?.focus();
  }, []);

  useEffect(() => {
    const q = term.trim();
    // Keep the term in the address so back/forward restores it, without adding history entries.
    const url = new URL(window.location.href);
    const hashParams = new URLSearchParams(url.hash.split('?')[1] || '');
    if (q) hashParams.set('q', q);
    else hashParams.delete('q');
    hashParams.set('hp', 'search');
    window.history.replaceState(window.history.state, '', `#/home?${hashParams.toString()}`);

    if (q.length < 2) {
      setResult(null);
      return;
    }
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true);
      api<SearchResult>(`Homepage/Search?q=${encodeURIComponent(q)}`, { signal: controller.signal })
        .then(setResult)
        .catch(() => undefined)
        .finally(() => setLoading(false));
    }, 300);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [term]);

  return (
    <div class="hp-page hp-page-search">
      <div class="hp-search-box">
        <Icon name="search" />
        <input
          ref={input}
          type="search"
          value={term}
          placeholder={layout.seerrConfigured ? 'Rechercher un film, une série… (médiathèque et Seerr)' : 'Rechercher un film, une série…'}
          onInput={(e) => setTerm((e.target as HTMLInputElement).value)}
          autocomplete="off"
          enterkeyhint="search"
        />
        {loading && <div class="hp-spinner hp-spinner-small" />}
      </div>

      {result && (
        <>
          <h2 class="hp-results-title">
            <span class="hp-badge hp-badge-local hp-badge-inline">
              <Icon name="check" />
            </span>
            Dans ta médiathèque
          </h2>
          {result.library.length ? (
            <div class="hp-grid">
              {result.library.map((card) => (
                <Card key={card.key} card={card} shape="Poster" />
              ))}
            </div>
          ) : (
            <p class="hp-empty">Aucun résultat.</p>
          )}

          {layout.seerrConfigured && (
            <>
              <h2 class="hp-results-title">
                <span class="hp-badge hp-badge-seerr hp-badge-inline">
                  <Icon name="add" />
                </span>
                À demander sur Seerr
              </h2>
              {result.seerrError && <p class="hp-error">{result.seerrError}</p>}
              {result.seerr.length ? (
                <div class="hp-grid">
                  {result.seerr.map((card) => (
                    <Card key={card.key} card={card} shape="Poster" />
                  ))}
                </div>
              ) : (
                !result.seerrError && <p class="hp-empty">Aucun résultat.</p>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
