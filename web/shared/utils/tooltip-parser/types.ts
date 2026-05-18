/**
 * Output shape of every tooltip parser. The renderer walks this array and
 * emits one node per segment.
 *
 * The walker relies on `DOMParser`, so parsing only runs where a DOM is
 * available — i.e. in the browser at hover time, or in tests via
 * `happy-dom`. We never parse server-side (descriptions are streamed as
 * raw strings through the Nitro endpoints). If a future code path needs
 * server-side parsing, swap the walker for a DOM-less tokenizer or pull
 * in `linkedom` rather than ship `jsdom`.
 */
export type ParsedSegment =
  | { kind: 'text', tag: string, text: string }
  | { kind: 'break' }
  | { kind: 'meleeRanged', melee: string, ranged: string }

export type ParsedDocument = ParsedSegment[]
