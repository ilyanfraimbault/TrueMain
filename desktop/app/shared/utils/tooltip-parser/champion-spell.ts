// Twin of `web/shared/utils/tooltip-parser/champion-spell.ts` — copied verbatim until the shared layer (#1687); keep the two identical.
import { walkHtmlToSegments } from './dom-walker'
import type { ParsedDocument } from './types'

/**
 * Parse a DDragon champion-ability description (`spell.description`). These
 * are mostly clean text with the occasional `<br>`, `<status>`, or
 * `<physicalDamage>` tag. The walker handles all of those by default.
 *
 * Note: DDragon also exposes `spell.tooltip`, which is templated with
 * `{{ vars }}` that need interpolation against `effect[]` per spell rank.
 * We intentionally ignore `tooltip` and use `description` only.
 */
export function parseChampionSpell(description: string): ParsedDocument {
  return walkHtmlToSegments(description)
}
