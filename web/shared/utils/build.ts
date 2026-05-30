/**
 * Minimum pickrate (as a 0–1 ratio) an alternative must reach to be shown in a
 * build "variations" panel. League's long tail is heavily skewed, so anything
 * below this is statistical noise that clutters the panel without informing the
 * choice. The build *tree* is intentionally exempt — it shows the full path.
 */
export const MIN_VARIATION_PICKRATE = 0.05

/**
 * Drop variation options whose pickrate falls below {@link MIN_VARIATION_PICKRATE}.
 * Works on any build option shape (items, spells, skill order, rune pages) since
 * they all expose a `pickRate`.
 */
export function filterByPickRate<T extends { pickRate: number }>(options: T[]): T[] {
  return options.filter(option => option.pickRate >= MIN_VARIATION_PICKRATE)
}
