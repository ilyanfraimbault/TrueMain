import { describe, expect, it } from 'vitest'
import { escapeChartTickText, escapeTickFormatter } from '~/utils/chart-text'

// Regression cover for #842 on the portal's copy of the helpers (#1404), which
// is kept in sync with `web/app/utils/chart-text.ts` — the same test, so the two
// cannot drift silently.
//
// `@unovis/ts` string-builds `<tspan>…</tspan>` from the tick text and parses it
// with `DOMParser` as `image/svg+xml`, so a label carrying `&`, `<` or `>` yields
// a `<parsererror>` document (silent in Chromium, an `XML Parsing Error` line in
// the Firefox console). The portal reaches this through champion names and any
// table or caller name that grows an angle bracket.

/** Mirrors the fragment @unovis/ts builds around each tick line. */
function unovisTspanFragment(line: string): string {
  return `<tspan xmlns="http://www.w3.org/2000/svg" font-size="12">`
    + `<tspan x="0" dy="0em" dominant-baseline="central">${line}</tspan></tspan>`
}

function parsesAsXml(fragment: string): boolean {
  const doc = new DOMParser().parseFromString(fragment, 'image/svg+xml')
  return doc.querySelector('parsererror') === null
}

describe('escapeChartTickText', () => {
  it('escapes the XML metacharacters that break the unovis tspan parse', () => {
    expect(escapeChartTickText('<20m')).toBe('&lt;20m')
    expect(escapeChartTickText('>35m')).toBe('&gt;35m')
    expect(escapeChartTickText('Nunu & Willump')).toBe('Nunu &amp; Willump')
  })

  it('escapes ampersands before angle brackets so entities are not doubled', () => {
    expect(escapeChartTickText('a & <b>')).toBe('a &amp; &lt;b&gt;')
    expect(escapeChartTickText('&amp;')).toBe('&amp;amp;')
  })

  it('leaves labels without metacharacters untouched', () => {
    for (const label of ['20-25m', '35m+', '16.14', '+1.5', '≤20m', '']) {
      expect(escapeChartTickText(label)).toBe(label)
    }
  })

  it('produces a well-formed tspan fragment for a label carrying an angle bracket', () => {
    expect(parsesAsXml(unovisTspanFragment('<20m'))).toBe(false)
    expect(parsesAsXml(unovisTspanFragment(escapeChartTickText('<20m')))).toBe(true)
  })

  it('round-trips a champion name carrying an ampersand', () => {
    // "Nunu & Willump" is the live case on /champions' category axis. It is
    // asserted by round-trip rather than by a failed parse: a bare `&` is
    // invalid XML, but jsdom's parser accepts it where a browser's does not, so
    // only the angle-bracket case above can carry the "this breaks" half.
    const doc = new DOMParser().parseFromString(
      unovisTspanFragment(escapeChartTickText('Nunu & Willump')),
      'image/svg+xml',
    )
    expect(doc.querySelector('parsererror')).toBeNull()
    expect(doc.documentElement.textContent).toBe('Nunu & Willump')
  })

  it('still renders the original glyphs once the XML is parsed', () => {
    const doc = new DOMParser().parseFromString(
      unovisTspanFragment(escapeChartTickText('<20m & >35m')),
      'image/svg+xml',
    )
    expect(doc.documentElement.textContent).toBe('<20m & >35m')
  })
})

describe('escapeTickFormatter', () => {
  it('escapes whatever the wrapped formatter returns', () => {
    const wrapped = escapeTickFormatter((tick: number) => `<${tick}m`)
    expect(wrapped?.(20 as never)).toBe('&lt;20m')
  })

  it('forwards the tick index and tick list', () => {
    const seen: unknown[] = []
    const wrapped = escapeTickFormatter(((tick: number, i?: number, ticks?: number[]) => {
      seen.push([tick, i, ticks])
      return 'ok'
    }))
    wrapped?.(1 as never, 2, [1, 2, 3] as never)
    expect(seen).toEqual([[1, 2, [1, 2, 3]]])
  })

  it('leaves an absent formatter absent', () => {
    expect(escapeTickFormatter(undefined)).toBeUndefined()
  })
})
