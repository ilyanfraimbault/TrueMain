/**
 * Output shape of every tooltip parser. The renderer walks this array and
 * emits one node per segment; parsers stay pure (no Vue, no DOM-on-server)
 * and the renderer stays free of game-data knowledge.
 */
export type ParsedSegment =
  | { kind: 'text', tag: string, text: string }
  | { kind: 'break' }
  | { kind: 'meleeRanged', melee: string, ranged: string }

export type ParsedDocument = ParsedSegment[]
