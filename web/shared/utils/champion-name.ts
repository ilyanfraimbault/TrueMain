/**
 * A champion's name, or the one stand-in every surface uses while the static
 * list has not loaded or does not know the id yet (a champion released after the
 * cached list): `Champion 950`. One string, so a missing name reads the same on
 * a match row, the scoreboard and the leaderboard (#2031).
 *
 * `champion` is the static entry, or a name already resolved upstream.
 */
export function championDisplayName(championId: number, champion?: { name: string } | string | null): string {
  const name = typeof champion === 'string' ? champion : champion?.name
  return name || `Champion ${championId}`
}
