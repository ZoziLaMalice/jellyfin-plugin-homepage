import type { HomeCard } from './types';

const locale = () => document.documentElement.lang || navigator.language || 'fr-FR';

export function formatRuntime(ticks?: number): string {
  const minutes = Math.round((ticks || 0) / 600000000);
  if (!minutes) return '';
  const hours = Math.floor(minutes / 60);
  return hours ? `${hours} h ${String(minutes % 60).padStart(2, '0')}` : `${minutes} min`;
}

export function formatMinutes(minutes?: number): string {
  return minutes ? formatRuntime(minutes * 600000000) : '';
}

export function statusLabel(status?: number): string | null {
  switch (status) {
    case 2:
      return 'Demandé';
    case 3:
      return 'En cours';
    case 4:
      return 'Partiel';
    case 5:
      return 'Disponible';
    case 6:
      return 'Bloqué';
    default:
      return null;
  }
}

/** "Aujourd'hui", "Demain", weekday this week, otherwise a short date — in the viewer's time zone. */
export function relativeDay(iso?: string): string | undefined {
  if (!iso) return undefined;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return undefined;
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const day = new Date(date);
  day.setHours(0, 0, 0, 0);
  const delta = Math.round((day.getTime() - today.getTime()) / 86400000);
  const time = date.toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit' });
  if (delta <= 0) return `Aujourd'hui · ${time}`;
  if (delta === 1) return `Demain · ${time}`;
  if (delta < 7) {
    const weekday = date.toLocaleDateString(locale(), { weekday: 'long' });
    return weekday.charAt(0).toUpperCase() + weekday.slice(1);
  }
  return date.toLocaleDateString(locale(), { day: 'numeric', month: 'short' });
}

export function metaLine(card: HomeCard): string[] {
  const parts: string[] = [];
  if (card.year) parts.push(String(card.year));
  if (card.officialRating) parts.push(card.officialRating);
  const runtime = card.mediaType === 'movie' ? formatRuntime(card.runTimeTicks) : '';
  if (runtime) parts.push(runtime);
  return parts;
}
