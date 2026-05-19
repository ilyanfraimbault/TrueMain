import { walkHtmlToSegments } from './dom-walker'
import type { ParsedDocument } from './types'

/**
 * Map of raw hex colors emitted by CDragon rune descriptions to a semantic
 * tag name our renderer knows. The aim is to keep palette discipline — the
 * app is dark-mode-only with a constrained color set — so we mediate every
 * inline color through this table.
 *
 * Unknown hex codes fall back to `default` (no inline color, plain text).
 * The list grows empirically: add a new entry the first time a rune surfaces
 * an unseen color.
 */
const RUNE_HEX_TO_TAG: Record<string, string> = {
  '#48c4b7': 'runeadaptive',  // verified — Lethal Tempo adaptive damage
  '#ff9966': 'runead',
  '#ff9900': 'runead',
  '#ffaa00': 'runead',
  '#9999ff': 'runeap',
  '#7eb3ff': 'runeap',
  '#90ee90': 'runehealth',
  '#5bc489': 'runehealth',
  '#ff4444': 'runehealth',
  '#ffff00': 'runearmor',
  '#f2c94c': 'runearmor',
  '#b597f5': 'runemr',
  '#ffffff': 'default',
}

const FONT_TAG_RE = /<font\s+color\s*=\s*['"]?(#[0-9a-fA-F]{3,8})['"]?\s*>([\s\S]*?)<\/font>/gi
const KEYWORD_TAG_RE = /<lol-uikit-tooltipped-keyword[^>]*>([\s\S]*?)<\/lol-uikit-tooltipped-keyword>/gi
const MELEE_RANGED_RE = /\[\s*([^[\]|]+?)\s+Melee\s*\|\|\s*([^[\]|]+?)\s+Ranged\s*\]/gi

/**
 * Parse a CDragon rune description (either `shortDesc` or `longDesc`).
 *
 * CDragon emits rune text in a different shape than DDragon items:
 *   - inline colors via `<font color='#HEX'>` (not semantic tags)
 *   - keyword phrases via `<lol-uikit-tooltipped-keyword key='...'>`
 *   - melee/ranged variants via `[X Melee || Y Ranged]` literal syntax
 *
 * We pre-pass over the raw string to rewrite each of these into something the
 * shared DOM walker understands, then run the same walker as items/spells.
 */
export function parseRuneDescription(description: string): ParsedDocument {
  if (!description) return []
  const prepared = prepareRuneHtml(description)
  return walkHtmlToSegments(prepared)
}

/**
 * Apply the three pre-passes that bring a rune description into shape the
 * shared DOM walker can consume. Exported only for unit testing — production
 * code should call `parseRuneDescription`.
 */
export function prepareRuneHtml(description: string): string {
  let prepared = description

  // 1. Replace `<font color='#HEX'>X</font>` with `<TAG>X</TAG>` where TAG is
  //    a known semantic tag, or strip the wrapper if the hex is unknown.
  prepared = prepared.replace(FONT_TAG_RE, (_, hex: string, inner: string) => {
    const tag = RUNE_HEX_TO_TAG[hex.toLowerCase()]
    if (!tag || tag === 'default') return inner
    return `<${tag}>${inner}</${tag}>`
  })

  // 2. Replace `<lol-uikit-tooltipped-keyword key='...'>X</lol-uikit-...>`
  //    with `<runekeyword>X</runekeyword>`. We drop the `key=` attribute —
  //    resolving it would need a localization table we don't have.
  prepared = prepared.replace(KEYWORD_TAG_RE, (_, inner: string) => `<runekeyword>${inner}</runekeyword>`)

  // 3. Replace `[X Melee || Y Ranged]` with a synthetic `<rng>` marker the
  //    walker turns into a `meleeRanged` segment. Quote-escape the payload
  //    so it survives DOMParser without re-encoding surprises.
  prepared = prepared.replace(MELEE_RANGED_RE, (_, melee: string, ranged: string) => {
    return `<rng melee="${escapeAttr(melee.trim())}" ranged="${escapeAttr(ranged.trim())}"></rng>`
  })

  return prepared
}

function escapeAttr(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}
