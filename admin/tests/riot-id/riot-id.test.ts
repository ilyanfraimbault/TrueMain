import { describe, expect, it } from 'vitest'
import {
  RIOT_ID_MAX_LENGTH,
  formatRiotId,
  isRiotIdOrSlug,
  parseRiotId,
  parseRiotIdOrSlug,
  riotIdError,
  splitRiotId,
} from '~~/shared/utils/riot-id'

// The point of this suite is the CONTRACT, not the implementation: every case
// below is one the backend's NameTagParser answers the same way, so a drift in
// either direction shows up here rather than as a 400 the operator has to
// interpret.
describe('parseRiotId', () => {
  it('splits the typed form on the first hash and trims both halves', () => {
    expect(parseRiotId('  Phantasm # EUW1 ')).toEqual({ gameName: 'Phantasm', tagLine: 'EUW1' })
  })

  it('keeps hyphens inside the game name', () => {
    expect(parseRiotId('Sneaky-Bo#NA1')).toEqual({ gameName: 'Sneaky-Bo', tagLine: 'NA1' })
  })

  it('rejects a second hash instead of swallowing it into the tag', () => {
    expect(parseRiotId('Phantasm#EUW1#KR')).toBeNull()
    expect(riotIdError('Phantasm#EUW1#KR')).toBe('Tag line cannot contain a second "#"')
  })

  it('rejects a missing hash, an empty name and an empty tag', () => {
    expect(riotIdError('Phantasm')).toBe('Missing "#tagLine"')
    expect(riotIdError('#EUW1')).toBe('Missing game name')
    expect(riotIdError('Phantasm#')).toBe('Missing tag line')
  })

  it('accepts a Riot ID at the length cap and rejects the next character', () => {
    const atCap = `${'a'.repeat(RIOT_ID_MAX_LENGTH - 5)}#EUW1`
    expect(atCap).toHaveLength(RIOT_ID_MAX_LENGTH)
    expect(parseRiotId(atCap)).not.toBeNull()

    const pastCap = `${'a'.repeat(RIOT_ID_MAX_LENGTH - 4)}#EUW1`
    expect(parseRiotId(pastCap)).toBeNull()
    expect(riotIdError(pastCap)).toBe(`Longer than ${RIOT_ID_MAX_LENGTH} characters`)
  })

  it('returns null for nullish and blank input', () => {
    expect(parseRiotId(null)).toBeNull()
    expect(parseRiotId(undefined)).toBeNull()
    expect(parseRiotId('   ')).toBeNull()
  })
})

describe('splitRiotId', () => {
  // The preview table shows the halves of a malformed line too, so the split
  // has to survive input the verdict rejects.
  it('yields best-effort halves even when the line is invalid', () => {
    expect(splitRiotId('Phantasm')).toEqual({ gameName: 'Phantasm', tagLine: '' })
    expect(splitRiotId('#EUW1')).toEqual({ gameName: '', tagLine: 'EUW1' })
    expect(splitRiotId('a#b#c')).toEqual({ gameName: 'a', tagLine: 'b#c' })
  })
})

describe('parseRiotIdOrSlug', () => {
  it('accepts the typed form', () => {
    expect(parseRiotIdOrSlug('Phantasm#EUW1')).toEqual({ gameName: 'Phantasm', tagLine: 'EUW1' })
  })

  it('falls back to the hyphen slug on the LAST hyphen, so names keep theirs', () => {
    expect(parseRiotIdOrSlug('Sneaky-Bo-NA1')).toEqual({ gameName: 'Sneaky-Bo', tagLine: 'NA1' })
  })

  it('rejects a slug with an empty half', () => {
    expect(parseRiotIdOrSlug('-EUW1')).toBeNull()
    expect(parseRiotIdOrSlug('Phantasm-')).toBeNull()
    expect(parseRiotIdOrSlug('Phantasm')).toBeNull()
  })

  it('applies the length cap to the slug form as well', () => {
    expect(parseRiotIdOrSlug(`${'a'.repeat(RIOT_ID_MAX_LENGTH)}-EUW1`)).toBeNull()
  })

  it('still refuses a second hash', () => {
    expect(isRiotIdOrSlug('Phantasm#EUW1#KR')).toBe(false)
    expect(isRiotIdOrSlug('Phantasm#EUW1')).toBe(true)
  })
})

describe('formatRiotId', () => {
  it('renders the typed form, and null when a half is missing', () => {
    expect(formatRiotId('Phantasm', 'EUW1')).toBe('Phantasm#EUW1')
    expect(formatRiotId('Phantasm', null)).toBeNull()
    expect(formatRiotId('', 'EUW1')).toBeNull()
  })
})
