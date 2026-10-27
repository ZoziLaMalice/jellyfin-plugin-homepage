import { useEffect, useState } from 'preact/hooks';
import { Card } from '../components/Card';
import { api } from '../jellyfin';
import type { HomeCard } from '../types';

export function RequestsPage() {
  const [items, setItems] = useState<HomeCard[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<HomeCard[]>('Homepage/MyRequests')
      .then(setItems)
      .catch((e: Error) => setError(e.message));
  }, []);

  return (
    <div class="hp-page hp-page-requests">
      <h1 class="hp-page-title">Mes demandes</h1>
      {error && <p class="hp-error">{error}</p>}
      {!items && !error && <div class="hp-spinner" />}
      {items && items.length === 0 && <p class="hp-empty">Tu n'as encore rien demandé.</p>}
      {items && items.length > 0 && (
        <div class="hp-grid">
          {items.map((card) => (
            <Card key={card.key} card={card} shape="Poster" />
          ))}
        </div>
      )}
    </div>
  );
}
