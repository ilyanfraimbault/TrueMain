import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { createError } from 'h3'
import { describe, expect, it } from 'vitest'

/**
 * The profile read that rides the SSR payload (#2359): a found profile, a definite
 * 404 as "not found", and any other failure as an error — never as an unknown
 * player, which would render the not-found page during an API outage.
 */
registerEndpoint('/api/truemains/Found-EUW/profile', () => ({
  identity: { gameName: 'Found', tagLine: 'EUW', platformId: 'EUW1', profileIconId: 1, summonerLevel: 100 },
  ranked: null,
  mains: [],
  dedication: null,
  positions: [],
  persona: null,
}))
registerEndpoint('/api/truemains/Missing-EUW/profile', () => {
  throw createError({ statusCode: 404 })
})
registerEndpoint('/api/truemains/Broken-EUW/profile', () => {
  throw createError({ statusCode: 502 })
})

async function read(nameTag: string) {
  let result!: ReturnType<typeof useTruemainProfile>
  await mountSuspended(defineComponent({
    async setup() {
      result = useTruemainProfile(nameTag, { ssr: true })
      await result.ready
      return () => h('div')
    },
  }))
  await flushPromises()
  return result
}

describe('useTruemainProfile ssr', () => {
  it('reads a profile', async () => {
    const profile = await read('Found-EUW')

    expect(profile.data.value?.identity.gameName).toBe('Found')
    expect(profile.notFound.value).toBe(false)
    expect(profile.isInitialLoading.value).toBe(false)
  })

  it('treats a 404 as not found', async () => {
    const profile = await read('Missing-EUW')

    expect(profile.data.value).toBeNull()
    expect(profile.notFound.value).toBe(true)
    expect(profile.error.value).toBeFalsy()
  })

  it('treats any other failure as an error, not an unknown player', async () => {
    const profile = await read('Broken-EUW')

    expect(profile.notFound.value).toBe(false)
    expect(profile.error.value).toBeTruthy()
  })
})
