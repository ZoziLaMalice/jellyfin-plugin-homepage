/** Material icons are already loaded by the Jellyfin web client (one CSS class per icon). */
export function Icon({ name, class: className }: { name: string; class?: string }) {
  return <span class={`material-icons hp-icon ${name} ${className || ''}`} aria-hidden="true" />;
}
