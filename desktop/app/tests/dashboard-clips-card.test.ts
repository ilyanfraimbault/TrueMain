import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import type { Clip, RecordingLibrary, RecordingStatus } from '../app/types/recordings'
import DashboardClipsCard from '../app/components/dashboard/DashboardClipsCard.vue'

// The card outside Nuxt: its auto-imports stubbed as globals, its child
// components as plain elements that keep what the test reads.
const library = ref<RecordingLibrary | null>(null)
const status = ref<RecordingStatus | null>(null)

const global = {
  stubs: {
    NuxtLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
    UButton: { props: ['to', 'label'], template: '<a class="see-all" :href="to">{{ label }}</a>' },
    UIcon: true,
    RecordingsThumb: { props: ['item'], template: '<div class="thumb" />' },
    ChampionPortrait: true,
  },
}

function clip(id: string, createdAtMs: number): Clip {
  return {
    id,
    title: `Clip ${id}`,
    gameId: 1,
    recordingId: null,
    queueId: 420,
    championId: 103,
    win: true,
    kills: 1,
    deaths: 0,
    assists: 2,
    gameStartedAtMs: 0,
    createdAtMs,
    startMs: 0,
    endMs: 10_000,
    durationMs: 10_000,
    favorite: false,
    sizeBytes: 1,
    videoPath: `/clips/${id}.mp4`,
    thumbnailPath: null,
    moments: [],
    youtubeUrl: null,
    videoOnDisk: true,
    automatic: [],
  }
}

const withClips = (clips: Clip[]): RecordingLibrary => ({ games: [], clips, usedBytes: 0, budgetBytes: 0, folder: '/clips' })

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useRecordings', () => ({ library, status }))
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri' }))
  status.value = { availability: 'ready' } as RecordingStatus
  library.value = null
})

afterEach(() => vi.unstubAllGlobals())

describe('DashboardClipsCard', () => {
  it('shows the four latest clips, each opening its player', () => {
    library.value = withClips(['a', 'b', 'c', 'd', 'e'].map((id, index) => clip(id, index)))
    const wrapper = mount(DashboardClipsCard, { global })

    const links = wrapper.findAll('li a')
    expect(links.map(link => link.attributes('href'))).toEqual([
      '/recordings/clips/e',
      '/recordings/clips/d',
      '/recordings/clips/c',
      '/recordings/clips/b',
    ])
    expect(links[0]!.attributes('aria-label')).toBe('Clip e')
    expect(wrapper.find('.see-all').attributes('href')).toBe('/recordings?kind=clips')
    expect(wrapper.findAll('li[aria-hidden="true"]')).toHaveLength(0)
  })

  it('keeps four cells with fewer clips, the free ones empty', () => {
    library.value = withClips([clip('a', 1)])
    const wrapper = mount(DashboardClipsCard, { global })

    expect(wrapper.findAll('li')).toHaveLength(4)
    expect(wrapper.findAll('li a')).toHaveLength(1)
    expect(wrapper.findAll('li[aria-hidden="true"]')).toHaveLength(3)
  })

  it('renders nothing without a clip', () => {
    library.value = withClips([])
    expect(mount(DashboardClipsCard, { global }).find('section').exists()).toBe(false)
  })

  it('renders nothing when the shell records nothing', () => {
    library.value = withClips([clip('a', 1)])
    status.value = null
    expect(mount(DashboardClipsCard, { global }).find('section').exists()).toBe(false)
  })
})
