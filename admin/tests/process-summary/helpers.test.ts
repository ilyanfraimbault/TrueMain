import { describe, expect, it } from 'vitest'
import { formatScalar, humanizeKey, isPlainObject, isScalar } from '~~/shared/utils/process-summary'

describe('humanizeKey', () => {
  it('splits camelCase into words', () => {
    expect(humanizeKey('matchesIngested')).toBe('Matches Ingested')
    expect(humanizeKey('queuedCandidatesTotal')).toBe('Queued Candidates Total')
  })

  it('splits on a digit → uppercase boundary', () => {
    expect(humanizeKey('top5Champions')).toBe('Top5 Champions')
  })

  it('turns underscores and dashes into spaces', () => {
    expect(humanizeKey('matches_ingested')).toBe('Matches ingested')
    expect(humanizeKey('matches-ingested')).toBe('Matches ingested')
    expect(humanizeKey('matches__ingested')).toBe('Matches ingested')
  })

  it('capitalizes the first character', () => {
    expect(humanizeKey('platform')).toBe('Platform')
    expect(humanizeKey('Platform')).toBe('Platform')
  })

  it('trims the whitespace surrounding separators leave behind', () => {
    // A leading separator becomes the (already-uppercase) first character, so a
    // key like `_platform_` keeps its lowercase `p` — documented, not desired.
    expect(humanizeKey('_platform_')).toBe('platform')
    expect(humanizeKey('platform_')).toBe('Platform')
  })

  it('leaves an empty key empty', () => {
    expect(humanizeKey('')).toBe('')
  })
})

describe('isScalar', () => {
  it('accepts primitives', () => {
    expect(isScalar('EUW1')).toBe(true)
    expect(isScalar(12)).toBe(true)
    expect(isScalar(0)).toBe(true)
    expect(isScalar(true)).toBe(true)
    expect(isScalar('')).toBe(true)
  })

  it('accepts null and undefined (rendered as an em dash)', () => {
    expect(isScalar(null)).toBe(true)
    expect(isScalar(undefined)).toBe(true)
  })

  it('rejects objects and arrays', () => {
    expect(isScalar({})).toBe(false)
    expect(isScalar({ a: 1 })).toBe(false)
    expect(isScalar([])).toBe(false)
    expect(isScalar([1, 2])).toBe(false)
  })

  it('rejects exotic objects, which are not renderable as one line', () => {
    expect(isScalar(new Date())).toBe(false)
    expect(isScalar(new Map())).toBe(false)
    expect(isScalar(new Set())).toBe(false)
  })
})

describe('isPlainObject', () => {
  it('accepts object literals', () => {
    expect(isPlainObject({})).toBe(true)
    expect(isPlainObject({ platform: 'EUW1' })).toBe(true)
  })

  it('accepts an object parsed from JSON', () => {
    expect(isPlainObject(JSON.parse('{"platform":"EUW1"}'))).toBe(true)
  })

  it('rejects arrays', () => {
    expect(isPlainObject([])).toBe(false)
    expect(isPlainObject([{ platform: 'EUW1' }])).toBe(false)
  })

  it('rejects null, undefined and primitives', () => {
    expect(isPlainObject(null)).toBe(false)
    expect(isPlainObject(undefined)).toBe(false)
    expect(isPlainObject('EUW1')).toBe(false)
    expect(isPlainObject(12)).toBe(false)
    expect(isPlainObject(true)).toBe(false)
  })

  it('rejects Date, Map and Set', () => {
    expect(isPlainObject(new Date())).toBe(false)
    expect(isPlainObject(new Map([['a', 1]]))).toBe(false)
    expect(isPlainObject(new Set([1]))).toBe(false)
  })

  it('rejects other exotic objects (RegExp, class instances, null-prototype)', () => {
    expect(isPlainObject(/x/)).toBe(false)
    expect(isPlainObject(new (class Row { platform = 'EUW1' })())).toBe(false)
    expect(isPlainObject(Object.create(null))).toBe(false)
  })
})

describe('formatScalar', () => {
  it('renders absent values as an em dash', () => {
    expect(formatScalar(null)).toBe('—')
    expect(formatScalar(undefined)).toBe('—')
  })

  it('groups numbers with the shared number formatter', () => {
    expect(formatScalar(1234567)).toBe('1,234,567')
    expect(formatScalar(0)).toBe('0')
    expect(formatScalar(-42)).toBe('-42')
    expect(formatScalar(12.5)).toBe('12.5')
  })

  it('renders non-finite numbers as an em dash, like every other metric', () => {
    expect(formatScalar(Number.NaN)).toBe('—')
    expect(formatScalar(Number.POSITIVE_INFINITY)).toBe('—')
  })

  it('renders booleans as Yes/No', () => {
    expect(formatScalar(true)).toBe('Yes')
    expect(formatScalar(false)).toBe('No')
  })

  it('passes strings through', () => {
    expect(formatScalar('EUW1')).toBe('EUW1')
    expect(formatScalar('')).toBe('')
  })
})
