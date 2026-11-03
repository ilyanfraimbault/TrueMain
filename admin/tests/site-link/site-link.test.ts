import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import SiteLink from '~~/app/components/SiteLink.vue'
import { championSitePath, knownPlayerPath, siteHref, truemainPath } from '~~/shared/utils/site-link'

// "Open on the site" (#2291): the paths must be the site's own, and the button must
// disappear rather than point at the admin when no site is configured.

describe('siteHref', () => {
  it('joins the configured origin and the path', () => {
    expect(siteHref('https://site.test', '/players/faker')).toBe('https://site.test/players/faker')
  })

  it('drops trailing slashes and adds a missing leading one', () => {
    expect(siteHref(' https://site.test// ', 'champions/ahri')).toBe('https://site.test/champions/ahri')
  })

  it('is null without a site or without a path', () => {
    expect(siteHref('', '/players/faker')).toBeNull()
    expect(siteHref(undefined, '/players/faker')).toBeNull()
    expect(siteHref('https://site.test', null)).toBeNull()
  })
})

describe('site paths', () => {
  it('builds a known player page', () => {
    expect(knownPlayerPath('faker')).toBe('/players/faker')
  })

  it('builds a profile the way the site spells it', () => {
    expect(truemainPath('Hide on bush', 'KR1')).toBe('/truemains/Hide%20on%20bush-KR1')
    expect(truemainPath('A-B', 'EUW')).toBe('/truemains/A-B-EUW')
  })

  it('has no profile without both halves of the Riot ID', () => {
    expect(truemainPath(null, 'KR1')).toBeNull()
    expect(truemainPath('Faker', null)).toBeNull()
    expect(truemainPath('Faker', ' ')).toBeNull()
  })

  it('uses the champion slug, or the id the site redirects', () => {
    expect(championSitePath(103, { 103: 'ahri' })).toBe('/champions/ahri')
    expect(championSitePath(103, null)).toBe('/champions/103')
  })
})

describe('SiteLink', () => {
  let siteUrl = ''

  beforeEach(() => {
    vi.stubGlobal('useRuntimeConfig', () => ({ public: { siteUrl } }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const UButton = defineComponent({
    props: ['href', 'target', 'rel', 'icon'],
    setup(props, { attrs }) {
      return () => h('a', { href: props.href, target: props.target, rel: props.rel, 'aria-label': attrs['aria-label'] })
    },
  })
  const UTooltip = defineComponent({
    props: ['text'],
    setup(props, { slots }) {
      return () => h('span', { 'data-tooltip': props.text }, slots.default?.())
    },
  })
  const global = { components: { UButton, UTooltip } }

  it('opens the page on the site in a new tab', () => {
    siteUrl = 'https://site.test/'
    const wrapper = mount(SiteLink, { props: { path: '/players/faker' }, global })
    const link = wrapper.find('a')
    expect(link.attributes('href')).toBe('https://site.test/players/faker')
    expect(link.attributes('target')).toBe('_blank')
    expect(link.attributes('rel')).toBe('noopener noreferrer')
    expect(link.attributes('aria-label')).toBe('Open on the site')
  })

  it('names the page it opens', () => {
    siteUrl = 'https://site.test'
    const wrapper = mount(SiteLink, { props: { path: '/champions/ahri', label: 'Open Ahri on the site' }, global })
    expect(wrapper.find('[data-tooltip]').attributes('data-tooltip')).toBe('Open Ahri on the site')
  })

  it('is hidden when no site is configured', () => {
    siteUrl = ''
    const wrapper = mount(SiteLink, { props: { path: '/players/faker' }, global })
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('is hidden when there is no page to open', () => {
    siteUrl = 'https://site.test'
    const wrapper = mount(SiteLink, { props: { path: null }, global })
    expect(wrapper.find('a').exists()).toBe(false)
  })
})
