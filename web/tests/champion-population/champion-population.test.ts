import { describe, expect, it } from 'vitest'
import {
  EVERYONE_QUERY_PARAM,
  EVERYONE_QUERY_VALUE,
  resolveTruemainsOnly,
} from '~~/app/utils/champion-population'

describe('resolveTruemainsOnly', () => {
  it('is on when the param is absent', () => {
    for (const raw of [null, undefined, '']) {
      expect(resolveTruemainsOnly(raw, undefined)).toBe(true)
    }
  })

  it('is off only for the exact opt-out value', () => {
    expect(resolveTruemainsOnly(EVERYONE_QUERY_VALUE, undefined)).toBe(false)
  })

  it('stays on for anything else the param could carry', () => {
    // The opt-out is the narrow case; a junk value must not silently widen the
    // page to a population its header does not claim.
    for (const raw of ['0', 'true', 'yes', 'everyone', '11', ' 1']) {
      expect(resolveTruemainsOnly(raw, undefined)).toBe(true)
    }
  })

  it('is forced on by a pinned matchup, whatever the param says', () => {
    // The rule this test exists for: matchups are folded from a mains-only
    // aggregate, and the API 400s `?truemainsOnly=false&opponentChampionId=`.
    // Resolving the pair away here is what keeps a shared `?vs=…&everyone=1`
    // rendering instead of erroring.
    expect(resolveTruemainsOnly(EVERYONE_QUERY_VALUE, 122)).toBe(true)
    expect(resolveTruemainsOnly(null, 122)).toBe(true)
  })

  it('honours the opt-out again once the opponent is cleared', () => {
    expect(resolveTruemainsOnly(EVERYONE_QUERY_VALUE, 122)).toBe(true)
    expect(resolveTruemainsOnly(EVERYONE_QUERY_VALUE, undefined)).toBe(false)
  })

  it('names the param the composable writes', () => {
    expect(EVERYONE_QUERY_PARAM).toBe('everyone')
    expect(EVERYONE_QUERY_VALUE).toBe('1')
  })
})
