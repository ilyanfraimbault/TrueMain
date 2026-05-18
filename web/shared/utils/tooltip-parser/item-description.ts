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
  return ensureParagraphBeforeLabels(
    retagStealthKeywords(recolorAttentionByStatLabel(walkHtmlToSegments(description))),
  )
}

const STEALTH_KEYWORDS = /^\s*(?:Invisible|Camouflage|Stealth(?:ed)?|Obscured)\s*$/i

/**
 * DDragon uses a single `<keyword>` tag for both CC labels ("Slowing",
 * "Immobilizing") and stealth labels ("Invisible", "Camouflage"). In-game
 * they're distinct colors. Retag stealth-family text to `keywordstealth`
 * so the renderer can route it to the dedicated `--color-stat-stealth`
 * token.
 */
function retagStealthKeywords(doc: ParsedDocument): ParsedDocument {
  return doc.map((seg) => {
    if (seg.kind !== 'text' || seg.tag.toLowerCase() !== 'keyword') return seg
    if (!STEALTH_KEYWORDS.test(seg.text)) return seg
    return { ...seg, tag: 'keywordstealth' }
  })
}

const PARAGRAPH_LABEL_TAGS = new Set(['passive', 'active'])

/**
 * Force a paragraph break (two `<br>`) before every `<passive>` / `<active>`
 * label that appears mid-prose. DDragon sometimes inlines `<active>X</active>`
 * inside the same sentence as the preceding passive description (cf.
 * Solstice Sleigh: "...nearby ally. <active>Active</active> (4 charges)..."),
 * which reads as a wall of text. The in-game tooltip renders it as a fresh
 * paragraph; this pass keeps the same visual rhythm.
 */
function ensureParagraphBeforeLabels(doc: ParsedDocument): ParsedDocument {
  const result: ParsedSegment[] = []
  // Track each label name we've already emitted. DDragon often mentions an
  // earlier passive by name inside a later passive's prose (cf. Blackfire
  // Torch: "Blackfire ... affected by your <passive>Baleful Blaze</passive>")
  // — those second mentions are *references*, not fresh labels, and must NOT
  // trigger a paragraph break in the middle of a sentence.
  const seenLabels = new Set<string>()
  for (const seg of doc) {
    if (seg.kind === 'text' && PARAGRAPH_LABEL_TAGS.has(seg.tag.toLowerCase())) {
      const labelKey = `${seg.tag.toLowerCase()}:${seg.text.trim().toLowerCase()}`
      const isReference = seenLabels.has(labelKey)
      seenLabels.add(labelKey)
      if (result.length > 0 && !isReference && !isFlattenContinuation(result, seg.tag.toLowerCase())) {
        // Want exactly two consecutive breaks before a label that follows
        // earlier content. Items vary in how they separate the previous
        // block: Solstice Sleigh has zero (inline), Celestial Opposition
        // has one `<br>`, items with their own paragraph already have two.
        // Top up to two regardless. Skip entirely when the label is the
        // very first segment — no preceding content means no separator.
        const trailingBreaks = countTrailingBreaks(result)
        for (let i = trailingBreaks; i < 2; i++) result.push({ kind: 'break' })
      }
    }
    result.push(seg)
  }
  return result
}

function countTrailingBreaks(segments: ParsedDocument): number {
  let count = 0
  for (let i = segments.length - 1; i >= 0; i--) {
    if (segments[i]?.kind === 'break') count++
    else break
  }
  return count
}

/**
 * Walk back through the already-emitted segments (skipping nothing, stopping
 * at the first break) and return true when we find another segment with the
 * same label tag. That means we're in the flattened tail of the same
 * `<passive>` / `<active>` DOM element — not a fresh label — and we must not
 * insert a paragraph break in the middle of it.
 */
function isFlattenContinuation(result: ParsedSegment[], tag: string): boolean {
  for (let i = result.length - 1; i >= 0; i--) {
    const s = result[i]
    if (!s) return false
    if (s.kind === 'break') return false
    if (s.kind === 'text' && s.tag.toLowerCase() === tag) return true
  }
  return false
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
  // Flat health / HP regen — also matches "Base Health Regen" (#24a564)
  [/^\s*(?:Base )?(?:Health(?: Regen(?:eration)?)?|HP(?: Regen(?:eration)?)?)\b/i, 'attentionhealth'],

  // ── Resources / utility
  // Haste family (#ede2cf) — Ability Haste, Item Haste, Summoner Spell Haste
  // all reduce cooldowns and share the same beige bucket in the client.
  [/^\s*(?:Ability Haste|Item Haste|Summoner(?: Spell)? Haste|Haste\b|AH\b)/i, 'attentionhaste'],
  // Move Speed / On-Hit (#ffffff)
  [/^\s*(?:Move(?:ment)? Speed|On-Hit)/i, 'attentionspeed'],
  // Mana — also matches "Base Mana Regen" (#00a6ed)
  [/^\s*(?:Base )?(?:Mana(?: Regen(?:eration)?)?)\b/i, 'attentionmana'],
  // Gold per N seconds — support / world items (#c8aa6e)
  [/^\s*Gold per\b/i, 'attentiongold'],

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
