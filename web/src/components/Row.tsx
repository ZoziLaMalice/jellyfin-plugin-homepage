import type { RefObject } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { HomeCard } from '../types';
import { Card, type CardShape } from './Card';
import { Icon } from './Icon';

export function Row({ title, items, shape, moreHref, loading }: { title: string; items: HomeCard[]; shape: CardShape; moreHref?: string; loading?: boolean }) {
  const scroller = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ start: true, end: false });

  const updateEdges = () => {
    const el = scroller.current;
    if (!el) return;
    setEdges({ start: el.scrollLeft <= 4, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 4 });
  };

  useEffect(() => {
    updateEdges();
    const el = scroller.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(updateEdges);
    observer.observe(el);
    return () => observer.disconnect();
  }, [items.length]);

  const scrollBy = (direction: number) => {
    const el = scroller.current;
    if (!el) return;
    el.scrollBy({ left: direction * Math.max(el.clientWidth * 0.85, 300), behavior: 'smooth' });
  };

  if (!loading && items.length === 0) return null;

  return (
    <section class="hp-row">
      <div class="hp-row-header">
        <h2 class="hp-row-title">{title}</h2>
        {moreHref && (
          <a class="hp-row-more" href={moreHref}>
            Tout voir <Icon name="chevron_right" />
          </a>
        )}
      </div>
      <div class="hp-row-body">
        {!edges.start && (
          <button type="button" class="hp-row-arrow hp-row-arrow-left" onClick={() => scrollBy(-1)} aria-label="Précédent">
            <Icon name="chevron_left" />
          </button>
        )}
        <div class={`hp-row-scroller hp-row-${shape === 'Landscape' ? 'landscape' : 'poster'}`} ref={scroller} onScroll={updateEdges}>
          {loading && items.length === 0
            ? Array.from({ length: 8 }, (_, i) => <div key={i} class={`hp-card hp-card-${shape === 'Landscape' ? 'landscape' : 'poster'} hp-skeleton`}><div class="hp-card-image" /></div>)
            : items.map((card) => <Card key={card.key} card={card} shape={shape} />)}
        </div>
        {!edges.end && items.length > 0 && (
          <button type="button" class="hp-row-arrow hp-row-arrow-right" onClick={() => scrollBy(1)} aria-label="Suivant">
            <Icon name="chevron_right" />
          </button>
        )}
      </div>
    </section>
  );
}

/** Renders children only once the placeholder approaches the viewport. */
export function useNearViewport<T extends Element>(margin = '600px'): [RefObject<T>, boolean] {
  const ref = useRef<T>(null);
  const [visible, setVisible] = useState(false);
  useEffect(() => {
    if (visible || !ref.current) return;
    if (typeof IntersectionObserver === 'undefined') {
      setVisible(true);
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setVisible(true);
          observer.disconnect();
        }
      },
      { rootMargin: margin },
    );
    observer.observe(ref.current);
    return () => observer.disconnect();
  }, [visible]);
  return [ref, visible];
}
