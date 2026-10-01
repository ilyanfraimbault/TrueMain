import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { Account } from '#components'

// The shared player identity (#1734). These pin what the component promises
// every surface that draws a truemain through it: the icon is the site's one
// canonical cached asset (a plain <img> through `/_ipx`, never NuxtImg's
// srcset), an account without an icon asks for nothing, and the link is the
// profile unless the surface owns the link itself.
const identity = { gameName: 'Hide on bush', tagLine: 'KR1', profileIconId: 6, platformId: 'KR' }

describe('Account', () => {
  it('draws the icon as a plain <img> on the canonical webp URL', async () => {
    const wrapper = await mountSuspended(Account, { props: { identity, patch: '16.19.1' } })

    const img = wrapper.find('img')
    expect(img.exists()).toBe(true)
    expect(img.attributes('src')).toContain('/_ipx/')
    expect(img.attributes('src')).toContain('f_webp')
    expect(img.attributes('src')).toContain('profileicon/6.png')
    expect(img.attributes('srcset')).toBeUndefined()
  })

  it.each([0, null])('requests no picture for icon id %j and shows the user glyph', async (profileIconId) => {
    const wrapper = await mountSuspended(Account, {
      props: { identity: { ...identity, profileIconId }, patch: '16.19.1' },
    })

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('[data-slot="icon"]').exists()).toBe(true)
  })

  it('links the profile by default, on the encoded slug', async () => {
    const wrapper = await mountSuspended(Account, { props: { identity, patch: '16.19.1' } })

    const link = wrapper.find('a')
    expect(link.attributes('href')).toBe('/truemains/Hide%20on%20bush-KR1')
    expect(link.attributes('aria-label')).toBe('Hide on bush #KR1')
  })

  it('renders no link when the surface owns it', async () => {
    const wrapper = await mountSuspended(Account, { props: { identity, patch: '16.19.1', to: false } })

    expect(wrapper.find('a').exists()).toBe(false)
    expect(wrapper.text()).toContain('Hide on bush')
    expect(wrapper.text()).toContain('#KR1')
  })

  it('reads the region from the platform, and drops the flag in the inline layout', async () => {
    const stacked = await mountSuspended(Account, { props: { identity, patch: '16.19.1' } })
    expect(stacked.find('[aria-label="Korea"]').exists()).toBe(true)

    const inline = await mountSuspended(Account, { props: { identity, patch: '16.19.1', layout: 'inline' } })
    expect(inline.find('[aria-label="Korea"]').exists()).toBe(false)
  })
})
