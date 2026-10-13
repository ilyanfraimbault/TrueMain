import { describe, expect, it } from 'vitest'
import { formatKda, formatKdaRatio, kdaRatio } from '~~/shared/utils/kda'

describe('kdaRatio', () => {
  it('divides kills plus assists by deaths', () => {
    expect(kdaRatio({ kills: 4, deaths: 3, assists: 7 })).toBeCloseTo(11 / 3)
  })

  it('floors deaths at one, so a deathless sample is a large finite number', () => {
    expect(kdaRatio({ kills: 12, deaths: 0, assists: 7 })).toBe(19)
  })
})

describe('formatKdaRatio', () => {
  it('writes two decimals', () => {
    expect(formatKdaRatio(3.456)).toBe('3.46')
    expect(formatKdaRatio(2)).toBe('2.00')
  })
})

describe('formatKda', () => {
  it('labels a game line with its ratio', () => {
    expect(formatKda({ kills: 4, deaths: 3, assists: 7 })).toBe('3.67 KDA')
  })

  it('calls a deathless game Perfect', () => {
    expect(formatKda({ kills: 12, deaths: 0, assists: 7 })).toBe('Perfect')
    expect(formatKda({ kills: 0, deaths: 0, assists: 0 })).toBe('Perfect')
  })
})
