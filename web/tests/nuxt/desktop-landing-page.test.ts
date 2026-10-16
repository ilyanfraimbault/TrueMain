import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  DesktopLandingDownloadButtons,
  DesktopLandingFaq,
  DesktopLandingPlatformRows,
  DesktopLandingSection,
  UApp,
} from '#components'
import type { DesktopRelease } from '~~/shared/types/desktop'
import DownloadPage from '~/pages/download.vue'
import { useSceneBeats } from '~/composables/useLandingMotion'
import { useDesktopPlatform } from '~/composables/useDesktopPlatform'

// `/download` (#2072): the download call to action and rows, the questions, the
// section shell, the page as a whole, and the motion and platform composables
// the scenes and the buttons stand on.

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
mockNuxtImport('useApiFetch', () => () => apiFetch)

const RELEASE: DesktopRelease = {
  channel: 'stable',
  version: '0.6.0',
  tag: 'desktop-v0.6.0',
  publishedAt: '2026-10-14T09:00:00Z',
  pageUrl: 'https://github.com/example/releases/tag/desktop-v0.6.0',
  installers: { mac: 'https://example.com/truemain.dmg', windows: 'https://example.com/truemain.exe' },
  manifestUrl: null,
}

registerEndpoint('/api/desktop/release', () => RELEASE)

beforeEach(() => {
  apiFetch.mockResolvedValue([])
})

afterEach(() => {
  apiFetch.mockReset()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

const buttons = (props: Partial<InstanceType<typeof DesktopLandingDownloadButtons>['$props']>) => mountSuspended(DesktopLandingDownloadButtons, {
  props: { release: RELEASE, pending: false, failed: false, platform: null, other: null, onPhone: false, ...props },
})

describe('LandingDownloadButtons', () => {
  it('offers both installers side by side before the platform is read', async () => {
    const wrapper = await buttons({})
    const links = wrapper.findAll('a')

    expect(links.map(link => link.attributes('href'))).toEqual(['/api/desktop/download/mac', '/api/desktop/download/windows'])
    expect(wrapper.text()).toContain('Free · Version 0.6.0 · 14 October 2026')
  })

  it('leads with the visitor\'s installer and offers the other beside it', async () => {
    const wrapper = await buttons({ platform: 'windows', other: 'mac' })
    const links = wrapper.findAll('a')

    expect(links[0]!.text()).toBe('Download for Windows')
    expect(links[1]!.text()).toBe('macOS')
  })

  it('says a preprod build reads preprod, and that the app needs a computer', async () => {
    const wrapper = await buttons({ release: { ...RELEASE, channel: 'beta' }, onPhone: true })

    expect(wrapper.text()).toContain('Test build: it reads this preprod\'s data')
    expect(wrapper.text()).toContain('open this page there')
  })

  it('warns when the releases cannot be read, and waits quietly while they load', async () => {
    expect((await buttons({ release: null, failed: true })).text()).toContain('The download is unavailable for a moment')
    expect((await buttons({ release: null, pending: true })).text()).toBe('')
  })

  it('shows a disabled "Coming soon" before the first release', async () => {
    const wrapper = await buttons({ release: null })

    expect(wrapper.text()).toContain('Coming soon')
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })
})

describe('LandingPlatformRows', () => {
  it('puts the visitor\'s platform first and marks it as this computer', async () => {
    const wrapper = await mountSuspended(DesktopLandingPlatformRows, { props: { release: RELEASE, platform: 'windows' } })
    const rows = wrapper.findAll('li')

    expect(rows[0]!.text()).toContain('Windows')
    expect(rows[0]!.text()).toContain('This computer')
    expect(rows[1]!.text()).not.toContain('This computer')
  })

  it('leads with macOS when the platform is unknown, and disables a missing installer', async () => {
    const wrapper = await mountSuspended(DesktopLandingPlatformRows, {
      props: { release: { ...RELEASE, installers: { mac: RELEASE.installers.mac } }, platform: null },
    })
    const rows = wrapper.findAll('li')

    expect(rows[0]!.text()).toContain('macOS')
    expect(wrapper.text()).not.toContain('This computer')
    expect(rows[1]!.find('[aria-disabled="true"], [disabled]').exists()).toBe(true)
  })
})

describe('LandingFaq', () => {
  it('asks the eight questions', async () => {
    const wrapper = await mountSuspended(DesktopLandingFaq)

    expect(wrapper.findAll('button')).toHaveLength(8)
    expect(wrapper.text()).toContain('Is it safe to use with Riot and Vanguard?')
  })
})

describe('LandingSection', () => {
  it('heads its moment and holds the scene under it', async () => {
    const wrapper = await mountSuspended(DesktopLandingSection, {
      props: { id: 'in-game', moment: 'In game', title: 'Four panels', lead: 'Right on the game.' },
      slots: { default: () => 'scene' },
    })

    expect(wrapper.attributes('id')).toBe('in-game')
    expect(wrapper.find('h2').text()).toBe('Four panels')
    expect(wrapper.text()).toContain('scene')
  })
})

describe('the /download page', () => {
  it('lays out the four moments, the download and the questions', async () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    const wrapper = await mountSuspended(defineComponent({ setup: () => () => h(UApp, null, () => h(DownloadPage)) }))
    await flushPromises()

    for (const id of ['champion-select', 'in-game', 'after-the-game', 'recordings', 'download']) {
      expect(wrapper.find(`#${id}`).exists()).toBe(true)
    }
    expect(wrapper.text()).toContain('Every game,')
    expect(wrapper.text()).toContain('Questions')
    expect(wrapper.text()).toContain('Version 0.6.0')
  })
})

/** A component running `useSceneBeats` over a four-beat timeline. */
function scene(beats: number[], options?: { loop?: boolean }) {
  let result!: ReturnType<typeof useSceneBeats>
  const component = defineComponent({
    setup() {
      const root = ref<HTMLElement | null>(null)
      result = useSceneBeats(root, beats, options)
      return () => h('div', { ref: root })
    },
  })
  return { component, beats: () => result }
}

class FakeObserver {
  static last: FakeObserver | null = null
  constructor(readonly callback: (entries: { isIntersecting: boolean }[]) => void) {
    FakeObserver.last = this
  }

  observe() {}
  disconnect() {}
  emit(isIntersecting: boolean) {
    this.callback([{ isIntersecting }])
  }
}

function stubMotion(reduced: boolean) {
  vi.stubGlobal('IntersectionObserver', FakeObserver)
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: reduced && query.includes('reduce'), addEventListener() {}, removeEventListener() {} }))
}

describe('useSceneBeats', () => {
  it('rewinds once mounted, plays while on screen, loops, and rewinds when it leaves', async () => {
    vi.useFakeTimers()
    stubMotion(false)
    const { component, beats } = scene([100, 100, 100])
    await mountSuspended(component)
    await nextTick()

    expect(beats().beat.value).toBe(0)
    FakeObserver.last!.emit(true)
    await nextTick()
    vi.advanceTimersByTime(100)
    expect(beats().beat.value).toBe(1)
    expect(beats().reached(1)).toBe(true)
    expect(beats().reached(2)).toBe(false)
    vi.advanceTimersByTime(200)
    expect(beats().beat.value).toBe(0)

    vi.advanceTimersByTime(100)
    FakeObserver.last!.emit(false)
    await nextTick()
    expect(beats().beat.value).toBe(0)
    vi.advanceTimersByTime(500)
    expect(beats().beat.value).toBe(0)
  })

  it('stops on its last beat when it does not loop', async () => {
    vi.useFakeTimers()
    stubMotion(false)
    const { component, beats } = scene([100, 100], { loop: false })
    await mountSuspended(component)
    await nextTick()
    FakeObserver.last!.emit(true)
    await nextTick()

    vi.advanceTimersByTime(1000)
    expect(beats().beat.value).toBe(1)
  })

  it('holds the final state for a visitor who asked for less motion', async () => {
    vi.useFakeTimers()
    stubMotion(true)
    const { component, beats } = scene([100, 100, 100])
    await mountSuspended(component)
    await nextTick()
    FakeObserver.last?.emit(true)
    await nextTick()
    vi.advanceTimersByTime(1000)

    expect(beats().playing.value).toBe(false)
    expect(beats().beat.value).toBe(2)
    expect(beats().reached(2)).toBe(true)
  })
})

describe('useDesktopPlatform', () => {
  function probe() {
    let result!: ReturnType<typeof useDesktopPlatform>
    const component = defineComponent({
      setup() {
        result = useDesktopPlatform()
        return () => h('div')
      },
    })
    return { component, platform: () => result }
  }

  function stubNavigator(userAgent: string, platform: string, maxTouchPoints = 0) {
    vi.stubGlobal('navigator', { userAgent, platform, maxTouchPoints })
  }

  it('reads a Mac and offers Windows as the other', async () => {
    stubNavigator('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 'MacIntel')
    const { component, platform } = probe()
    await mountSuspended(component)

    expect(platform().platform.value).toBe('mac')
    expect(platform().other.value).toBe('windows')
  })

  it('reads Windows', async () => {
    stubNavigator('Mozilla/5.0 (Windows NT 10.0; Win64; x64)', 'Win32')
    const { component, platform } = probe()
    await mountSuspended(component)

    expect(platform().platform.value).toBe('windows')
    expect(platform().other.value).toBe('mac')
  })

  it('takes an iPad announcing itself as a Mac for a phone, and leaves Linux unread', async () => {
    stubNavigator('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 'MacIntel', 5)
    const ipad = probe()
    await mountSuspended(ipad.component)
    expect(ipad.platform().onPhone.value).toBe(true)
    expect(ipad.platform().platform.value).toBeNull()

    stubNavigator('Mozilla/5.0 (X11; Linux x86_64)', 'Linux x86_64')
    const linux = probe()
    await mountSuspended(linux.component)
    expect(linux.platform().platform.value).toBeNull()
    expect(linux.platform().other.value).toBeNull()
  })
})
