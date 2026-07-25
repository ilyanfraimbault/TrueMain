import { describe, expect, it } from 'vitest'
import { RIOT_ID_MAX_LENGTH, formatRiotId, isValidRiotId, parseRiotId } from '~~/app/utils/riot-id'

// Mirror of the backend's NameTagParserTests.TryParseRiotId cases: the panel
// only submits what the API can parse, so both sides must agree on what a
// well-formed Riot ID is.
describe('parseRiotId', () => {
  it('splits on the first #', () => {
    expect(parseRiotId('Phantasm#EUW1')).toEqual({ gameName: 'Phantasm', tagLine: 'EUW1' })
    expect(parseRiotId('  Phantasm#EUW1  ')).toEqual({ gameName: 'Phantasm', tagLine: 'EUW1' })
    expect(parseRiotId('GXI Flakked #EUW')).toEqual({ gameName: 'GXI Flakked', tagLine: 'EUW' })
  })

  it('keeps hyphens inside the game name', () => {
    expect(parseRiotId('Some-Player-Name#NA1'))
      .toEqual({ gameName: 'Some-Player-Name', tagLine: 'NA1' })
  })

  it('rejects anything the API would 400 on', () => {
    expect(parseRiotId(null)).toBeNull()
    expect(parseRiotId('')).toBeNull()
    expect(parseRiotId('   ')).toBeNull()
    // The slug form is valid on the API but not what the panel asks for — the
    // input's hint says Name#TAG, so the '#' is required here.
    expect(parseRiotId('Phantasm-EUW1')).toBeNull()
    expect(parseRiotId('#EUW1')).toBeNull()
    expect(parseRiotId('Phantasm#')).toBeNull()
    expect(parseRiotId('Phantasm#EUW#1')).toBeNull()
  })

  it('rejects input past the length cap', () => {
    // Pins the value against NameTagParser.MaxRiotIdLength on the backend: a
    // drift lets the panel submit something the API 400s on.
    expect(RIOT_ID_MAX_LENGTH).toBe(64)

    const atCap = `${'a'.repeat(RIOT_ID_MAX_LENGTH - 5)}#EUW1`
    expect(atCap).toHaveLength(RIOT_ID_MAX_LENGTH)
    expect(parseRiotId(atCap)).not.toBeNull()

    expect(parseRiotId(`${'a'.repeat(RIOT_ID_MAX_LENGTH - 4)}#EUW1`)).toBeNull()
  })

  it('drives the submit guard', () => {
    expect(isValidRiotId('Phantasm#EUW1')).toBe(true)
    expect(isValidRiotId('Phantasm')).toBe(false)
  })
})

describe('formatRiotId', () => {
  it('renders the typed form', () => {
    expect(formatRiotId('Phantasm', 'EUW1')).toBe('Phantasm#EUW1')
  })

  it('yields null for an identity that has no tag line', () => {
    expect(formatRiotId('Phantasm', null)).toBeNull()
    expect(formatRiotId('', 'EUW1')).toBeNull()
  })
})
