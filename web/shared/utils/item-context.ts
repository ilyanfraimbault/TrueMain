import type { ChampionItemContextAxis, ChampionItemContextItem, ItemContextSlot } from '~~/shared/types/item-context'

/**
 * The wording of the situational build context (#1451).
 *
 * The API sends axis identifiers, never prose: the fold has no business deciding how a
 * situation reads, and the same identifier has to render differently on a card, in a
 * summary sentence and (later) in the matchup tool. This module is that one translation.
 */

/**
 * How each situation reads at both ends. The Low end is a real finding, not a negation
 * bolted on: an item built *against ranged* lanes is Low on the melee-count axis, and
 * "against a mostly ranged team" is the sentence a reader needs — never "not against a
 * mostly melee team".
 */
const AXIS_PHRASES: Record<string, { high: string, low: string }> = {
  EnemyMagicDamage: {
    high: 'against a magic-damage team',
    low: 'against a team with little magic damage',
  },
  EnemyPhysicalDamage: {
    high: 'against a physical-damage team',
    low: 'against a team with little physical damage',
  },
  EnemySustain: {
    high: 'against a team that heals',
    low: 'against a team with no healing',
  },
  EnemyCrowdControl: {
    high: 'against heavy crowd control',
    low: 'against little crowd control',
  },
  EnemyFrontline: {
    high: 'against a frontline',
    low: 'against no frontline',
  },
  EnemyMelee: {
    high: 'against a mostly melee team',
    low: 'against a mostly ranged team',
  },
  EnemyCrit: {
    high: 'against crit carries',
    low: 'against no crit carry',
  },
  EnemyArmorPenetration: {
    high: 'against lethality builders',
    low: 'against no lethality',
  },
  OpponentRanged: {
    high: 'against a ranged lane opponent',
    low: 'against a melee lane opponent',
  },
  OpponentLanePressure: {
    high: 'against a strong laner',
    low: 'against a weak laner',
  },
  OpponentMagicDamage: {
    high: 'against a magic-damage lane opponent',
    low: 'against a lane opponent with little magic damage',
  },
  OpponentSustain: {
    high: 'against a lane opponent that sustains',
    low: 'against a lane opponent with no sustain',
  },
  AllyMagicDamage: {
    high: 'when the team already deals magic damage',
    low: 'when the team has little magic damage',
  },
  AllyFrontline: {
    high: 'when the team already has a frontline',
    low: 'when the team has no frontline',
  },
  OwnGoldLeadAt15: {
    high: 'when ahead at 15 min',
    low: 'when behind at 15 min',
  },
}

/** Every axis this module knows how to word — the list the exhaustiveness test reads. */
export const KNOWN_ITEM_CONTEXT_AXES: readonly string[] = Object.keys(AXIS_PHRASES)

/**
 * How one situation reads, or `null` when the API sent an axis this build does not know.
 * Null rather than the raw identifier: a card showing `EnemyArmorPenetration` to a player
 * is worse than a card showing one line fewer.
 */
export function itemContextAxisPhrase(axis: ChampionItemContextAxis): string | null {
  return AXIS_PHRASES[axis.axis]?.[axis.bucket === 'High' ? 'high' : 'low'] ?? null
}

/** Only the findings this build can word — the card renders nothing it cannot explain. */
export function wordableAxes(item: ChampionItemContextItem): ChampionItemContextAxis[] {
  return item.axes.filter(axis => itemContextAxisPhrase(axis) !== null)
}

/**
 * A verdict as the card renders it: the API's item plus whatever the *client* knows about
 * the scope the reader is looking at.
 */
export interface ItemContextCard extends ChampionItemContextItem {
  /**
   * An extra scope clause the card must disclose. Today it carries one case: the champion
   * page's matchup filter re-slices every build panel around the card, but not the
   * verdicts — those are not folded per opponent. Without the clause a reader would take
   * "62% against a magic-damage team" as a figure inside the pinned matchup, which it is
   * not. Undefined when there is nothing extra to say.
   */
  scopeNote?: string
}

/** The key an item's verdict is looked up by: a slot and an item, since the two answer different questions. */
export function itemContextKey(slot: ItemContextSlot, itemId: number): string {
  return `${slot}:${itemId}`
}

/**
 * Index a response for the O(1) lookups the build panels do while rendering, keyed by
 * slot and item id.
 *
 * `allMatchups` attaches the scope clause once, here, rather than drilling a flag through
 * five components down to the card: the caller that knows a matchup is pinned is the one
 * that builds the index, and wording belongs in this module.
 */
export function indexItemContext(
  items: readonly ChampionItemContextItem[] | null | undefined,
  options: { allMatchups?: boolean } = {},
): Map<string, ItemContextCard> {
  const scopeNote = options.allMatchups ? 'all matchups' : undefined
  const index = new Map<string, ItemContextCard>()
  for (const item of items ?? []) {
    index.set(itemContextKey(item.slot, item.itemId), scopeNote ? { ...item, scopeNote } : item)
  }
  return index
}
