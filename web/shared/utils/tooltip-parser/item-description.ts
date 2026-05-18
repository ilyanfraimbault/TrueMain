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

// Ordered most-specific to least-specific. Each line includes the in-game
// hex (color-picked from the live client on 2026-05-18) for reviewer context.
const STAT_PATTERNS: ReadonlyArray<readonly [RegExp, string]> = [
  // ── Magic family — checked before AP/Armor so multi-word stats win
  // Magic Resist (#54e6ff) — must precede the generic Magic pattern
  [/^\s*(?:Magic Resist|MR\b)/i, 'attentionmr'],
  // Magic Penetration (#cc6efc) — distinct from MR and AP
  [/^\s*(?:Magic Penetration|Magic Pen\b|Magic Resist Reduction)/i, 'attentionmagicpen'],
  // Ability Power (#7e78ff)
  [/^\s*(?:Ability Power|AP\b)/i, 'attentionap'],

  // ── Physical family
  // Lethality / Armor Penetration (#f65e57) — separate from raw AD
  [/^\s*(?:Lethality|Armor Penetration|Armor Pen\b)/i, 'attentionlethality'],
  // Attack Damage (#f19425)
  [/^\s*(?:Attack Damage|AD\b|Bonus Attack Damage)/i, 'attentionad'],
  // Attack Speed (#ffe991) — distinct from Move Speed
  [/^\s*Attack Speed/i, 'attentionas'],

  // ── Defensive
  // Tenacity / Slow Resist (#8c72ff)
  [/^\s*(?:Tenacity|Slow Resist)/i, 'attentiontenacity'],
  // Armor (#f3c057) — bare word, after Lethality/ArmorPen above
  [/^\s*Armor\b/i, 'attentionarmor'],
  // Heal and Shield Power (#6be695) — own bucket
  [/^\s*Heal and Shield Power/i, 'attentionhsp'],
  // Heal Reduction / Grievous Wounds (#8d5874)
  [/^\s*(?:Heal(?:ing)? Reduction|Grievous Wounds|Reduced Healing)/i, 'attentionhealreduction'],

  // ── Sustain offensif — must precede flat-health green
  // Lifesteal / Omnivamp / Physical Vamp (#d70045)
  [/^\s*(?:Life ?[Ss]teal|Omnivamp|Physical Vamp)/i, 'attentionvamp'],
  // Flat health / HP regen (#24a564)
  [/^\s*(?:Health(?: Regen(?:eration)?)?|HP\b)/i, 'attentionhealth'],

  // ── Resources / utility
  // Ability Haste (#ede2cf) — beige, its own bucket
  [/^\s*(?:Ability Haste|AH\b)/i, 'attentionhaste'],
  // Move Speed / On-Hit (#00a6ed)
  [/^\s*(?:Move(?:ment)? Speed|On-Hit)/i, 'attentionspeed'],
  // Mana (#00a6ed)
  [/^\s*(?:Mana(?: Regen(?:eration)?)?)\b/i, 'attentionmana'],

  // ── Crit
  [/^\s*(?:Critical Strike(?: Chance| Damage)?|Crit Chance|Crit Damage)/i, 'attentioncrit'],

  // ── Shields
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
