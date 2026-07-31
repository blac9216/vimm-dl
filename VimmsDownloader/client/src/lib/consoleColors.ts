// Console palette + generated cover-art fallback (issue #256 / design handoff "Global Shell" +
// "Design Tokens" -> Console palette). Hue groups by manufacturer, lightness ramps by generation,
// per the handoff table (recreated verbatim below) and extended for every console the catalog can
// return — see Modules/Module.Catalog/CatalogSystems.cs (the `console` folder slug is the key used
// throughout the frontend, e.g. CatalogGame.console) and Modules/Module.Core/ConsoleDirectories.cs.

export interface ConsoleColor {
  code: string
  color: string
}

// (All consoles) fallback — also used for unmatched/unknown console slugs.
export const ALL_CONSOLE_COLOR: ConsoleColor = { code: 'ALL', color: '#8f9cd8' }

// The handoff's table, verbatim, keyed by the catalog's console folder slug (not the DAT display
// name). Extended below with every other console CatalogSystems.All can emit.
export const CONSOLE_COLORS: Record<string, ConsoleColor> = {
  // --- Nintendo home consoles (pink/red hue ramp, lightness darkens by generation) ---
  nes: { code: 'NES', color: '#ff97a5' },
  fds: { code: 'FDS', color: '#ffa3ae' },
  snes: { code: 'SNES', color: '#f76a7f' },
  satellaview: { code: 'BSX', color: '#ef7f90' },
  sufami: { code: 'SFT', color: '#e97b8c' },
  n64: { code: 'N64', color: '#e5405b' },
  n64dd: { code: '64DD', color: '#d84a62' },
  gc: { code: 'GC', color: '#b62f47' },
  wii: { code: 'WII', color: '#8c2438' },

  // --- Nintendo handhelds (magenta/purple hue ramp) ---
  gb: { code: 'GB', color: '#efa8e0' },
  gbc: { code: 'GBC', color: '#e07fd0' },
  gba: { code: 'GBA', color: '#cd57bd' },
  virtualboy: { code: 'VB', color: '#d9699e' },
  pokemini: { code: 'PKMN', color: '#e8a0d8' },
  nds: { code: 'NDS', color: '#a83c9c' },
  n3ds: { code: '3DS', color: '#8a2f82' },

  // --- Sega (orange hue ramp) ---
  'sg-1000': { code: 'SG1K', color: '#ffdba0' },
  mastersystem: { code: 'SMS', color: '#ffd08a' },
  gamegear: { code: 'GG', color: '#ffc169' },
  genesis: { code: 'GEN', color: '#f79d3f' },
  sega32x: { code: '32X', color: '#e8890f' },
  segacd: { code: 'SCD', color: '#d9791f' },
  saturn: { code: 'SAT', color: '#ab5f18' },
  dreamcast: { code: 'DC', color: '#ffb45c' },
  naomi: { code: 'NAOMI', color: '#e0983f' },
  naomi2: { code: 'NAOM2', color: '#c47f2f' },
  segapico: { code: 'PICO', color: '#ffe0b0' },
  beena: { code: 'BEENA', color: '#ffd9a3' },

  // --- Sony (blue hue ramp) ---
  psx: { code: 'PS1', color: '#93bcff' },
  ps2: { code: 'PS2', color: '#6b9dff' },
  ps3: { code: 'PS3', color: '#3f7cf0' },
  psp: { code: 'PSP', color: '#2b5fc4' },
  psvita: { code: 'VITA', color: '#1f4aa0' },

  // --- Microsoft (green hue ramp; MSX grouped here per CatalogSystems' DAT source) ---
  xbox: { code: 'XBOX', color: '#3ee0a0' },
  xbox360: { code: 'X360', color: '#26b07c' },
  msx1: { code: 'MSX', color: '#52d9ad' },
  msx2: { code: 'MSX2', color: '#3dbd93' },

  // --- NEC (violet hue ramp) ---
  pcengine: { code: 'TG16', color: '#b79bff' },
  supergrafx: { code: 'SGFX', color: '#a688f0' },
  pcenginecd: { code: 'TGCD', color: '#9a7ce0' },
  pcfx: { code: 'PCFX', color: '#7a5cc0' },
  pc98: { code: 'PC98', color: '#6b4db0' },

  // --- SNK / Neo Geo (violet hue ramp, distinct shade from NEC) ---
  ngp: { code: 'NGP', color: '#b39dff' },
  ngpc: { code: 'NGPC', color: '#a688f5' },
  neogeocd: { code: 'NGCD', color: '#8560e6' },

  // --- 3DO / other disc systems (violet hue ramp) ---
  '3do': { code: '3DO', color: '#7a55e6' },
  cdimono1: { code: 'CDI', color: '#6f4fd6' },

  // --- Atari (teal/cyan hue ramp — not in the handoff table, extended along the same recipe) ---
  atari2600: { code: '2600', color: '#7fd9d4' },
  atari5200: { code: '5200', color: '#5cc4c0' },
  atari7800: { code: '7800', color: '#3fa8a8' },
  atari800: { code: '800', color: '#52b8b5' },
  atarilynx: { code: 'LYNX', color: '#8fe0d8' },
  atarijaguar: { code: 'JAG', color: '#1f7d80' },
  atarijaguarcd: { code: 'JAGCD', color: '#17656a' },
  atarist: { code: 'ST', color: '#4aa8a0' },

  // --- Commodore (lime/yellow-green hue ramp — extended) ---
  c64: { code: 'C64', color: '#c5e07a' },
  c16: { code: 'C16', color: '#a8cf5f' },
  vic20: { code: 'VIC20', color: '#d6ea94' },
  amiga: { code: 'AMIGA', color: '#7fae3f' },
  amigacd32: { code: 'CD32', color: '#6f9a35' },
  cdtv: { code: 'CDTV', color: '#5f8a2d' },

  // --- Bandai (gold/tan hue ramp — extended) ---
  wonderswan: { code: 'WS', color: '#e0c26a' },
  wonderswancolor: { code: 'WSC', color: '#c9a83f' },
}

// The long tail of one-off manufacturers (Arduboy, Coleco, Emerson, Fairchild, Sinclair, …) — each
// contributes a single console, so there's no real "generation ramp" to extend by hand. They get a
// deterministic hue (same hashing recipe as the cover-art fallback below) inside a shared muted band
// close to the ALL fallback hue, keeping the same saturation/lightness recipe as the rest of the
// palette instead of hand-picking ~30 more one-off hex values.
const MISC_CONSOLES: Record<string, string> = {
  arduboy: 'ARDB',
  pockchalv2: 'PCV2',
  casloopy: 'LOOPY',
  pv1000: 'PV1K',
  colecovision: 'COLECO',
  arcadia: 'ARCD',
  advision: 'ADVN',
  scv: 'SCV',
  channelf: 'CHF',
  supracan: 'SUPRA',
  gp32: 'GP32',
  vectrex: 'VECTR',
  gmaster: 'GMSTR',
  vc4000: 'VC4K',
  picno: 'PICNO',
  leappad: 'LPAD',
  leapster: 'LPSTR',
  odyssey2: 'ODY2',
  intellivision: 'INTV',
  videopac: 'VIDEO',
  studio2: 'STDO2',
  x1: 'X1',
  x68000: 'X68K',
  zxspectrum: 'ZXS',
  gamecom: 'GCOM',
  vsmile: 'VSMILE',
  crvision: 'CRV',
  supervision: 'SPRV',
  j2me: 'J2ME',
  palm: 'PALM',
  symbian: 'SYMB',
  zeebo: 'ZEEBO',
}

/** Deterministic string hash (djb2-ish) used for both the misc console hue and the cover-art hue. */
function hashHue(seed: string): number {
  let h = 0
  for (let i = 0; i < seed.length; i++) {
    h = (h * 31 + seed.charCodeAt(i)) >>> 0
  }
  return h % 360
}

for (const [slug, code] of Object.entries(MISC_CONSOLES)) {
  CONSOLE_COLORS[slug] = { code, color: `hsl(${hashHue(slug)} 42% 62%)` }
}

/** Console folder slug (CatalogGame.console) -> chip code + color. Unknown/empty -> the ALL fallback. */
export function getConsoleColor(console: string | null | undefined): ConsoleColor {
  if (!console) return ALL_CONSOLE_COLOR
  const key = console.toLowerCase()
  if (key === 'all' || key === '') return ALL_CONSOLE_COLOR
  const known = CONSOLE_COLORS[key]
  if (known) return known
  // Defensive fallback for any future catalog console not yet in the table above: a deterministic
  // color (same recipe as MISC_CONSOLES) and a code derived from the slug itself.
  return { code: console.slice(0, 5).toUpperCase(), color: `hsl(${hashHue(key)} 42% 62%)` }
}

// --- Generated cover-art fallback (used by CatalogThumb + the Library detail pane when no cached
// box art / title screen exists): a hash of the title picks a hue, rendered as the handoff's
// two-stop diagonal gradient, with derived initials overlaid. ---

const INITIALS_SKIP_WORDS = new Set(['the', 'of', 'a', 'and', 'to'])

/** 1-2 letter initials for a game title, skipping small connecting words (design handoff "Assets"). */
export function getCoverInitials(title: string): string {
  const words = title
    .split(/[\s:\-–—,'!.()[\]]+/)
    .map(w => w.trim())
    .filter(w => w.length > 0 && !INITIALS_SKIP_WORDS.has(w.toLowerCase()))

  if (words.length === 0) {
    const fallback = title.trim()
    return fallback.slice(0, 2).toUpperCase() || '?'
  }
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase()
  }
  return (words[0][0] + words[1][0]).toUpperCase()
}

/** The generated gradient backdrop for a title's cover-art fallback: `linear-gradient(140deg, hsl(H 48% 30%), hsl(H+50 55% 15%))`. */
export function getCoverGradient(title: string): string {
  const hue = hashHue(title)
  const hue2 = (hue + 50) % 360
  return `linear-gradient(140deg, hsl(${hue} 48% 30%), hsl(${hue2} 55% 15%))`
}
