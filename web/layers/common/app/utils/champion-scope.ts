import { DEFAULT_ELO_BRACKET, ELO_BRACKET_ALL } from '#shared/utils/elo-brackets'

/**
 * Whose games a champion page reads: everyone's (`/champions/{slug}`, and the
 * pages that list champions), or one account's (`/truemains/{nameTag}/champions/{slug}`).
 * The scope decides what differs between the two: the rank the page opens on,
 * the read it sends, and which patch its picker shows.
 */
export type ChampionScope = { kind: 'global' } | { kind: 'player', nameTag: string }

export const GLOBAL_CHAMPION_SCOPE: ChampionScope = { kind: 'global' }

/**
 * The rank a page opens on. Master+ for everyone's games, the site's promise of
 * high-elo builds; every rank for one account, whose games re-sliced by rank
 * empty the build of anyone below Master (that page renders no rank select).
 */
export function defaultEloBracketFor(scope: ChampionScope['kind']): string {
  return scope === 'player' ? ELO_BRACKET_ALL : DEFAULT_ELO_BRACKET
}
