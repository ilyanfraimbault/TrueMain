import { describe, expect, it } from 'vitest'
import { firstParamValue, parseRouteParam } from '#common/utils/route-params'

// Vue Router hands path params and query values over as `string | string[]`
// (repeated query keys and catch-all params arrive as arrays) and query values
// may be `null`. Both helpers collapse that to the first string, differing only
// in their "missing" sentinel (#690).

describe('firstParamValue', () => {
  it.each([
    ['a plain string', 'euw', 'euw'],
    ['the first entry of an array', ['euw', 'na'], 'euw'],
    ['an empty string as-is', '', ''],
  ])('returns %s', (_label, value, expected) => {
    expect(firstParamValue(value)).toBe(expected)
  })

  it.each([
    ['undefined', undefined],
    ['null (a valueless query key)', null],
    ['an empty array', []],
    ['an array led by null', [null, 'euw']],
    ['a number', 42],
    ['an object', { region: 'euw' }],
  ])('returns undefined for %s', (_label, value) => {
    expect(firstParamValue(value)).toBeUndefined()
  })
})

describe('parseRouteParam', () => {
  it('returns the string param untouched', () => {
    expect(parseRouteParam('Faker-KR1')).toBe('Faker-KR1')
  })

  it('returns the first entry of a catch-all array', () => {
    expect(parseRouteParam(['Faker-KR1', 'extra'])).toBe('Faker-KR1')
  })

  it('falls back to the empty string when the param is missing', () => {
    expect(parseRouteParam(undefined)).toBe('')
    expect(parseRouteParam([])).toBe('')
  })
})
