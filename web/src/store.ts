import { useEffect, useState } from 'preact/hooks';
import type { ClientLayout, HomeCard } from './types';

/** Minimal observable value shared by the components. */
export class Store<T> {
  private listeners = new Set<(value: T) => void>();
  constructor(private value: T) {}

  get(): T {
    return this.value;
  }

  set(value: T) {
    this.value = value;
    this.listeners.forEach((listener) => listener(value));
  }

  subscribe(listener: (value: T) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
}

export function useStore<T>(store: Store<T>): T {
  const [value, setValue] = useState(store.get());
  useEffect(() => {
    setValue(store.get());
    return store.subscribe(setValue);
  }, [store]);
  return value;
}

export const layoutStore = new Store<ClientLayout | null>(null);

/** Seerr statuses changed locally (after a request) so every card of the same title updates. */
export const statusOverrides = new Store<Record<string, number>>({});

export function setStatus(card: Pick<HomeCard, 'mediaType' | 'tmdbId'>, status: number) {
  if (!card.tmdbId) return;
  statusOverrides.set({ ...statusOverrides.get(), [`${card.mediaType}:${card.tmdbId}`]: status });
}

export function effectiveStatus(card: HomeCard, overrides: Record<string, number>): number | undefined {
  return card.tmdbId ? overrides[`${card.mediaType}:${card.tmdbId}`] ?? card.seerrStatus : card.seerrStatus;
}

export interface Toast {
  id: number;
  message: string;
  tone: 'ok' | 'error' | 'info';
}

export const toastStore = new Store<Toast[]>([]);
let toastId = 0;

export function toast(message: string, tone: Toast['tone'] = 'info') {
  const id = ++toastId;
  toastStore.set([...toastStore.get(), { id, message, tone }]);
  window.setTimeout(() => toastStore.set(toastStore.get().filter((t) => t.id !== id)), tone === 'error' ? 7000 : 4500);
}

/** The card whose details/request dialog is open. */
export const modalStore = new Store<{ card: HomeCard; focusRequest: boolean } | null>(null);

export function openDetails(card: HomeCard, focusRequest = false) {
  modalStore.set({ card, focusRequest });
}
