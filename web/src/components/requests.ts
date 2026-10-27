import { api } from '../jellyfin';
import { setStatus, toast } from '../store';
import type { HomeCard, RequestOutcome } from '../types';

/** Sends a Seerr request for the card (as the current user) and updates every matching card. */
export async function requestNow(card: HomeCard, seasons?: number[]): Promise<RequestOutcome> {
  if (!card.tmdbId) {
    const outcome = { success: false, message: "Ce titre n'a pas d'identifiant TMDB, impossible de le demander." };
    toast(outcome.message, 'error');
    return outcome;
  }
  try {
    const outcome = await api<RequestOutcome>('Homepage/Request', {
      method: 'POST',
      body: { mediaType: card.mediaType === 'movie' ? 'movie' : 'tv', tmdbId: card.tmdbId, seasons: seasons && seasons.length ? seasons : null },
    });
    if (outcome.success) setStatus(card, outcome.seerrStatus || 2);
    toast(outcome.success ? `${card.title} : ${outcome.message}` : outcome.message, outcome.success ? 'ok' : 'error');
    return outcome;
  } catch (error) {
    const message = `Échec de la demande : ${(error as Error).message}`;
    toast(message, 'error');
    return { success: false, message };
  }
}
