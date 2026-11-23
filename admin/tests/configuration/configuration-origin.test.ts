import { describe, expect, it } from 'vitest'
import { originLabel, originMeta } from '~/utils/configuration-origin'

describe('configuration origin', () => {
  it('names an override with the provider that supplied it', () => {
    expect(originLabel({ origin: 'override', source: 'environment' })).toBe('Override — environment')
    expect(originLabel({ origin: 'default', source: null })).toBe('Default')
  })

  it('reads an origin it does not know as its own name, uncoloured', () => {
    expect(originMeta('computed')).toEqual({ label: 'computed', color: 'neutral' })
    expect(originMeta('derived').color).toBe('warning')
  })
})
