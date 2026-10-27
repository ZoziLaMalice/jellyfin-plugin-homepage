import type { ComponentChildren } from 'preact';

export function Field({ label, hint, children }: { label: string; hint?: string; children: ComponentChildren }) {
  return (
    <label class="hpa-field">
      <span class="hpa-label">{label}</span>
      {children}
      {hint && <span class="hpa-hint">{hint}</span>}
    </label>
  );
}

export function Text({ value, onChange, placeholder, type = 'text' }: { value: string; onChange: (v: string) => void; placeholder?: string; type?: string }) {
  return <input class="hpa-input" type={type} value={value || ''} placeholder={placeholder} onInput={(e) => onChange((e.target as HTMLInputElement).value)} autocomplete="off" spellcheck={false} />;
}

export function NumberInput({ value, onChange, min, max }: { value: number; onChange: (v: number) => void; min?: number; max?: number }) {
  return (
    <input
      class="hpa-input hpa-input-number"
      type="number"
      value={value}
      min={min}
      max={max}
      onInput={(e) => {
        const n = Number.parseInt((e.target as HTMLInputElement).value, 10);
        if (!Number.isNaN(n)) onChange(n);
      }}
    />
  );
}

export function Select({ value, onChange, options }: { value: string; onChange: (v: string) => void; options: [string, string][] }) {
  return (
    <select class="hpa-input" value={value} onChange={(e) => onChange((e.target as HTMLSelectElement).value)}>
      {options.map(([v, label]) => (
        <option key={v} value={v}>
          {label}
        </option>
      ))}
    </select>
  );
}

export function Check({ checked, onChange, label, hint }: { checked: boolean; onChange: (v: boolean) => void; label: string; hint?: string }) {
  return (
    <label class="hpa-check">
      <input type="checkbox" checked={checked} onChange={(e) => onChange((e.target as HTMLInputElement).checked)} />
      <span>
        {label}
        {hint && <span class="hpa-hint">{hint}</span>}
      </span>
    </label>
  );
}

export function IconButton({ icon, title, onClick, disabled }: { icon: string; title: string; onClick: () => void; disabled?: boolean }) {
  return (
    <button type="button" class="hpa-icon-button" title={title} aria-label={title} onClick={onClick} disabled={disabled}>
      <span class={`material-icons ${icon}`} aria-hidden="true" />
    </button>
  );
}

export function move<T>(list: T[], index: number, delta: number): T[] {
  const next = list.slice();
  const target = index + delta;
  if (target < 0 || target >= next.length) return next;
  const [item] = next.splice(index, 1);
  next.splice(target, 0, item);
  return next;
}

export function newId(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
}
