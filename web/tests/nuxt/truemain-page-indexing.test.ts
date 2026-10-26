import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { injectHead } from '#imports'
import { resolveTags } from 'unhead/utils'
import { computed, defineComponent, h } from 'vue'

// Paths, not URLs: the test runtime has no site URL of its own.
// The canonical a truemain page declares (#2335): the stored Riot ID's slug, the
// route's own slug while the lookup has no verdict, none for an unknown account.

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
mockNuxtImport('useApiFetch', () => () => apiFetch)

afterEach(() => apiFetch.mockReset())

function httpError(statusCode: number) {
  return Object.assign(new Error(`HTTP ${statusCode}`), { statusCode })
}

/** Mounts the composable, then reads the canonical the head resolves to (deduped, by priority). */
async function canonicalFor(nameTag: string) {
  let head!: ReturnType<typeof injectHead>
  const wrapper = await mountSuspended(defineComponent({
    async setup() {
      head = injectHead()
      await useTruemainPageIndexing(computed(() => nameTag), truemainProfilePath)
      return () => h('div')
    },
  }))
  await flushPromises()
  const tags = resolveTags(head)
  wrapper.unmount()
  return tags
    .filter(tag => tag.tag === 'link' && tag.props.rel === 'canonical')
    .map(tag => new URL(String(tag.props.href), 'http://localhost').pathname)
    .filter(path => path.startsWith('/truemains/'))
}

describe('useTruemainPageIndexing', () => {
  it('declares the stored Riot ID\'s spelling, whatever casing the route used', async () => {
    apiFetch.mockResolvedValue({ nameTag: 'Known Ghoster-False' })

    const canonical = await canonicalFor('known ghoster-false')

    expect(apiFetch).toHaveBeenCalledWith('/truemain-identity/known%20ghoster-false', expect.anything())
    expect(canonical).toEqual(['/truemains/Known%20Ghoster-False'])
  })

  it('keeps the route\'s slug, never lowercased, when the lookup has no verdict', async () => {
    apiFetch.mockRejectedValue(httpError(502))

    expect(await canonicalFor('Outage Case-EUW')).toEqual(['/truemains/Outage%20Case-EUW'])
  })

  it('declares no profile canonical for an unknown account', async () => {
    apiFetch.mockRejectedValue(httpError(404))

    expect(await canonicalFor('nobody-0000')).toEqual([])
  })
})
