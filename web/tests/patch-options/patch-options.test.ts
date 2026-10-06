import { describe, expect, it } from 'vitest'
import * as vue from 'vue'

/**
 * `usePatchOptions` feeds every patch picker (#690): the recent DDragon
 * minors, plus pinned values so the select never renders blank for a patch
 * outside the recent list, sorted newest first.
 *
 * Same auto-import stand-in as `tests/visible-once/visible-once.test.ts`: the
 * composable relies on Nuxt's auto-imported Vue helpers, so they have to be
 * globals before the module is evaluated.
 */
Object.assign(globalThis, {
  computed: vue.computed,
  toValue: vue.toValue,
})

const { usePatchOptions } = await import('#common/composables/usePatchOptions')

const values = (options: { label: string, value: string }[]) => options.map(o => o.value)

describe('usePatchOptions', () => {
  it('dedupes full DDragon versions to major.minor', () => {
    const options = usePatchOptions(['15.13.1', '15.12.2', '15.12.1']).value
    expect(options).toEqual([
      { label: '15.13', value: '15.13' },
      { label: '15.12', value: '15.12' },
    ])
  })

  it('keeps the first 12 versions only', () => {
    const versions = Array.from({ length: 20 }, (_, i) => `15.${20 - i}.1`)
    const options = values(usePatchOptions(versions).value)
    expect(options).toHaveLength(12)
    expect(options[0]).toBe('15.20')
    expect(options.at(-1)).toBe('15.9')
  })

  it('sorts numerically, newest first', () => {
    const options = values(usePatchOptions(['15.9.1', '15.10.1', '14.24.1', '15.2.1']).value)
    expect(options).toEqual(['15.10', '15.9', '15.2', '14.24'])
  })

  it('adds pinned patches outside the recent list without duplicating listed ones', () => {
    const options = values(usePatchOptions(['15.13.1', '15.12.1'], '15.3', '15.13', null, undefined).value)
    expect(options).toEqual(['15.13', '15.12', '15.3'])
  })

  it('ignores empty pinned values', () => {
    expect(values(usePatchOptions(['15.13.1'], '').value)).toEqual(['15.13'])
  })

  it('returns only the pinned patches when versions are not loaded yet', () => {
    expect(values(usePatchOptions(null, '15.13').value)).toEqual(['15.13'])
    expect(usePatchOptions(undefined).value).toEqual([])
  })

  it('tracks reactive versions and pinned refs', () => {
    const versions = vue.ref<string[] | null>(null)
    const pinned = vue.ref<string | null>(null)
    const options = usePatchOptions(versions, () => pinned.value)
    expect(options.value).toEqual([])

    versions.value = ['15.13.1']
    pinned.value = '15.1'
    expect(values(options.value)).toEqual(['15.13', '15.1'])
  })
})
