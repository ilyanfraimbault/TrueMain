// Twin of `web/shared/utils/tooltip-parser/summoner-spell.ts` — copied verbatim until the shared layer (#1687); keep the two identical.
import { walkHtmlToSegments } from './dom-walker'
import type { ParsedDocument } from './types'

/**
 * Parse a DDragon summoner-spell description (`summoner.description`).
 * Almost always plain text, occasionally with `<br>` separators. We reuse the
 * shared walker so a future tag in the data flows through automatically.
 */
export function parseSummonerSpell(description: string): ParsedDocument {
  return walkHtmlToSegments(description)
}
