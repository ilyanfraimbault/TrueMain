import { hasPlus, isEloTier, tierPlus } from '#common/utils/elo-brackets'

/**
 * The bracket the draft reads for a player of `tier` (#1906): their tier and
 * every tier above it, so a Gold player is answered with Gold-and-up games —
 * close to how their games are played, without the sample of one tier alone.
 * No tier (unranked, or never seen on this machine) reads every bracket.
 */
export function draftEloBracket(tier: string | null | undefined): string | null {
  const upper = tier?.toUpperCase()
  if (!isEloTier(upper)) return null
  return hasPlus(upper) ? tierPlus(upper) : upper
}
