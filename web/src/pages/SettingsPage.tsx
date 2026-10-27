import { useEffect, useState } from 'preact/hooks';
import { Icon } from '../components/Icon';
import { api } from '../jellyfin';
import { pageHash } from '../route';
import { layoutStore, toast } from '../store';
import type { ClientLayout, ClientSection, UserSettings } from '../types';
import { clearSectionCache } from './HomePage';

const TYPE_LABELS: Record<string, string> = {
  ContinueWatching: 'Reprise',
  Library: 'Médiathèque',
  Genres: 'Genres',
  SeerrDiscover: 'Seerr',
  RecentDigital: 'Seerr',
  Upcoming: 'Sonarr',
  MyRequests: 'Seerr',
};

function Toggle({ checked, onChange, disabled, label }: { checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; label: string }) {
  return (
    <label class={`hp-toggle ${disabled ? 'is-disabled' : ''}`}>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange((e.target as HTMLInputElement).checked)} />
      <span class="hp-toggle-track">
        <span class="hp-toggle-thumb" />
      </span>
      <span class="hp-toggle-label">{label}</span>
    </label>
  );
}

export function SettingsPage({ layout }: { layout: ClientLayout }) {
  const [settings, setSettings] = useState<UserSettings | null>(null);
  const [sections, setSections] = useState<ClientSection[]>(layout.sections);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api<UserSettings>('Homepage/UserSettings').then(setSettings).catch(() => setSettings({ sections: [] }));
  }, []);

  useEffect(() => setSections(layout.sections), [layout.generation]);

  const save = async (next: UserSettings, nextSections = sections) => {
    setSaving(true);
    try {
      const updated = await api<ClientLayout>('Homepage/UserSettings', {
        method: 'POST',
        body: { ...next, sections: nextSections.map((s) => ({ id: s.id, enabled: s.enabled })) },
      });
      setSettings(next);
      clearSectionCache();
      layoutStore.set(updated);
    } catch (e) {
      toast(`Enregistrement impossible : ${(e as Error).message}`, 'error');
    } finally {
      setSaving(false);
    }
  };

  const move = (index: number, delta: number) => {
    const next = sections.slice();
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item);
    setSections(next);
    if (settings) save(settings, next);
  };

  const toggleSection = (index: number, enabled: boolean) => {
    const next = sections.map((s, i) => (i === index ? { ...s, enabled } : s));
    setSections(next);
    if (settings) save(settings, next);
  };

  if (!settings) return <div class="hp-page"><div class="hp-spinner" /></div>;

  const enabled = layout.enabled;

  return (
    <div class="hp-page hp-page-settings">
      <h1 class="hp-page-title">Mon accueil</h1>

      <div class="hp-panel">
        <Toggle
          label="Utiliser l'accueil personnalisé Homepage"
          checked={enabled}
          disabled={!layout.canToggle || saving}
          onChange={(v) => save({ ...settings, enabled: v })}
        />
        {!layout.canToggle && <p class="hp-hint">Ce choix est fixé par l'administrateur.</p>}
        {enabled && (
          <>
            <Toggle label="Afficher le carrousel en haut de l'accueil" checked={layout.hero.enabled} disabled={saving} onChange={(v) => save({ ...settings, heroEnabled: v })} />
            <Toggle label="Afficher la barre de navigation (Découvrir, Films, Séries…)" checked={layout.header.enabled} disabled={saving} onChange={(v) => save({ ...settings, headerEnabled: v })} />
          </>
        )}
      </div>

      {enabled && layout.canCustomize && (
        <>
          <h2 class="hp-results-title">Sections</h2>
          <div class="hp-panel hp-section-list">
            {sections.map((s, i) => (
              <div key={s.id} class={`hp-section-item ${s.enabled ? '' : 'is-off'}`}>
                <Toggle label={s.title === '{genre}' ? 'Genres' : s.title} checked={s.enabled} disabled={!s.userCanHide || saving} onChange={(v) => toggleSection(i, v)} />
                <span class="hp-chip">{TYPE_LABELS[s.type] || s.type}</span>
                <div class="hp-section-move">
                  <button type="button" class="hp-icon-button" disabled={i === 0 || saving} onClick={() => move(i, -1)} aria-label="Monter">
                    <Icon name="arrow_upward" />
                  </button>
                  <button type="button" class="hp-icon-button" disabled={i === sections.length - 1 || saving} onClick={() => move(i, 1)} aria-label="Descendre">
                    <Icon name="arrow_downward" />
                  </button>
                </div>
              </div>
            ))}
          </div>
          <button
            type="button"
            class="hp-button hp-button-ghost"
            disabled={saving}
            onClick={() => save({ ...settings, sections: [] }, []).then(() => setSections(layoutStore.get()?.sections || []))}
          >
            <Icon name="restart_alt" /> Revenir à l'ordre par défaut
          </button>
        </>
      )}

      <div class="hp-settings-footer">
        <a class="hp-button hp-button-secondary" href={pageHash('home')}>
          <Icon name="home" /> Retour à l'accueil
        </a>
        {layout.isAdmin && (
          <a class="hp-button hp-button-ghost" href="#/configurationpage?name=Homepage">
            <Icon name="admin_panel_settings" /> Configuration administrateur
          </a>
        )}
      </div>
    </div>
  );
}
