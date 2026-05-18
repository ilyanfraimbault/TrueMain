import { walkHtmlToSegments } from './dom-walker'
import type { ParsedDocument } from './types'

/**
 * Parse a DDragon item description (the `description` field on each
 * `item.json` entry). Input is an HTML fragment wrapped in `<mainText>`
 * containing `<stats>`, `<passive>`, `<active>`, scaling tags, etc. Output is
 * a flat `ParsedDocument` ready for the renderer.
 */
export function parseItemDescription(description: string): ParsedDocument {
  return walkHtmlToSegments(description)
}
