import { toastStore, useStore } from '../store';
import { Icon } from './Icon';

export function Toasts() {
  const toasts = useStore(toastStore);
  if (toasts.length === 0) return null;
  return (
    <div class="hp-toasts" role="status" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} class={`hp-toast hp-toast-${t.tone}`}>
          <Icon name={t.tone === 'ok' ? 'check_circle' : t.tone === 'error' ? 'error_outline' : 'info_outline'} />
          <span>{t.message}</span>
        </div>
      ))}
    </div>
  );
}
