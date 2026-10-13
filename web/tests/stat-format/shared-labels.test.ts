import { describe, expect, it } from 'vitest'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { compareByPosition, positionIndex, positionLabel } from '#common/utils/positions'
import { formatDuration } from '#common/utils/relativeTime'

describe('championDisplayName', () => {
  it('reads the static entry or a resolved name', () => {
    expect(championDisplayName(103, { name: 'Ahri' })).toBe('Ahri')
    expect(championDisplayName(103, 'Ahri')).toBe('Ahri')
  })

  it('falls back to one stand-in while the name is unknown', () => {
    expect(championDisplayName(950)).toBe('Champion 950')
    expect(championDisplayName(950, null)).toBe('Champion 950')
    expect(championDisplayName(950, '')).toBe('Champion 950')
  })
})

describe('positionLabel', () => {
  it('names a position, in full or short', () => {
    expect(positionLabel('MIDDLE')).toBe('Middle')
    expect(positionLabel('BOTTOM', { short: true })).toBe('Bot')
    expect(positionLabel('UTILITY', { short: true })).toBe('Support')
  })

  it('is null for what is no position', () => {
    expect(positionLabel('')).toBeNull()
    expect(positionLabel(null)).toBeNull()
    expect(positionLabel('NONE')).toBeNull()
  })
})

describe('position order', () => {
  it('sorts top to support, unknown positions last', () => {
    expect(positionIndex('TOP')).toBe(0)
    expect(positionIndex('UTILITY')).toBe(4)
    expect(positionIndex('NONE')).toBe(5)
    const sorted = [{ position: 'UTILITY' }, { position: null }, { position: 'TOP' }].sort(compareByPosition)
    expect(sorted.map(p => p.position)).toEqual(['TOP', 'UTILITY', null])
  })
})

describe('formatDuration', () => {
  it('writes m:ss and keeps counting minutes past the hour', () => {
    expect(formatDuration(5)).toBe('0:05')
    expect(formatDuration(1854.7)).toBe('30:54')
    expect(formatDuration(3852)).toBe('64:12')
  })

  it('writes h:mm:ss past the hour on request', () => {
    expect(formatDuration(3852, { hours: true })).toBe('1:04:12')
    expect(formatDuration(1854, { hours: true })).toBe('30:54')
  })

  it('never goes negative', () => {
    expect(formatDuration(-3)).toBe('0:00')
  })
})
