// Emulator compatibility status vocabulary + badge coloring, shared by the Library list and the
// click-to-expand detail panel (epic #122 / M3) so the two render compat badges identically.

// The canonical (RPCS3-derived) compat status vocabulary, best → worst; other emulators normalize
// into it. Drives the status-filter options and the badge coloring.
export const COMPAT_STATUSES = ['Playable', 'Ingame', 'Intro', 'Loadable', 'Nothing']

// Color a normalized compatibility status (Playable/Ingame/Intro/Loadable/Nothing).
export function compatClass(status: string): string {
  switch (status) {
    case 'Playable': return 'bg-ps-triangle/15 text-ps-triangle border-ps-triangle/25'
    case 'Ingame': return 'bg-accent/15 text-accent border-accent/30'
    case 'Intro':
    case 'Loadable': return 'bg-[#e0a060]/15 text-[#e0a060] border-[#e0a060]/30'
    default: return 'bg-ps-circle/10 text-[#e06070] border-ps-circle/20' // Nothing / unknown
  }
}
