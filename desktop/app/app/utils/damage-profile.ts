import type { ChampionDamageBuildReadModel, ChampionDamageProfileReadModel, ChampionDamageProfilesResponse } from '#shared/contract/api'

/**
 * The champion damage profiles of `GET /champions/damage-profiles` (#1905) and
 * the arithmetic a team bar needs, kept beside the API's own rules: the same
 * lookup the item-context fold applies, and the same damage-weighted share as
 * its `DraftAxisEvaluator`, so the bar the player sees and the axis behind the
 * build advice cannot disagree.
 */

/** The API's read models (generated, `#shared/contract/api`). */
export type DamageBuild = ChampionDamageBuildReadModel
export type DamageProfile = ChampionDamageProfileReadModel
export type DamageProfilesResponse = ChampionDamageProfilesResponse

/** A team's measured damage split, and how many of its picks it could not measure. */
export interface TeamDamage {
  physicalShare: number
  magicShare: number
  trueShare: number
  /** Picks counted in the shares. */
  measured: number
  /** Picks left out: a fallback class or no profile at all. */
  unmeasured: number
}

/** The profiles keyed the way `profileOf` reads them. */
export function indexProfiles(profiles: DamageProfile[]): Map<string, DamageProfile> {
  return new Map(profiles.map(profile => [`${profile.championId}:${profile.position ?? '*'}`, profile]))
}

/**
 * A champion's profile on a lane, its champion-wide entry when it has none
 * there (the snapshot's best-covered lane, or a fallback class), else null.
 */
export function profileOf(index: Map<string, DamageProfile>, championId: number, position: string | null | undefined): DamageProfile | null {
  return (position ? index.get(`${championId}:${position}`) : undefined)
    ?? index.get(`${championId}:*`)
    ?? null
}

/**
 * A team's damage split, each pick weighted by the damage it deals per game —
 * a support's damage type says less about the team than its carry's. Only
 * measured picks carry shares; null when none of them is measured.
 */
export function teamDamage(picks: (DamageProfile | null)[]): TeamDamage | null {
  const measured = picks.filter((pick): pick is DamageProfile & { damagePerGame: number } =>
    pick?.source === 'measured' && pick.damagePerGame !== null)
  if (measured.length === 0) return null

  const weight = measured.reduce((sum, pick) => sum + pick.damagePerGame, 0)
  const share = (of: (pick: DamageProfile) => number | null) => weight > 0
    ? measured.reduce((sum, pick) => sum + (of(pick) ?? 0) * pick.damagePerGame, 0) / weight
    : measured.reduce((sum, pick) => sum + (of(pick) ?? 0), 0) / measured.length

  return {
    physicalShare: share(pick => pick.physicalShare),
    magicShare: share(pick => pick.magicShare),
    trueShare: share(pick => pick.trueShare),
    measured: measured.length,
    unmeasured: picks.length - measured.length,
  }
}
