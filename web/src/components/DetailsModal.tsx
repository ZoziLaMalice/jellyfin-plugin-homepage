import { useEffect, useRef, useState } from 'preact/hooks';
import { api, detailsHash, goTo, imageUrl } from '../jellyfin';
import { formatMinutes, statusLabel } from '../format';
import { effectiveStatus, modalStore, statusOverrides, useStore } from '../store';
import type { MediaDetails } from '../types';
import { Icon } from './Icon';
import { requestNow } from './requests';

export function DetailsModal() {
  const state = useStore(modalStore);
  const overrides = useStore(statusOverrides);
  const [details, setDetails] = useState<MediaDetails | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<number[]>([]);
  const [sending, setSending] = useState(false);
  const requestButton = useRef<HTMLButtonElement>(null);
  const card = state?.card;
  const close = () => modalStore.set(null);

  useEffect(() => {
    setDetails(null);
    setError(null);
    setSelected([]);
    if (!card) return;
    if (!card.tmdbId) {
      setError("Ce titre n'est pas identifié (pas d'identifiant TMDB).");
      return;
    }
    let cancelled = false;
    api<MediaDetails>(`Homepage/Details?mediaType=${card.mediaType === 'movie' ? 'movie' : 'tv'}&tmdbId=${card.tmdbId}`)
      .then((result) => {
        if (cancelled) return;
        setDetails(result);
        const requestable = result.seasons.filter((s) => s.status === 1 || s.status === 7).map((s) => s.number);
        const wanted = (card.requestSeasons || []).filter((n) => requestable.includes(n));
        setSelected(wanted.length ? wanted : requestable);
      })
      .catch((e: Error) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
    };
  }, [card?.key]);

  useEffect(() => {
    if (!card) return;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && close();
    document.addEventListener('keydown', onKey);
    document.documentElement.classList.add('hp-modal-open');
    return () => {
      document.removeEventListener('keydown', onKey);
      document.documentElement.classList.remove('hp-modal-open');
    };
  }, [card?.key]);

  useEffect(() => {
    if (details && state?.focusRequest) requestButton.current?.focus();
  }, [details]);

  if (!card) return null;

  const status = details ? effectiveStatus({ ...card, seerrStatus: details.seerrStatus }, overrides) : effectiveStatus(card, overrides);
  const alreadyAsked = status === 2 || status === 3 || status === 5;
  const isTv = card.mediaType !== 'movie';
  const canRequest = Boolean(details?.canRequest) && !(card.mediaType === 'movie' && alreadyAsked) && (!isTv || selected.length > 0);

  const send = async () => {
    if (!canRequest || sending) return;
    setSending(true);
    const outcome = await requestNow(card, isTv ? selected : undefined);
    setSending(false);
    if (outcome.success) close();
  };

  const toggleSeason = (n: number) => setSelected((s) => (s.includes(n) ? s.filter((x) => x !== n) : [...s, n].sort((a, b) => a - b)));
  const backdrop = imageUrl(details?.backdrop || card.backdrop, 1280);
  const poster = imageUrl(details?.poster || card.poster, 342);
  const meta = [
    details?.year || card.year,
    details?.runtime ? formatMinutes(details.runtime) : null,
    details?.rating || card.rating ? `★ ${(details?.rating || card.rating)!.toFixed(1)}` : null,
  ].filter(Boolean);

  return (
    <div class="hp-modal-backdrop" onClick={(e) => e.target === e.currentTarget && close()}>
      <div class="hp-modal" role="dialog" aria-modal="true" aria-label={card.title}>
        <div class="hp-modal-banner">
          {backdrop && <img src={backdrop} alt="" />}
          <div class="hp-modal-banner-shade" />
          <button type="button" class="hp-modal-close" onClick={close} aria-label="Fermer">
            <Icon name="close" />
          </button>
        </div>
        <div class="hp-modal-body">
          {poster && <img class="hp-modal-poster" src={poster} alt="" />}
          <div class="hp-modal-main">
            <h2 class="hp-modal-title">{details?.title || card.title}</h2>
            {details?.tagline && <div class="hp-modal-tagline">{details.tagline}</div>}
            <div class="hp-modal-meta">
              {meta.map((m) => (
                <span key={String(m)}>{m}</span>
              ))}
              {(details?.genres || card.genres || []).slice(0, 4).map((g) => (
                <span key={g} class="hp-chip">
                  {g}
                </span>
              ))}
              {statusLabel(status) && <span class={`hp-chip hp-status-${status}`}>{statusLabel(status)}</span>}
            </div>
            {error && <p class="hp-error">{error}</p>}
            {!details && !error && <div class="hp-spinner" />}
            {(details?.overview || card.overview) && <p class="hp-modal-overview">{details?.overview || card.overview}</p>}
            {details && (details.director || details.cast.length > 0) && (
              <dl class="hp-modal-credits">
                {details.director && (
                  <>
                    <dt>{isTv ? 'Création' : 'Réalisation'}</dt>
                    <dd>{details.director}</dd>
                  </>
                )}
                {details.cast.length > 0 && (
                  <>
                    <dt>Avec</dt>
                    <dd>{details.cast.join(', ')}</dd>
                  </>
                )}
              </dl>
            )}

            {details && isTv && details.seasons.length > 0 && (
              <div class="hp-seasons">
                <div class="hp-seasons-header">
                  <span>Saisons</span>
                  {details.seasons.some((s) => s.status === 1 || s.status === 7) && (
                    <button
                      type="button"
                      class="hp-link"
                      onClick={() => {
                        const all = details.seasons.filter((s) => s.status === 1 || s.status === 7).map((s) => s.number);
                        setSelected(selected.length === all.length ? [] : all);
                      }}
                    >
                      {selected.length === details.seasons.filter((s) => s.status === 1 || s.status === 7).length ? 'Tout désélectionner' : 'Tout sélectionner'}
                    </button>
                  )}
                </div>
                <div class="hp-season-list">
                  {details.seasons.map((s) => {
                    const free = s.status === 1 || s.status === 7;
                    return (
                      <label key={s.number} class={`hp-season ${free ? '' : 'is-locked'}`}>
                        <input type="checkbox" checked={free ? selected.includes(s.number) : true} disabled={!free} onChange={() => toggleSeason(s.number)} />
                        <span class="hp-season-name">{s.name}</span>
                        <span class="hp-season-info">{free ? `${s.episodeCount} ép.` : statusLabel(s.status)}</span>
                      </label>
                    );
                  })}
                </div>
              </div>
            )}

            <div class="hp-modal-actions">
              {details?.localItemId && (
                <button
                  type="button"
                  class="hp-button hp-button-primary"
                  onClick={() => {
                    close();
                    goTo(detailsHash(details.localItemId!));
                  }}
                >
                  <Icon name="play_arrow" /> Voir dans la médiathèque
                </button>
              )}
              {details && (details.canRequest || !details.localItemId) && (
                <button ref={requestButton} type="button" class="hp-button hp-button-request" disabled={!canRequest || sending} onClick={send}>
                  <Icon name={sending ? 'hourglass_empty' : alreadyAsked && !details.canRequest ? 'schedule' : 'add'} />
                  {sending
                    ? 'Envoi…'
                    : details.canRequest
                      ? isTv
                        ? selected.length > 1
                          ? `Demander ${selected.length} saisons`
                          : selected.length === 1
                            ? `Demander la saison ${selected[0]}`
                            : 'Choisis une saison'
                        : 'Demander'
                      : details.cannotRequestReason || statusLabel(status) || 'Indisponible'}
                </button>
              )}
              {details?.trailerUrl && (
                <a class="hp-button hp-button-secondary" href={details.trailerUrl} target="_blank" rel="noopener noreferrer">
                  <Icon name="movie" /> Bande-annonce
                </a>
              )}
              {details?.seerrLink && (
                <a class="hp-button hp-button-ghost" href={details.seerrLink} target="_blank" rel="noopener noreferrer">
                  <Icon name="open_in_new" /> Seerr
                </a>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
