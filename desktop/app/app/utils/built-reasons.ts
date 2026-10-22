import type { ItemContextPhraseToken, ItemContextTone } from '#shared/utils/item-context'

/**
 * How the in-game reasons of the next item read (#1750): what the enemies
 * have built when the decision opened — their armour, their magic resist,
 * their life steal, the damage they built, their crit.
 *
 * The app's own wording, not the site's: these axes are read by the next
 * item alone (product decision, 2026-10-05), and the champion page's hover
 * cards have no sentence for them. Each reads as a game state — "once the
 * enemies have built armor" — never as a draft, since nobody can know it in
 * champion select.
 */

function phrase(...parts: (string | [string, ItemContextTone])[]): ItemContextPhraseToken[] {
  return parts.map(part => (typeof part === 'string' ? { text: part } : { text: part[0], tone: part[1] }))
}

const BUILT_AXIS_PHRASES: Record<string, { high: ItemContextPhraseToken[], low: ItemContextPhraseToken[] }> = {
  EnemyArmorBuilt: {
    high: phrase('once the enemies have built ', ['armor', 'armor']),
    low: phrase('while the enemies have little ', ['armor', 'armor']),
  },
  EnemyMagicResistBuilt: {
    high: phrase('once the enemies have built ', ['magic resist', 'magic']),
    low: phrase('while the enemies have little ', ['magic resist', 'magic']),
  },
  OpponentArmorBuilt: {
    high: phrase('once your lane opponent has built ', ['armor', 'armor']),
    low: phrase('while your lane opponent has no ', ['armor', 'armor']),
  },
  OpponentMagicResistBuilt: {
    high: phrase('once your lane opponent has built ', ['magic resist', 'magic']),
    low: phrase('while your lane opponent has no ', ['magic resist', 'magic']),
  },
  EnemyHealingBuilt: {
    high: phrase('once the enemies have built ', ['life steal', 'heal']),
    low: phrase('while no enemy has built ', ['life steal', 'heal']),
  },
  EnemyMagicDamageBuilt: {
    high: phrase('once the enemies have built ', ['magic damage', 'magic']),
    low: phrase('once the enemies have built ', ['physical damage', 'physical']),
  },
  EnemyCritBuilt: {
    high: phrase('once the enemies have built ', ['crit', 'crit']),
    low: phrase('while the enemies have built no ', ['crit', 'crit']),
  },
}

/** Every in-game axis this module words — the list its exhaustiveness test reads. */
export const BUILT_AXES: readonly string[] = Object.keys(BUILT_AXIS_PHRASES)

/** How an in-game reason reads, or null for an axis this module does not word. */
export function builtAxisPhrase(axis: string, bucket: 'High' | 'Low'): ItemContextPhraseToken[] | null {
  return BUILT_AXIS_PHRASES[axis]?.[bucket === 'High' ? 'high' : 'low'] ?? null
}
