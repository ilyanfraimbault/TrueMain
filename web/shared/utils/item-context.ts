import type { ChampionItemContextAxis, ChampionItemContextItem, ItemContextSlot } from '~~/shared/types/item-context'

/**
 * The wording of the situational build context (#1451).
 *
 * The API sends axis identifiers, never prose: the fold has no business deciding how a
 * situation reads, and the same identifier has to render differently on a card, in a
 * summary sentence and (later) in the matchup tool. This module is that one translation.
 */

/**
 * A situation reads as a short phrase with one highlighted term — the thing the reader is
 * actually being told to answer.
 */
export interface ItemContextPhraseToken {
  text: string
  /** The term to colour. Absent on the connecting words. */
  tone?: ItemContextTone
}

/**
 * The colours a situation's key term can take.
 *
 * They are the item tooltip's own vocabulary, not a second one invented for this card: the
 * card sits directly under an item description where magic damage is already cyan and
 * physical damage already orange, and two conventions on one surface would read as a bug.
 * The rule those colours follow is Riot's — **damage is coloured by the resistance that
 * blocks it** (physical → armour orange, magic → magic-resist cyan), which is documented at
 * the source in `tooltip-parser/tag-classes.ts` and is why "magic damage" is cyan here and
 * not the ability-power violet.
 *
 * Terms with no stat behind them — melee, ranged, a strong laner — take no colour. A card
 * where every other word is tinted says nothing; the colour has to mark the answerable
 * threat, not decorate the sentence.
 */
export type ItemContextTone
  = | 'magic'
    | 'physical'
    | 'heal'
    | 'cc'
    | 'armor'
    | 'crit'
    | 'lethality'
    | 'gold'

/** Tone → the same Tailwind stat class the item tooltip above the card uses. */
export const ITEM_CONTEXT_TONE_CLASS: Record<ItemContextTone, string> = {
  magic: 'text-stat-mr',
  physical: 'text-stat-ad',
  heal: 'text-stat-hsp',
  cc: 'text-stat-status',
  armor: 'text-stat-armor',
  crit: 'text-stat-crit',
  lethality: 'text-stat-lethality',
  gold: 'text-stat-gold',
}

/** Shorthand: `phrase('against a ', ['magic-damage', 'magic'], ' team')`. */
function phrase(...parts: (string | [string, ItemContextTone])[]): ItemContextPhraseToken[] {
  return parts.map(part => (typeof part === 'string' ? { text: part } : { text: part[0], tone: part[1] }))
}

/**
 * How each situation reads at both ends. The Low end is a real finding, not a negation
 * bolted on: an item built *against ranged* lanes is Low on the melee-count axis, and
 * "against a mostly ranged team" is the sentence a reader needs — never "not against a
 * mostly melee team".
 */
const AXIS_PHRASES: Record<string, { high: ItemContextPhraseToken[], low: ItemContextPhraseToken[] }> = {
  EnemyMagicDamage: {
    high: phrase('against a ', ['magic-damage', 'magic'], ' team'),
    low: phrase('against a team with little ', ['magic damage', 'magic']),
  },
  EnemyPhysicalDamage: {
    high: phrase('against a ', ['physical-damage', 'physical'], ' team'),
    low: phrase('against a team with little ', ['physical damage', 'physical']),
  },
  EnemySustain: {
    high: phrase('against a team that ', ['heals', 'heal']),
    low: phrase('against a team with no ', ['healing', 'heal']),
  },
  EnemyCrowdControl: {
    high: phrase('against heavy ', ['crowd control', 'cc']),
    low: phrase('against little ', ['crowd control', 'cc']),
  },
  EnemyFrontline: {
    high: phrase('against a ', ['frontline', 'armor']),
    low: phrase('against no ', ['frontline', 'armor']),
  },
  EnemyMelee: {
    high: phrase('against a mostly melee team'),
    low: phrase('against a mostly ranged team'),
  },
  EnemyCrit: {
    high: phrase('against ', ['crit', 'crit'], ' carries'),
    low: phrase('against no ', ['crit', 'crit'], ' carry'),
  },
  EnemyArmorPenetration: {
    high: phrase('against ', ['lethality', 'lethality'], ' builders'),
    low: phrase('against no ', ['lethality', 'lethality']),
  },
  OpponentRanged: {
    high: phrase('against a ranged lane opponent'),
    low: phrase('against a melee lane opponent'),
  },
  OpponentLanePressure: {
    high: phrase('against a strong laner'),
    low: phrase('against a weak laner'),
  },
  OpponentMagicDamage: {
    high: phrase('against a ', ['magic-damage', 'magic'], ' lane opponent'),
    low: phrase('against a lane opponent with little ', ['magic damage', 'magic']),
  },
  OpponentSustain: {
    high: phrase('against a lane opponent that ', ['sustains', 'heal']),
    low: phrase('against a lane opponent with no ', ['sustain', 'heal']),
  },
  AllyMagicDamage: {
    high: phrase('when the team already deals ', ['magic damage', 'magic']),
    low: phrase('when the team has little ', ['magic damage', 'magic']),
  },
  AllyFrontline: {
    high: phrase('when the team already has a ', ['frontline', 'armor']),
    low: phrase('when the team has no ', ['frontline', 'armor']),
  },
  OwnGoldLeadAt15: {
    high: phrase('when ', ['ahead', 'gold'], ' at 15 min'),
    low: phrase('when ', ['behind', 'gold'], ' at 15 min'),
  },
}

/** Every axis this module knows how to word — the list the exhaustiveness test reads. */
export const KNOWN_ITEM_CONTEXT_AXES: readonly string[] = Object.keys(AXIS_PHRASES)

/**
 * How one situation reads, or `null` when the API sent an axis this build does not know.
 * Null rather than the raw identifier: a card showing `EnemyArmorPenetration` to a player
 * is worse than a card showing one line fewer.
 */
export function itemContextAxisPhrase(axis: ChampionItemContextAxis): ItemContextPhraseToken[] | null {
  return AXIS_PHRASES[axis.axis]?.[axis.bucket === 'High' ? 'high' : 'low'] ?? null
}

/** The same phrase as flat text — for tests, and for anywhere prose is needed without markup. */
export function itemContextAxisText(axis: ChampionItemContextAxis): string | null {
  return itemContextAxisPhrase(axis)?.map(token => token.text).join('') ?? null
}

/** Only the findings this build can word — the card renders nothing it cannot explain. */
export function wordableAxes(item: ChampionItemContextItem): ChampionItemContextAxis[] {
  return item.axes.filter(axis => itemContextAxisPhrase(axis) !== null)
}

/**
 * A verdict as the card renders it. Nothing is added to the API's item today — the alias
 * stays because every call site is typed on it, and the next thing the card needs to know
 * that the API cannot tell it will land here rather than in a new prop drilled through
 * five components.
 */
export type ItemContextCard = ChampionItemContextItem

/** The key an item's verdict is looked up by: a slot and an item, since the two answer different questions. */
export function itemContextKey(slot: ItemContextSlot, itemId: number): string {
  return `${slot}:${itemId}`
}

/**
 * Index a response for the O(1) lookups the build panels do while rendering, keyed by
 * slot and item id.
 */
export function indexItemContext(
  items: readonly ChampionItemContextItem[] | null | undefined,
): Map<string, ItemContextCard> {
  const index = new Map<string, ItemContextCard>()
  for (const item of items ?? []) {
    index.set(itemContextKey(item.slot, item.itemId), item)
  }
  return index
}
