import { useEffect, useState } from 'preact/hooks';
import { Hero } from '../components/Hero';
import { Icon } from '../components/Icon';
import { Row, useNearViewport } from '../components/Row';
import { api } from '../jellyfin';
import { pageHash } from '../route';
import type { ClientLayout, ClientSection, SectionResult } from '../types';

/** Section results are kept briefly so going back to the home page is instant. */
const sectionCache = new Map<string, { at: number; result: SectionResult }>();
const SECTION_TTL = 3 * 60000;

export function clearSectionCache() {
  sectionCache.clear();
}

function Section({ section }: { section: ClientSection }) {
  const cached = sectionCache.get(section.id);
  const [result, setResult] = useState<SectionResult | null>(cached && Date.now() - cached.at < SECTION_TTL ? cached.result : null);
  const [ref, near] = useNearViewport<HTMLDivElement>();

  useEffect(() => {
    if (!near || result) return;
    let cancelled = false;
    api<SectionResult>(`Homepage/Sections/${section.id}`)
      .then((r) => {
        if (cancelled) return;
        sectionCache.set(section.id, { at: Date.now(), result: r });
        setResult(r);
      })
      .catch((e: Error) => !cancelled && setResult({ rows: [], error: e.message }));
    return () => {
      cancelled = true;
    };
  }, [near]);

  if (!result) {
    return (
      <div ref={ref} class="hp-section-placeholder">
        <Row title={section.title.replace('{genre}', '…')} items={[]} shape={section.display} loading />
      </div>
    );
  }

  return (
    <div class={`hp-section hp-section-${section.type}`}>
      {result.rows.map((row, i) => (
        <Row
          key={`${section.id}-${i}`}
          title={row.title}
          items={row.items}
          shape={row.display}
          moreHref={row.moreLink?.startsWith('seerr:') ? pageHash('grid', { source: row.moreLink.slice(6), title: row.title }) : undefined}
        />
      ))}
    </div>
  );
}

export function HomePage({ layout }: { layout: ClientLayout }) {
  const sections = layout.sections.filter((s) => s.enabled);
  return (
    <div class="hp-page hp-page-home">
      {layout.hero.enabled && <Hero interval={layout.hero.intervalSeconds} />}
      <div class={`hp-sections ${layout.hero.enabled ? 'hp-sections-overlap' : ''}`}>
        {sections.map((section) => (
          <Section key={`${section.id}-${layout.generation}`} section={section} />
        ))}
        {sections.length === 0 && <p class="hp-empty">Aucune section n'est affichée. Personnalise ton accueil pour en ajouter.</p>}
      </div>
      <div class="hp-home-footer">
        <a class="hp-button hp-button-ghost" href={pageHash('settings')}>
          <Icon name="tune" /> Personnaliser l'accueil
        </a>
      </div>
    </div>
  );
}
