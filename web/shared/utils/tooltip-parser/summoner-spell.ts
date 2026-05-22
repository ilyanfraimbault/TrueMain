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
