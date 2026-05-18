import { walkHtmlToSegments } from './dom-walker'
import type { ParsedDocument, ParsedSegment } from './types'

/**
 * Parse a DDragon item description (the `description` field on each
 * `item.json` entry). Input is an HTML fragment wrapped in `<mainText>`
 * containing `<stats>`, `<passive>`, `<active>`, scaling tags, etc. Output is
 * a flat `ParsedDocument` ready for the renderer.
 *
 * After the generic walk, each `<attention>` value gets a more specific tag
 * based on the stat label that follows it ("Attack Damage" → `attentionad`,
 * "Health" → `attentionhealth`, ...). DDragon wraps every numeric callout in
 * the same `<attention>` tag regardless of stat type, but the in-game
 * tooltip colors each value to match its stat. Mirroring that requires a
 * tiny look-ahead pass on the parsed segments.
 */
export function parseItemDescription(description: string): ParsedDocument {
  return recolorAttentionByStatLabel(walkHtmlToSegments(description))
}

const STAT_PATTERNS: ReadonlyArray<readonly [RegExp, string]> = [
  // AD bucket — numbers paired with "Attack Damage" / "Lethality" / "Armor Penetration" read like AD
  [/^\s*(?:Attack Damage|AD\b|Lethality|Armor Penetration|Bonus Attack Damage)/i, 'attentionad'],
  // AP bucket — magic-damage scaling stats land here, including Ability Haste (cyan in-game)
  [/^\s*(?:Ability Power|AP\b|Magic Penetration|Ability Haste|Magic Resist Reduction)/i, 'attentionap'],
  // MR before generic "Magic" so "Magic Resist" doesn't match the AP pattern
  [/^\s*(?:Magic Resist|MR\b|Tenacity|Slow Resist)/i, 'attentionmr'],
  [/^\s*(?:Armor)\b/i, 'attentionarmor'],
  // Vamp / lifesteal / sustain — pink, distinct from flat-health green.
  // Checked BEFORE the health bucket so "Life Steal" doesn't fall into health.
  [/^\s*(?:Life Steal|Omnivamp|Heal and Shield Power|Physical Vamp)/i, 'attentionvamp'],
  // Flat health / HP regen — green
  [/^\s*(?:Health(?: Regen(?:eration)?)?|HP\b)/i, 'attentionhealth'],
  // Movement / attack speed / on-hit utility
  [/^\s*(?:Move(?:ment)? Speed|Attack Speed|On-Hit)/i, 'attentionspeed'],
  // Resources
  [/^\s*(?:Mana(?: Regen(?:eration)?)?)\b/i, 'attentionmana'],
  // Crit
  [/^\s*(?:Critical Strike(?: Chance)?|Crit Chance)/i, 'attentioncrit'],
  // Shields
  [/^\s*Shield/i, 'attentionshield'],
]

function recolorAttentionByStatLabel(doc: ParsedDocument): ParsedDocument {
  return doc.map((seg, i) => {
    if (seg.kind !== 'text' || seg.tag.toLowerCase() !== 'attention') return seg
    const labelTag = lookaheadStatTag(doc, i + 1)
    if (!labelTag) return seg
    return { ...seg, tag: labelTag }
  })
}

function lookaheadStatTag(doc: ParsedDocument, startIndex: number): string | null {
  for (let j = startIndex; j < doc.length; j++) {
    const next = doc[j]
    if (!next) return null
    if (next.kind === 'break') return null
    if (next.kind === 'meleeRanged') return null
    // Skip nested `<attention>` segments (e.g. "10% Armor by 6%" wouldn't apply
    // — but in practice DDragon emits siblings, not nested attention).
    if (next.tag.toLowerCase() === 'attention') continue
    for (const [pattern, tag] of STAT_PATTERNS) {
      if (pattern.test(next.text)) return tag
    }
    return null
  }
  return null
}

export { recolorAttentionByStatLabel } // exported for unit tests
export type { ParsedSegment }
