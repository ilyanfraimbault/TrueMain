import type { ParsedDocument, ParsedSegment } from './types'

/**
 * Shared DOM walker used by item, champion-spell, and summoner-spell
 * parsers (and by the rune parser after its three pre-passes). The input is
 * an HTML fragment; the walker parses it via `DOMParser`, DFS-flattens nested
 * tags into a `ParsedSegment[]`, and treats `<br>` as a structural break.
 *
 * The walker is intentionally permissive: any unrecognised element name
 * becomes a text segment with that tag — the tag-class lookup falls open to
 * `default`, so the user always sees the text content.
 *
 * Pure logic — no Vue, no game-data knowledge. Lives in shared/ so it can be
 * unit-tested with `happy-dom` providing `DOMParser`.
 */
export function walkHtmlToSegments(html: string): ParsedDocument {
  if (!html) return []

  const doc = new DOMParser().parseFromString(`<root>${html}</root>`, 'text/html')
  const root = doc.querySelector('root')
  if (!root) return []

  const segments: ParsedSegment[] = []
  walkNode(root, 'default', segments)
  return collapseAdjacentBreaks(segments)
}

function walkNode(node: Node, inheritedTag: string, out: ParsedSegment[]): void {
  for (const child of Array.from(node.childNodes)) {
    if (child.nodeType === 3 /* TEXT_NODE */) {
      const text = child.textContent ?? ''
      if (text.length === 0) continue
      out.push({ kind: 'text', tag: inheritedTag, text })
      continue
    }

    if (child.nodeType !== 1 /* ELEMENT_NODE */) continue

    const element = child as Element
    const elementTag = element.tagName.toLowerCase()

    if (elementTag === 'br') {
      out.push({ kind: 'break' })
      continue
    }

    // For the synthetic `<rng>` marker injected by the rune parser to carry
    // [X Melee || Y Ranged] payloads (see rune.ts for the pre-pass).
    if (elementTag === 'rng') {
      out.push({
        kind: 'meleeRanged',
        melee: element.getAttribute('melee') ?? '',
        ranged: element.getAttribute('ranged') ?? '',
      })
      continue
    }

    // Wrappers without their own styling (e.g. `<mainText>`) — descend with
    // the inherited tag so child text doesn't accidentally pick up a class
    // it shouldn't have.
    if (elementTag === 'maintext') {
      walkNode(element, inheritedTag, out)
      continue
    }

    walkNode(element, elementTag, out)
  }
}

/**
 * `<br><br>` in DDragon item descriptions is the paragraph separator. Two
 * adjacent break segments stay (renderer handles spacing); three or more
 * collapse to two. Single breaks pass through. This keeps spacing visually
 * matching the in-game tooltip where the same convention is used.
 */
function collapseAdjacentBreaks(segments: ParsedDocument): ParsedDocument {
  const result: ParsedSegment[] = []
  let consecutiveBreaks = 0
  for (const segment of segments) {
    if (segment.kind === 'break') {
      consecutiveBreaks++
      if (consecutiveBreaks <= 2) result.push(segment)
      continue
    }
    consecutiveBreaks = 0
    result.push(segment)
  }
  return result
}
