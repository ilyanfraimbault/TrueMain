import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { AppFooter } from '#components'

// Riot's legal notice (#2090): the site's footer carries it under every page,
// in Riot's wording, once.

describe('AppFooter', () => {
  it('carries Riot\'s legal notice, once', async () => {
    const wrapper = await mountSuspended(AppFooter)

    const notices = wrapper.findAll('p').filter(p => p.text().includes('endorsed by Riot Games'))
    expect(notices).toHaveLength(1)
    expect(notices[0]!.text()).toContain(
      'Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.',
    )
  })

  it('links the legal pages', async () => {
    const wrapper = await mountSuspended(AppFooter)

    const hrefs = wrapper.findAll('a').map(link => link.attributes('href'))
    expect(hrefs).toEqual(expect.arrayContaining(['/about', '/privacy', '/terms']))
  })
})
