import { describe, expect, it } from 'vitest'
import { readChampionId } from '~~/server/utils/champion-slice-query'

describe('readChampionId', () => {
  it('accepts a plain positive integer', () => {
    expect(readChampionId('103')).toBe(103)
  })

  it('rejects everything that is not one, instead of coercing it', () => {
    // The value reaches us from a URL segment; `Number('')` is 0 and
    // `Number(' 12 ')` is 12, either of which would turn a hand-crafted URL
    // into an upstream call we never meant to make.
    for (const raw of ['', ' 12 ', '0', '-4', '1e3', '12.5', 'abc', '12345678', null, undefined]) {
      expect(readChampionId(raw)).toBeNull()
    }
  })
})
