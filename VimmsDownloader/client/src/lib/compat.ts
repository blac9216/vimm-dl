// Emulator compatibility status vocabulary + badge coloring, shared by the Library list and the
// click-to-expand detail panel (epic #122 / M3) so the two render compat badges identically.

// The canonical (RPCS3-derived) compat status vocabulary, best → worst; other emulators normalize
// into it. Drives the status-filter options and the badge coloring.
export const COMPAT_STATUSES = ['Playable', 'Ingame', 'Intro', 'Loadable', 'Nothing']

// Color a normalized compatibility status (Playable/Ingame/Intro/Loadable/Nothing).
export function compatClass(status: string): string {
  switch (status) {
    case 'Playable': return 'bg-success/15 text-success border-success/25'
    case 'Ingame': return 'bg-accent/15 text-accent border-accent/30'
    case 'Intro':
    case 'Loadable': return 'bg-warning/15 text-warning border-warning/30'
    default: return 'bg-error/10 text-error border-error/20' // Nothing / unknown
  }
}
