// Queue/history rows carry a *platform display name* from the source ("PlayStation 3", "Sega
// Genesis"), while the console palette in `consoleColors.ts` is keyed by the catalog's console
// folder slug ("ps3", "genesis"). This mirrors the backend's Module.Core/ConsoleDirectories.cs
// mapping (same normalization: lowercase, letters+digits only) so a downloaded item gets exactly
// the tile colour the Library shows for the same console.

import { getConsoleColor, ALL_CONSOLE_COLOR, type ConsoleColor } from './consoleColors'

// Keys are pre-normalised (lowercase, alphanumeric only — see `normalize`).
const PLATFORM_SLUGS: Record<string, string> = {
  // --- Sony ---
  playstation: 'psx', playstation1: 'psx', ps1: 'psx', psx: 'psx', psone: 'psx',
  playstation2: 'ps2', ps2: 'ps2',
  playstation3: 'ps3', ps3: 'ps3',
  playstationportable: 'psp', psp: 'psp',
  playstationvita: 'psvita', psvita: 'psvita', vita: 'psvita',

  // --- Nintendo ---
  nintendo: 'nes', nintendoentertainmentsystem: 'nes', nes: 'nes', famicom: 'nes',
  supernintendo: 'snes', supernintendoentertainmentsystem: 'snes', snes: 'snes', superfamicom: 'snes',
  nintendo64: 'n64', n64: 'n64',
  gamecube: 'gc', nintendogamecube: 'gc', gc: 'gc', ngc: 'gc',
  wii: 'wii', wiiu: 'wiiu',
  switch: 'switch', nintendoswitch: 'switch',
  gameboy: 'gb', gb: 'gb',
  gameboycolor: 'gbc', gbc: 'gbc',
  gameboyadvance: 'gba', gba: 'gba',
  nintendods: 'nds', nds: 'nds', nintendodsi: 'nds',
  nintendo3ds: 'n3ds', '3ds': 'n3ds',
  virtualboy: 'virtualboy',

  // --- Sega ---
  genesis: 'genesis', segagenesis: 'genesis', megadrive: 'genesis', segamegadrive: 'genesis',
  mastersystem: 'mastersystem', segamastersystem: 'mastersystem', sms: 'mastersystem',
  gamegear: 'gamegear', segagamegear: 'gamegear',
  segacd: 'segacd', megacd: 'segacd',
  sega32x: 'sega32x', '32x': 'sega32x', genesis32x: 'sega32x',
  saturn: 'saturn', segasaturn: 'saturn',
  dreamcast: 'dreamcast', segadreamcast: 'dreamcast',
  sg1000: 'sg-1000',

  // --- Microsoft ---
  xbox: 'xbox', xbox360: 'xbox360',

  // --- Atari ---
  atari2600: 'atari2600', '2600': 'atari2600',
  atari5200: 'atari5200', '5200': 'atari5200',
  atari7800: 'atari7800', '7800': 'atari7800',
  atarilynx: 'atarilynx', lynx: 'atarilynx',
  atarijaguar: 'atarijaguar', jaguar: 'atarijaguar',
  atarijaguarcd: 'atarijaguarcd', jaguarcd: 'atarijaguarcd',
  atarist: 'atarist',

  // --- NEC ---
  turbografx16: 'pcengine', turbografx: 'pcengine', tg16: 'pcengine', pcengine: 'pcengine',
  turbografxcd: 'pcenginecd', turbografx16cd: 'pcenginecd', tgcd: 'pcenginecd', pcenginecd: 'pcenginecd',
  supergrafx: 'supergrafx', pcfx: 'pcfx',

  // --- Other ---
  '3do': '3do',
  neogeo: 'neogeocd', neogeocd: 'neogeocd',
  neogeopocket: 'ngp', ngp: 'ngp',
  neogeopocketcolor: 'ngpc', ngpc: 'ngpc',
  wonderswan: 'wonderswan', wonderswancolor: 'wonderswancolor',
  colecovision: 'colecovision', intellivision: 'intellivision', vectrex: 'vectrex',
  odyssey2: 'odyssey2',
  commodore64: 'c64', c64: 'c64', amiga: 'amiga',
  msx: 'msx1', x68000: 'x68000',
}

/** Lowercase, keep only letters and digits (same rule as ConsoleDirectories.Normalize). */
function normalize(s: string): string {
  return s.toLowerCase().replace(/[^a-z0-9]/g, '')
}

/** Platform display name -> catalog console folder slug, or null when unknown. */
export function consoleSlugFromPlatform(platform: string | null | undefined): string | null {
  if (!platform) return null
  return PLATFORM_SLUGS[normalize(platform)] ?? null
}

/**
 * Platform display name -> the console palette chip (code + colour).
 * Unknown-but-present platforms fall through to `getConsoleColor`, which derives a deterministic
 * colour + short code from the string itself; an absent platform gets the neutral ALL colour.
 */
export function getPlatformColor(platform: string | null | undefined): ConsoleColor {
  if (!platform) return { ...ALL_CONSOLE_COLOR, code: '?' }
  const slug = consoleSlugFromPlatform(platform)
  return getConsoleColor(slug ?? normalize(platform))
}
