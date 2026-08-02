// Library hidden-console preference (#311): the `library_hidden_consoles` setting is a CSV of
// console slugs, shared here so Settings (writes it) and the Library rail (reads it) parse/compose
// it identically. Comparison is case-insensitive to match the backend's `lower(s.console)` filter.

/** Parse the CSV setting into a lowercase set for membership checks. */
export function parseHiddenConsoles(csv: string | undefined): Set<string> {
  return new Set((csv ?? '').split(',').map(s => s.trim().toLowerCase()).filter(Boolean))
}

/** Toggle one console's membership in the CSV setting, returning the new CSV to persist. */
export function toggleHiddenConsole(csv: string | undefined, console: string, hidden: boolean): string {
  const set = parseHiddenConsoles(csv)
  if (hidden) set.add(console.toLowerCase()); else set.delete(console.toLowerCase())
  return [...set].join(',')
}
