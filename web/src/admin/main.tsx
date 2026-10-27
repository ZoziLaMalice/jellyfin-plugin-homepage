import { render } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import './admin.css';
import { api } from '../jellyfin';
import { Check, Field, IconButton, NumberInput, Select, Text, move, newId } from './fields';
import type { DiscoverRowConfig, HeaderTab, JellyfinUser, LibraryInfo, PluginConfig, SectionConfig } from './types';

const PLUGIN_ID = 'acdcaecb-228d-4e50-acfb-479d2f8a90a6';

const SECTION_TYPES: [string, string][] = [
  ['ContinueWatching', 'Continuer à regarder / À suivre'],
  ['Library', 'Bibliothèques (local + reco)'],
  ['Genres', 'Une ligne par genre'],
  ['RecentDigital', 'Sorties digitales récentes (Seerr)'],
  ['SeerrDiscover', 'Filtre Seerr personnalisé'],
  ['Upcoming', 'Prochains épisodes (Sonarr)'],
  ['MyRequests', 'Mes demandes (Seerr)'],
];

const TAB_TYPES: [string, string][] = [
  ['Home', 'Accueil'],
  ['Discover', 'Découvrir (Seerr)'],
  ['DiscoverMovies', 'Découvrir les films'],
  ['DiscoverTv', 'Découvrir les séries'],
  ['Search', 'Recherche (médiathèque + Seerr)'],
  ['Favorites', 'Favoris'],
  ['Requests', 'Mes demandes'],
  ['Library', 'Bibliothèque Jellyfin'],
  ['Link', 'Lien personnalisé'],
];

const SORTS: [string, string][] = [
  ['DateCreated', 'Ajout le plus récent'],
  ['DateLastContentAdded', 'Dernier contenu ajouté (nouveaux épisodes)'],
  ['Random', 'Aléatoire'],
  ['CommunityRating', 'Mieux notés'],
  ['PremiereDate', 'Date de sortie'],
  ['Name', 'Nom'],
];

const TABS = ['Général', 'Seerr', 'Sonarr', 'Navigation', 'Hero', 'Sections', 'Découvrir'] as const;
type TabName = (typeof TABS)[number];

interface JfClient {
  getPluginConfiguration(id: string): Promise<PluginConfig>;
  updatePluginConfiguration(id: string, config: PluginConfig): Promise<unknown>;
}

function jf(): JfClient {
  return window.ApiClient as unknown as JfClient;
}

function LibraryPicker({ libraries, value, onChange, kinds }: { libraries: LibraryInfo[]; value: string[]; onChange: (v: string[]) => void; kinds?: string }) {
  const wanted = (kinds || 'Movie,Series').split(',');
  const list = libraries.filter((l) => (l.type === 'movies' && wanted.includes('Movie')) || (l.type === 'tvshows' && wanted.includes('Series')));
  const norm = (id: string) => id.replace(/-/g, '').toLowerCase();
  return (
    <div class="hpa-libraries">
      {list.map((lib) => (
        <label key={lib.id} class="hpa-check hpa-library">
          <input
            type="checkbox"
            checked={value.map(norm).includes(norm(lib.id))}
            onChange={(e) => {
              const on = (e.target as HTMLInputElement).checked;
              onChange(on ? [...value, norm(lib.id)] : value.filter((v) => norm(v) !== norm(lib.id)));
            }}
          />
          <span>
            {lib.name} <span class="hpa-tag">{lib.type === 'movies' ? 'Films' : 'Séries'}</span>
            {lib.isVirtual && <span class="hpa-tag hpa-tag-virtual">Reco / à demander</span>}
          </span>
        </label>
      ))}
      {value.length === 0 && <span class="hpa-hint">Aucune cochée : toutes les bibliothèques correspondantes sont utilisées.</span>}
    </div>
  );
}

function SeerrImport({ onPick }: { onPick: (slider: { title: string; seerrPath: string; seerrQuery: string }) => void }) {
  const [sliders, setSliders] = useState<{ title: string; enabled: boolean; seerrPath: string; seerrQuery: string }[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);

  const load = () => {
    setOpen(!open);
    if (sliders || open) return;
    api<typeof sliders>('Homepage/Admin/SeerrSliders')
      .then(setSliders)
      .catch((e: Error) => setError(e.message));
  };

  return (
    <div class="hpa-import">
      <button type="button" class="hpa-button hpa-button-ghost" onClick={load}>
        <span class="material-icons download" aria-hidden="true" /> Importer un slider de Seerr
      </button>
      {open && (
        <div class="hpa-import-list">
          {error && <p class="hpa-error">{error} (enregistre d'abord la configuration Seerr)</p>}
          {!sliders && !error && <p class="hpa-hint">Chargement…</p>}
          {sliders?.map((s) => (
            <button key={s.title + s.seerrPath + s.seerrQuery} type="button" class="hpa-import-item" onClick={() => onPick(s)}>
              <strong>{s.title}</strong>
              <code>
                {s.seerrPath}
                {s.seerrQuery ? `?${s.seerrQuery}` : ''}
              </code>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function SectionEditor({ section, libraries, onChange }: { section: SectionConfig; libraries: LibraryInfo[]; onChange: (s: SectionConfig) => void }) {
  const set = <K extends keyof SectionConfig>(key: K, value: SectionConfig[K]) => onChange({ ...section, [key]: value });
  const isLibrary = section.Type === 'Library' || section.Type === 'Genres';
  const isSeerr = section.Type === 'SeerrDiscover' || section.Type === 'RecentDigital';

  return (
    <div class="hpa-editor">
      <div class="hpa-grid">
        <Field label="Titre" hint={section.Type === 'Genres' ? '{genre} est remplacé par le nom du genre.' : undefined}>
          <Text value={section.Title} onChange={(v) => set('Title', v)} />
        </Field>
        <Field label="Type">
          <Select value={section.Type} onChange={(v) => set('Type', v)} options={SECTION_TYPES} />
        </Field>
        <Field label="Format des cartes">
          <Select value={section.Display} onChange={(v) => set('Display', v)} options={[['Poster', 'Affiche (2:3)'], ['Landscape', 'Paysage (16:9)']]} />
        </Field>
        <Field label="Nombre d'éléments max">
          <NumberInput value={section.Limit} min={4} max={60} onChange={(v) => set('Limit', v)} />
        </Field>
      </div>

      {isLibrary && (
        <>
          <div class="hpa-grid">
            <Field label="Contenu">
              <Select value={section.ItemTypes} onChange={(v) => set('ItemTypes', v)} options={[['Movie,Series', 'Films et séries'], ['Movie', 'Films'], ['Series', 'Séries']]} />
            </Field>
            {section.Type === 'Library' && (
              <Field label="Tri">
                <Select value={section.Sort} onChange={(v) => set('Sort', v)} options={SORTS} />
              </Field>
            )}
            {section.Type === 'Genres' && (
              <>
                <Field label="Nombre de lignes">
                  <NumberInput value={section.GenreRows} min={1} max={10} onChange={(v) => set('GenreRows', v)} />
                </Field>
                <Field label="Genres imposés" hint="Séparés par des virgules. Vide : genres les plus présents, changés chaque jour.">
                  <Text value={section.Genres} onChange={(v) => set('Genres', v)} placeholder="Action, Comédie" />
                </Field>
              </>
            )}
          </div>
          <Field label="Bibliothèques">
            <LibraryPicker libraries={libraries} value={section.LibraryIds} kinds={section.ItemTypes} onChange={(v) => set('LibraryIds', v)} />
          </Field>
          <div class="hpa-checks">
            {section.Type === 'Library' && <Check label="Alterner possédés et à demander" hint="Sinon, tout est trié ensemble." checked={section.Interleave} onChange={(v) => set('Interleave', v)} />}
            <Check label="Masquer ce qui est déjà vu" checked={section.HideWatched} onChange={(v) => set('HideWatched', v)} />
          </div>
        </>
      )}

      {section.Type === 'ContinueWatching' && <Check label="Inclure « À suivre » (prochain épisode des séries en cours)" checked={section.IncludeNextUp} onChange={(v) => set('IncludeNextUp', v)} />}

      {section.Type === 'SeerrDiscover' && (
        <>
          <div class="hpa-grid">
            <Field label="Chemin de l'API Seerr" hint="Sous /api/v1, ex. discover/movies, discover/tv, discover/trending.">
              <Text value={section.SeerrPath} onChange={(v) => set('SeerrPath', v)} placeholder="discover/movies" />
            </Field>
            <Field label="Paramètres" hint="Ex. genre=18&sortBy=popularity.desc, keywords=…, studio=…">
              <Text value={section.SeerrQuery} onChange={(v) => set('SeerrQuery', v)} placeholder="genre=878" />
            </Field>
          </div>
          <SeerrImport onPick={(s) => onChange({ ...section, Title: section.Title || s.title, SeerrPath: s.seerrPath, SeerrQuery: s.seerrQuery })} />
        </>
      )}

      {section.Type === 'RecentDigital' && (
        <Field label="Fenêtre (jours)" hint="Films sortis dans cette période et déjà proposés par un service de streaming/VOD dans ta région.">
          <NumberInput value={section.DaysBack} min={7} max={365} onChange={(v) => set('DaysBack', v)} />
        </Field>
      )}

      {isSeerr && <Check label="Masquer ce qui est déjà disponible" checked={section.HideAvailable} onChange={(v) => set('HideAvailable', v)} />}

      {section.Type === 'Upcoming' && (
        <div class="hpa-grid">
          <Field label="Jours à venir">
            <NumberInput value={section.DaysAhead} min={1} max={120} onChange={(v) => set('DaysAhead', v)} />
          </Field>
          <Check label="Seulement les séries que l'utilisateur a commencées" hint="Sinon, toutes les séries présentes dans Jellyfin." checked={section.OnlyWatched} onChange={(v) => set('OnlyWatched', v)} />
        </div>
      )}

      <Check label="Les utilisateurs peuvent masquer cette section" checked={section.UserCanHide} onChange={(v) => set('UserCanHide', v)} />
    </div>
  );
}

function newSection(type: string): SectionConfig {
  return {
    Id: newId(),
    Type: type,
    Title: SECTION_TYPES.find(([t]) => t === type)?.[1] || 'Nouvelle section',
    Enabled: true,
    UserCanHide: true,
    Display: type === 'ContinueWatching' || type === 'Upcoming' ? 'Landscape' : 'Poster',
    Limit: 24,
    LibraryIds: [],
    ItemTypes: 'Movie,Series',
    Sort: 'DateCreated',
    Interleave: true,
    HideWatched: false,
    IncludeNextUp: true,
    Genres: '',
    GenreRows: 3,
    SeerrPath: type === 'SeerrDiscover' ? 'discover/movies' : '',
    SeerrQuery: '',
    HideAvailable: false,
    DaysBack: 90,
    DaysAhead: 30,
    OnlyWatched: false,
  };
}

function TestButton({ service }: { service: 'seerr' | 'sonarr' }) {
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);
  return (
    <div class="hpa-test">
      <button
        type="button"
        class="hpa-button hpa-button-ghost"
        onClick={() => {
          setResult({ ok: true, message: 'Test…' });
          api<{ ok: boolean; message: string }>(`Homepage/Admin/Test?service=${service}`)
            .then(setResult)
            .catch((e: Error) => setResult({ ok: false, message: e.message }));
        }}
      >
        <span class="material-icons network_check" aria-hidden="true" /> Tester la connexion
      </button>
      {result && <span class={result.ok ? 'hpa-ok' : 'hpa-error'}>{result.message}</span>}
      <span class="hpa-hint">Le test utilise la configuration enregistrée.</span>
    </div>
  );
}

function Admin() {
  const [config, setConfig] = useState<PluginConfig | null>(null);
  const [libraries, setLibraries] = useState<LibraryInfo[]>([]);
  const [users, setUsers] = useState<JellyfinUser[]>([]);
  const [seerrUsers, setSeerrUsers] = useState<{ id: number; name: string }[] | null>(null);
  const [tab, setTab] = useState<TabName>('Général');
  const [openSection, setOpenSection] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const reload = () => {
    jf().getPluginConfiguration(PLUGIN_ID).then((c) => {
      setConfig(c);
      setDirty(false);
    });
  };

  useEffect(() => {
    reload();
    api<LibraryInfo[]>('Homepage/Admin/Libraries').then(setLibraries).catch(() => undefined);
    api<JellyfinUser[]>('Users').then(setUsers).catch(() => undefined);
  }, []);

  useEffect(() => {
    if (tab === 'Seerr' && !seerrUsers && config?.SeerrUrl && config.SeerrApiKey) {
      api<{ id: number; name: string }[]>('Homepage/Admin/SeerrUsers').then(setSeerrUsers).catch(() => setSeerrUsers([]));
    }
  }, [tab, config?.SeerrUrl]);

  if (!config) return <div class="hpa-loading">Chargement…</div>;

  const update = (next: Partial<PluginConfig>) => {
    setConfig({ ...config, ...next });
    setDirty(true);
    setMessage(null);
  };

  const save = async () => {
    setSaving(true);
    try {
      await jf().updatePluginConfiguration(PLUGIN_ID, config);
      setDirty(false);
      setMessage('Configuration enregistrée. Les utilisateurs verront les changements au prochain chargement de l’accueil.');
      reload();
      setSeerrUsers(null);
    } catch (e) {
      setMessage(`Erreur : ${(e as Error).message || e}`);
    } finally {
      setSaving(false);
    }
  };

  const setSections = (Sections: SectionConfig[]) => update({ Sections });
  const setTabs = (Tabs: HeaderTab[]) => update({ Header: { ...config.Header, Tabs } });
  const setRows = (DiscoverRows: DiscoverRowConfig[]) => update({ DiscoverRows });
  const userName = (id: string) => users.find((u) => u.Id.replace(/-/g, '') === id.replace(/-/g, ''))?.Name || id;

  return (
    <div class="hpa">
      <div class="hpa-tabs" role="tablist">
        {TABS.map((t) => (
          <button key={t} type="button" role="tab" class={`hpa-tab ${tab === t ? 'is-active' : ''}`} onClick={() => setTab(t)}>
            {t}
          </button>
        ))}
      </div>

      {tab === 'Général' && (
        <div class="hpa-panel">
          <Check label="Activer l'accueil Homepage par défaut" hint="Pour les utilisateurs qui n'ont pas fait de choix." checked={config.EnabledByDefault} onChange={(v) => update({ EnabledByDefault: v })} />
          <Check label="Les utilisateurs peuvent l'activer/désactiver pour eux" checked={config.AllowUserToggle} onChange={(v) => update({ AllowUserToggle: v })} />
          <Check label="Les utilisateurs peuvent masquer et réordonner les sections" checked={config.AllowUserSections} onChange={(v) => update({ AllowUserSections: v })} />
          <Field label="Désactivé pour ces utilisateurs">
            <div class="hpa-libraries">
              {users.map((u) => {
                const id = u.Id.replace(/-/g, '');
                const off = config.DisabledUserIds.map((x) => x.replace(/-/g, '')).includes(id);
                return (
                  <label key={u.Id} class="hpa-check hpa-library">
                    <input type="checkbox" checked={off} onChange={(e) => update({ DisabledUserIds: (e.target as HTMLInputElement).checked ? [...config.DisabledUserIds, id] : config.DisabledUserIds.filter((x) => x.replace(/-/g, '') !== id) })} />
                    <span>{u.Name}</span>
                  </label>
                );
              })}
            </div>
          </Field>
          <Field label="Bibliothèques « à demander »" hint="Fragments de chemin (un par ligne) : les éléments de ces bibliothèques (JellyNext…) sont proposés en demande Seerr au lieu d'être lus.">
            <textarea class="hpa-input" rows={2} value={config.VirtualPathMarkers} onInput={(e) => update({ VirtualPathMarkers: (e.target as HTMLTextAreaElement).value })} />
          </Field>
          <details class="hpa-advanced">
            <summary>Avancé</summary>
            <Field label="URL des fichiers client (développement)" hint="Vide = fichiers intégrés au plugin. Nécessite un redémarrage.">
              <Text value={config.AssetBaseUrl} onChange={(v) => update({ AssetBaseUrl: v })} placeholder="https://cdn.jsdelivr.net/gh/…/dist" />
            </Field>
            <button
              type="button"
              class="hpa-button hpa-button-danger"
              onClick={() => {
                if (!window.confirm('Recréer les sections, onglets et lignes Découvrir par défaut ? Les sections actuelles seront remplacées.')) return;
                api('Homepage/Admin/ResetSections', { method: 'POST', body: {} }).then(reload);
              }}
            >
              Réinitialiser la mise en page par défaut
            </button>
          </details>
        </div>
      )}

      {tab === 'Seerr' && (
        <div class="hpa-panel">
          <div class="hpa-grid">
            <Field label="URL de Seerr (vue depuis le serveur Jellyfin)">
              <Text value={config.SeerrUrl} onChange={(v) => update({ SeerrUrl: v })} placeholder="http://192.168.1.10:5055" />
            </Field>
            <Field label="URL publique (liens « ouvrir dans Seerr »)" hint="Vide = même URL.">
              <Text value={config.SeerrPublicUrl} onChange={(v) => update({ SeerrPublicUrl: v })} placeholder="https://seerr.exemple.fr" />
            </Field>
            <Field label="Clé API" hint="Paramètres Seerr → Général. Elle reste côté serveur.">
              <Text type="password" value={config.SeerrApiKey} onChange={(v) => update({ SeerrApiKey: v })} />
            </Field>
            <Field label="Région (disponibilité streaming)">
              <Text value={config.Region} onChange={(v) => update({ Region: v.toUpperCase() })} placeholder="FR" />
            </Field>
            <Field label="Langue des titres et résumés">
              <Text value={config.Language} onChange={(v) => update({ Language: v })} placeholder="fr" />
            </Field>
          </div>
          <Check label="Demander une confirmation avant d'envoyer une demande" checked={config.ConfirmRequests} onChange={(v) => update({ ConfirmRequests: v })} />
          <TestButton service="seerr" />

          <h3 class="hpa-subtitle">Correspondance des utilisateurs</h3>
          <p class="hpa-hint">
            Par défaut, chaque utilisateur Jellyfin est relié au compte Seerr importé depuis Jellyfin (ou portant le même nom). Ajoute une ligne pour forcer un autre compte.
          </p>
          {config.UserMappings.map((m, i) => (
            <div key={i} class="hpa-row">
              <Select
                value={m.JellyfinUserId}
                onChange={(v) => update({ UserMappings: config.UserMappings.map((x, j) => (j === i ? { ...x, JellyfinUserId: v } : x)) })}
                options={[['', 'Utilisateur Jellyfin…'], ...users.map((u) => [u.Id.replace(/-/g, ''), u.Name] as [string, string])]}
              />
              <span class="material-icons arrow_forward" aria-hidden="true" />
              {seerrUsers && seerrUsers.length ? (
                <Select
                  value={String(m.SeerrUserId || '')}
                  onChange={(v) => update({ UserMappings: config.UserMappings.map((x, j) => (j === i ? { ...x, SeerrUserId: Number(v) } : x)) })}
                  options={[['', 'Compte Seerr…'], ...seerrUsers.map((u) => [String(u.id), `${u.name} (#${u.id})`] as [string, string])]}
                />
              ) : (
                <NumberInput value={m.SeerrUserId} min={0} onChange={(v) => update({ UserMappings: config.UserMappings.map((x, j) => (j === i ? { ...x, SeerrUserId: v } : x)) })} />
              )}
              <IconButton icon="delete" title="Supprimer" onClick={() => update({ UserMappings: config.UserMappings.filter((_, j) => j !== i) })} />
              {m.JellyfinUserId && <span class="hpa-hint">{userName(m.JellyfinUserId)}</span>}
            </div>
          ))}
          <button type="button" class="hpa-button hpa-button-ghost" onClick={() => update({ UserMappings: [...config.UserMappings, { JellyfinUserId: '', SeerrUserId: 0 }] })}>
            <span class="material-icons add" aria-hidden="true" /> Ajouter une correspondance
          </button>
        </div>
      )}

      {tab === 'Sonarr' && (
        <div class="hpa-panel">
          <p class="hpa-hint">Sert à la section « Prochains épisodes » (calendrier Sonarr).</p>
          <div class="hpa-grid">
            <Field label="URL de Sonarr">
              <Text value={config.SonarrUrl} onChange={(v) => update({ SonarrUrl: v })} placeholder="http://192.168.1.10:8989" />
            </Field>
            <Field label="Clé API">
              <Text type="password" value={config.SonarrApiKey} onChange={(v) => update({ SonarrApiKey: v })} />
            </Field>
          </div>
          <TestButton service="sonarr" />
        </div>
      )}

      {tab === 'Navigation' && (
        <div class="hpa-panel">
          <Check label="Afficher la barre de navigation dans l'en-tête" checked={config.Header.Enabled} onChange={(v) => update({ Header: { ...config.Header, Enabled: v } })} />
          <Check label="Le bouton loupe ouvre la recherche combinée (médiathèque + Seerr)" checked={config.Header.ReplaceSearch} onChange={(v) => update({ Header: { ...config.Header, ReplaceSearch: v } })} />
          <Check label="Masquer les liens d'origine de Jellyfin (favoris, bibliothèques) dans la barre" hint="Le logo et le menu restent. Ajoute des onglets « Bibliothèque Jellyfin » pour garder des raccourcis." checked={config.Header.HideNativeLinks} onChange={(v) => update({ Header: { ...config.Header, HideNativeLinks: v } })} />
          <div class="hpa-list">
            {config.Header.Tabs.map((t, i) => (
              <div key={i} class={`hpa-row ${t.Enabled ? '' : 'is-off'}`}>
                <input type="checkbox" checked={t.Enabled} title="Afficher" onChange={(e) => setTabs(config.Header.Tabs.map((x, j) => (j === i ? { ...x, Enabled: (e.target as HTMLInputElement).checked } : x)))} />
                <Text value={t.Title} onChange={(v) => setTabs(config.Header.Tabs.map((x, j) => (j === i ? { ...x, Title: v } : x)))} />
                <Select value={t.Type} onChange={(v) => setTabs(config.Header.Tabs.map((x, j) => (j === i ? { ...x, Type: v } : x)))} options={TAB_TYPES} />
                {t.Type === 'Library' && (
                  <Select
                    value={t.Target}
                    onChange={(v) => setTabs(config.Header.Tabs.map((x, j) => (j === i ? { ...x, Target: v } : x)))}
                    options={[['', 'Bibliothèque…'], ...libraries.map((l) => [l.id, l.name] as [string, string])]}
                  />
                )}
                {t.Type === 'Link' && <Text value={t.Target} placeholder="https://… ou #/…" onChange={(v) => setTabs(config.Header.Tabs.map((x, j) => (j === i ? { ...x, Target: v } : x)))} />}
                <IconButton icon="arrow_upward" title="Monter" disabled={i === 0} onClick={() => setTabs(move(config.Header.Tabs, i, -1))} />
                <IconButton icon="arrow_downward" title="Descendre" disabled={i === config.Header.Tabs.length - 1} onClick={() => setTabs(move(config.Header.Tabs, i, 1))} />
                <IconButton icon="delete" title="Supprimer" onClick={() => setTabs(config.Header.Tabs.filter((_, j) => j !== i))} />
              </div>
            ))}
          </div>
          <button type="button" class="hpa-button hpa-button-ghost" onClick={() => setTabs([...config.Header.Tabs, { Type: 'Link', Title: 'Lien', Enabled: true, Target: '' }])}>
            <span class="material-icons add" aria-hidden="true" /> Ajouter un onglet
          </button>
        </div>
      )}

      {tab === 'Hero' && (
        <div class="hpa-panel">
          <Check label="Afficher le carrousel" checked={config.Hero.Enabled} onChange={(v) => update({ Hero: { ...config.Hero, Enabled: v } })} />
          <Field label="Bibliothèques sources">
            <LibraryPicker libraries={libraries} value={config.Hero.LibraryIds} onChange={(v) => update({ Hero: { ...config.Hero, LibraryIds: v } })} />
          </Field>
          <div class="hpa-grid">
            <Field label="Nombre de diapositives">
              <NumberInput value={config.Hero.Count} min={1} max={30} onChange={(v) => update({ Hero: { ...config.Hero, Count: v } })} />
            </Field>
            <Field label="Intervalle (secondes)">
              <NumberInput value={config.Hero.IntervalSeconds} min={3} max={60} onChange={(v) => update({ Hero: { ...config.Hero, IntervalSeconds: v } })} />
            </Field>
            <Field label="Choix des titres">
              <Select value={config.Hero.Sort} onChange={(v) => update({ Hero: { ...config.Hero, Sort: v } })} options={SORTS.filter(([s]) => s !== 'DateLastContentAdded' && s !== 'Name')} />
            </Field>
          </div>
          <div class="hpa-checks">
            <Check label="Films" checked={config.Hero.IncludeMovies} onChange={(v) => update({ Hero: { ...config.Hero, IncludeMovies: v } })} />
            <Check label="Séries" checked={config.Hero.IncludeSeries} onChange={(v) => update({ Hero: { ...config.Hero, IncludeSeries: v } })} />
            <Check label="Masquer ce qui est déjà vu" checked={config.Hero.HideWatched} onChange={(v) => update({ Hero: { ...config.Hero, HideWatched: v } })} />
            <Check label="Seulement les titres avec un logo" checked={config.Hero.RequireLogo} onChange={(v) => update({ Hero: { ...config.Hero, RequireLogo: v } })} />
            <Check label="Ajouter des tendances Seerr" checked={config.Hero.IncludeSeerrTrending} onChange={(v) => update({ Hero: { ...config.Hero, IncludeSeerrTrending: v } })} />
          </div>
        </div>
      )}

      {tab === 'Sections' && (
        <div class="hpa-panel">
          <div class="hpa-list">
            {config.Sections.map((s, i) => (
              <div key={s.Id} class={`hpa-section ${s.Enabled ? '' : 'is-off'}`}>
                <div class="hpa-row">
                  <input type="checkbox" checked={s.Enabled} title="Afficher" onChange={(e) => setSections(config.Sections.map((x, j) => (j === i ? { ...x, Enabled: (e.target as HTMLInputElement).checked } : x)))} />
                  <button type="button" class="hpa-section-title" onClick={() => setOpenSection(openSection === s.Id ? null : s.Id)}>
                    <span class={`material-icons ${openSection === s.Id ? 'expand_less' : 'expand_more'}`} aria-hidden="true" />
                    <strong>{s.Title || '(sans titre)'}</strong>
                    <span class="hpa-tag">{SECTION_TYPES.find(([t]) => t === s.Type)?.[1] || s.Type}</span>
                  </button>
                  <IconButton icon="arrow_upward" title="Monter" disabled={i === 0} onClick={() => setSections(move(config.Sections, i, -1))} />
                  <IconButton icon="arrow_downward" title="Descendre" disabled={i === config.Sections.length - 1} onClick={() => setSections(move(config.Sections, i, 1))} />
                  <IconButton icon="content_copy" title="Dupliquer" onClick={() => setSections([...config.Sections.slice(0, i + 1), { ...s, Id: newId(), Title: `${s.Title} (copie)` }, ...config.Sections.slice(i + 1)])} />
                  <IconButton icon="delete" title="Supprimer" onClick={() => window.confirm(`Supprimer « ${s.Title} » ?`) && setSections(config.Sections.filter((_, j) => j !== i))} />
                </div>
                {openSection === s.Id && <SectionEditor section={s} libraries={libraries} onChange={(next) => setSections(config.Sections.map((x, j) => (j === i ? next : x)))} />}
              </div>
            ))}
          </div>
          <div class="hpa-add">
            <span>Ajouter :</span>
            {SECTION_TYPES.map(([type, label]) => (
              <button
                key={type}
                type="button"
                class="hpa-button hpa-button-ghost"
                onClick={() => {
                  const section = newSection(type);
                  setSections([...config.Sections, section]);
                  setOpenSection(section.Id);
                }}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
      )}

      {tab === 'Découvrir' && (
        <div class="hpa-panel">
          <p class="hpa-hint">Lignes affichées sur les pages Découvrir, Films et Séries (sources Seerr).</p>
          {(['Discover', 'Movies', 'Tv'] as const).map((page) => (
            <div key={page} class="hpa-discover-page">
              <h3 class="hpa-subtitle">{page === 'Discover' ? 'Découvrir' : page === 'Movies' ? 'Films' : 'Séries'}</h3>
              {config.DiscoverRows.map((r, i) =>
                r.Page !== page ? null : (
                  <div key={r.Id} class={`hpa-row ${r.Enabled ? '' : 'is-off'}`}>
                    <input type="checkbox" checked={r.Enabled} onChange={(e) => setRows(config.DiscoverRows.map((x, j) => (j === i ? { ...x, Enabled: (e.target as HTMLInputElement).checked } : x)))} />
                    <Text value={r.Title} onChange={(v) => setRows(config.DiscoverRows.map((x, j) => (j === i ? { ...x, Title: v } : x)))} />
                    <Text value={r.SeerrPath} placeholder="discover/movies" onChange={(v) => setRows(config.DiscoverRows.map((x, j) => (j === i ? { ...x, SeerrPath: v } : x)))} />
                    <Text value={r.SeerrQuery} placeholder="genre=28" onChange={(v) => setRows(config.DiscoverRows.map((x, j) => (j === i ? { ...x, SeerrQuery: v } : x)))} />
                    <Select value={r.MediaType} onChange={(v) => setRows(config.DiscoverRows.map((x, j) => (j === i ? { ...x, MediaType: v } : x)))} options={[['', 'Tout'], ['movie', 'Films'], ['tv', 'Séries']]} />
                    <IconButton icon="arrow_upward" title="Monter" onClick={() => setRows(move(config.DiscoverRows, i, -1))} />
                    <IconButton icon="arrow_downward" title="Descendre" onClick={() => setRows(move(config.DiscoverRows, i, 1))} />
                    <IconButton icon="delete" title="Supprimer" onClick={() => setRows(config.DiscoverRows.filter((_, j) => j !== i))} />
                  </div>
                ),
              )}
              <div class="hpa-add">
                <button type="button" class="hpa-button hpa-button-ghost" onClick={() => setRows([...config.DiscoverRows, { Id: newId(), Page: page, Title: 'Nouvelle ligne', Enabled: true, SeerrPath: page === 'Tv' ? 'discover/tv' : 'discover/movies', SeerrQuery: '', MediaType: '' }])}>
                  <span class="material-icons add" aria-hidden="true" /> Ajouter une ligne
                </button>
                <SeerrImport onPick={(s) => setRows([...config.DiscoverRows, { Id: newId(), Page: page, Title: s.title, Enabled: true, SeerrPath: s.seerrPath, SeerrQuery: s.seerrQuery, MediaType: '' }])} />
              </div>
            </div>
          ))}
        </div>
      )}

      <div class="hpa-savebar">
        {message && <span class="hpa-message">{message}</span>}
        {dirty && <span class="hpa-hint">Modifications non enregistrées</span>}
        <button type="button" class="hpa-button hpa-button-primary" disabled={!dirty || saving} onClick={save}>
          {saving ? 'Enregistrement…' : 'Enregistrer'}
        </button>
      </div>
    </div>
  );
}

declare global {
  interface Window {
    HomepageAdmin?: { mount: (el: HTMLElement) => void };
  }
}

window.HomepageAdmin = {
  mount(el: HTMLElement) {
    render(null, el);
    render(<Admin />, el);
  },
};
window.dispatchEvent(new CustomEvent('homepage-admin-ready'));
