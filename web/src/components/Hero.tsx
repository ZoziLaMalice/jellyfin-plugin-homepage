import { useEffect, useRef, useState } from 'preact/hooks';
import { api, imageUrl } from '../jellyfin';
import { metaLine, statusLabel } from '../format';
import { effectiveStatus, openDetails, statusOverrides, useStore } from '../store';
import type { HomeCard } from '../types';
import { openCard, playAttributes } from './Card';
import { Icon } from './Icon';

let heroCache: { at: number; items: HomeCard[] } | null = null;

export function Hero({ interval }: { interval: number }) {
  const [items, setItems] = useState<HomeCard[] | null>(heroCache && Date.now() - heroCache.at < 5 * 60000 ? heroCache.items : null);
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const touchStart = useRef<number | null>(null);
  const overrides = useStore(statusOverrides);

  useEffect(() => {
    if (items) return;
    let cancelled = false;
    api<HomeCard[]>('Homepage/Hero')
      .then((result) => {
        if (cancelled) return;
        heroCache = { at: Date.now(), items: result };
        setItems(result);
      })
      .catch(() => !cancelled && setItems([]));
    return () => {
      cancelled = true;
    };
  }, []);

  const count = items?.length || 0;

  useEffect(() => {
    if (paused || count < 2) return;
    const timer = window.setTimeout(() => setIndex((i) => (i + 1) % count), Math.max(3, interval) * 1000);
    return () => window.clearTimeout(timer);
  }, [index, paused, count, interval]);

  // Preload the next backdrop so the crossfade never shows an empty frame.
  useEffect(() => {
    const next = items?.[(index + 1) % Math.max(count, 1)];
    if (next?.backdrop) new Image().src = imageUrl(next.backdrop, 1920) || '';
  }, [index, count]);

  if (items === null) return <div class="hp-hero hp-hero-loading" />;
  if (count === 0) return null;

  const go = (delta: number) => setIndex((i) => (i + delta + count) % count);

  return (
    <section
      class="hp-hero"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onTouchStart={(e) => (touchStart.current = e.touches[0]?.clientX ?? null)}
      onTouchEnd={(e) => {
        const start = touchStart.current;
        const end = e.changedTouches[0]?.clientX;
        touchStart.current = null;
        if (start != null && end != null && Math.abs(end - start) > 50) go(end < start ? 1 : -1);
      }}
    >
      {items.map((card, i) => {
        const active = i === index;
        const status = effectiveStatus(card, overrides);
        const meta = metaLine(card);
        if (card.rating) meta.unshift(`★ ${card.rating.toFixed(1)}`);
        return (
          <div key={card.key} class={`hp-hero-slide ${active ? 'is-active' : ''}`} aria-hidden={!active}>
            {(active || Math.abs(i - index) === 1 || (index === count - 1 && i === 0)) && (
              <img class="hp-hero-backdrop" src={imageUrl(card.backdrop, 1920)} alt="" />
            )}
            <div class="hp-hero-gradient" />
            <div class="hp-hero-content">
              {card.kind === 'request' && (
                <span class="hp-hero-tag">
                  <Icon name="explore" /> {statusLabel(status) || 'À découvrir'}
                </span>
              )}
              {card.logo ? <img class="hp-hero-logo" src={imageUrl(card.logo, 600)} alt={card.title} /> : <h1 class="hp-hero-title">{card.title}</h1>}
              <div class="hp-hero-meta">
                {meta.map((m) => (
                  <span key={m}>{m}</span>
                ))}
                {(card.genres || []).slice(0, 3).map((g) => (
                  <span key={g} class="hp-hero-genre">
                    {g}
                  </span>
                ))}
              </div>
              {card.overview && <p class="hp-hero-overview">{card.overview}</p>}
              <div class="hp-hero-actions">
                {card.kind === 'local' ? (
                  <button type="button" class="itemAction hp-button hp-button-primary" {...playAttributes(card)} tabIndex={active ? 0 : -1}>
                    <Icon name="play_arrow" /> {card.positionTicks ? 'Reprendre' : 'Lecture'}
                  </button>
                ) : (
                  <button type="button" class="hp-button hp-button-request" onClick={() => openDetails(card, true)} tabIndex={active ? 0 : -1} disabled={status === 2 || status === 3}>
                    <Icon name={status === 2 || status === 3 ? 'schedule' : 'add'} /> {status === 2 || status === 3 ? statusLabel(status) : 'Demander'}
                  </button>
                )}
                <button type="button" class="hp-button hp-button-secondary" onClick={() => openCard(card)} tabIndex={active ? 0 : -1}>
                  <Icon name="info_outline" /> Plus d'infos
                </button>
              </div>
            </div>
          </div>
        );
      })}
      {count > 1 && (
        <>
          <button type="button" class="hp-hero-arrow hp-hero-arrow-left" onClick={() => go(-1)} aria-label="Précédent">
            <Icon name="chevron_left" />
          </button>
          <button type="button" class="hp-hero-arrow hp-hero-arrow-right" onClick={() => go(1)} aria-label="Suivant">
            <Icon name="chevron_right" />
          </button>
          <div class="hp-hero-dots">
            {items.map((card, i) => (
              <button key={card.key} type="button" class={i === index ? 'is-active' : ''} onClick={() => setIndex(i)} aria-label={card.title}>
                {i === index && !paused && <span style={{ animationDuration: `${Math.max(3, interval)}s` }} />}
              </button>
            ))}
          </div>
        </>
      )}
    </section>
  );
}
