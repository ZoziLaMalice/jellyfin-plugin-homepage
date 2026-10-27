import type { JSX } from 'preact';
import { detailsHash, goTo, imageUrl, serverId } from '../jellyfin';
import { relativeDay, statusLabel } from '../format';
import { effectiveStatus, layoutStore, openDetails, statusOverrides, useStore } from '../store';
import type { HomeCard } from '../types';
import { Icon } from './Icon';
import { requestNow } from './requests';

export type CardShape = 'Poster' | 'Landscape';

/** Attributes understood by Jellyfin's emby-itemscontainer: it starts playback on click. */
export function playAttributes(card: HomeCard): Record<string, string> {
  const resume = (card.positionTicks || 0) > 0;
  return {
    'data-action': resume ? 'resume' : 'play',
    'data-id': card.itemId || '',
    'data-serverid': serverId(),
    'data-type': card.itemType || (card.mediaType === 'tv' ? 'Series' : 'Movie'),
    'data-isfolder': String(Boolean(card.isFolder || card.itemType === 'Series')),
    'data-mediatype': 'Video',
    'data-positionticks': String(card.positionTicks || 0),
  };
}

export function openCard(card: HomeCard) {
  if (card.kind === 'local') {
    const target = card.mediaType === 'episode' ? card.itemId : card.detailsId || card.itemId;
    if (target) goTo(detailsHash(target));
    return;
  }
  openDetails(card);
}

export function StatusBadge({ card }: { card: HomeCard }) {
  const overrides = useStore(statusOverrides);
  if (card.kind === 'local') {
    return (
      <span class="hp-badge hp-badge-local" title="Dans ta médiathèque">
        <Icon name="check" />
      </span>
    );
  }
  const status = effectiveStatus(card, overrides);
  const label = statusLabel(status);
  return (
    <span class={`hp-badge hp-badge-seerr ${status && status > 1 ? `hp-status-${status}` : ''}`} title="Disponible via Seerr">
      <Icon name={status === 2 || status === 3 ? 'schedule' : status === 5 || status === 4 ? 'check' : 'add'} />
      <span>{label || card.badge || 'Seerr'}</span>
    </span>
  );
}

export function Card({ card, shape }: { card: HomeCard; shape: CardShape }) {
  const landscape = shape === 'Landscape';
  const image = landscape ? card.thumb || card.backdrop || card.poster : card.poster || card.thumb;
  const src = imageUrl(image, landscape ? 420 : 260);
  const progress = card.kind === 'local' && card.progress && card.progress > 0 && card.progress < 100 ? card.progress : 0;
  // Dates are only shown for what is still to come (next episodes, upcoming releases).
  const upcoming = card.date ? new Date(card.date).getTime() > Date.now() - 12 * 3600000 : false;
  const dateLabel = upcoming && !card.badge ? relativeDay(card.date) : undefined;
  const subtitle = card.subtitle || (card.year ? String(card.year) : '');

  const onClick = (event: JSX.TargetedMouseEvent<HTMLElement>) => {
    event.preventDefault();
    // Play buttons bubble up to Jellyfin's items container, which starts playback.
    if ((event.target as HTMLElement).closest('.itemAction')) return;
    openCard(card);
  };

  const onPlay = (event: JSX.TargetedMouseEvent<HTMLElement>) => {
    event.preventDefault();
    event.stopPropagation();
    const confirm = layoutStore.get()?.confirmRequests !== false;
    if (confirm || card.mediaType === 'tv') openDetails(card, true);
    else requestNow(card);
  };

  return (
    <div class={`hp-card hp-card-${landscape ? 'landscape' : 'poster'} ${card.kind === 'request' ? 'hp-card-request' : ''}`} data-key={card.key}>
      <a class="hp-card-image" href={card.kind === 'local' ? detailsHash(card.mediaType === 'episode' ? card.itemId || '' : card.detailsId || card.itemId || '') : undefined} onClick={onClick} title={card.title}>
        {src ? <img src={src} alt="" loading="lazy" decoding="async" /> : <div class="hp-card-fallback">{card.title}</div>}
        {landscape && card.logo && card.mediaType !== 'episode' && <img class="hp-card-logo" src={imageUrl(card.logo, 220)} alt="" loading="lazy" />}
        <div class="hp-card-shade" />
        <StatusBadge card={card} />
        {(card.badge && card.kind === 'local') || dateLabel ? <span class="hp-badge hp-badge-info">{card.kind === 'local' ? card.badge || dateLabel : dateLabel}</span> : null}
        {card.kind === 'local' && card.itemId ? (
          <button type="button" class="itemAction hp-card-play" title={card.positionTicks ? 'Reprendre' : 'Lecture'} {...playAttributes(card)}>
            <Icon name="play_arrow" />
          </button>
        ) : card.kind === 'request' ? (
          <button type="button" class="hp-card-play hp-card-play-request" title="Demander" onClick={onPlay}>
            <Icon name="add" />
          </button>
        ) : null}
        {progress > 0 && (
          <div class="hp-progress">
            <div style={{ width: `${progress}%` }} />
          </div>
        )}
      </a>
      <div class="hp-card-text" onClick={onClick}>
        <div class="hp-card-title">{card.title}</div>
        {subtitle && <div class="hp-card-subtitle">{subtitle}</div>}
      </div>
    </div>
  );
}
