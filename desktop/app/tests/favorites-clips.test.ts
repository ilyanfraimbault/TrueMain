import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import type { Clip, RecordingLibrary } from '../app/types/recordings'
import FavoritesClips from '../app/components/favorites/FavoritesClips.vue'
import { favouriteClips } from '../app/utils/recording-library'

// The favourite clips on the Favorites page (#2080): the library's own flag,
// read as it is, one row per clip opening its player.
const library = ref<RecordingLibrary | null>(null)
const updateClip = vi.fn()

const global = {
  stubs: {
    NuxtLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
    FavoriteStar: { props: ['active', 'label'], emits: ['toggle'], template: '<button class="unstar" :aria-label="label" @click="$emit(\'toggle\')" />' },
    ChampionPortrait: true,
  },
}

function clip(id: string, favorite: boolean, overrides: Partial<Clip> = {}): Clip {
  return {
    id,
    title: `Clip ${id}`,
    gameId: 1,
    recordingId: null,
    queueId: 420,
    championId: 103,
    win: true,
    kills: 9,
    deaths: 2,
    assists: 11,
    gameStartedAtMs: 0,
    createdAtMs: 0,
    startMs: 0,
    endMs: 10_000,
    durationMs: 10_000,
    favorite,
    sizeBytes: 1,
    videoPath: `/clips/${id}.mp4`,
    thumbnailPath: null,
    moments: [],
    youtubeUrl: null,
    videoOnDisk: true,
    ...overrides,
  }
}

const withClips = (clips: Clip[]): RecordingLibrary => ({ games: [], clips, usedBytes: 0, budgetBytes: 0, folder: '/clips' })

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useRecordings', () => ({ library, updateClip }))
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri' }))
  library.value = null
  updateClip.mockReset()
})

afterEach(() => vi.unstubAllGlobals())

describe('favouriteClips', () => {
  it('keeps the favourite clips in the library order', () => {
    expect(favouriteClips(withClips([clip('a', true), clip('b', false), clip('c', true)])).map(c => c.id)).toEqual(['a', 'c'])
    expect(favouriteClips(null)).toEqual([])
  })
})

describe('FavoritesClips', () => {
  it('lists each favourite clip with its champion, result and K/D/A, opening its player', () => {
    library.value = withClips([clip('a', true), clip('b', false), clip('c', true, { win: false, kills: null })])
    const wrapper = mount(FavoritesClips, { global })

    const links = wrapper.findAll('li a')
    expect(links.map(link => link.attributes('href'))).toEqual(['/recordings/clips/a', '/recordings/clips/c'])
    expect(links[0]!.text()).toContain('Ahri · Victory · 9 / 2 / 11')
    expect(links[1]!.text()).toContain('Ahri · Defeat')
    expect(links[1]!.text()).not.toContain('/')
  })

  it('drops a clip from the favourites through the library', async () => {
    library.value = withClips([clip('a', true)])
    const wrapper = mount(FavoritesClips, { global })

    await wrapper.get('.unstar').trigger('click')
    expect(updateClip).toHaveBeenCalledWith('a', { favorite: false })
  })

  it('renders nothing without a favourite clip', () => {
    library.value = withClips([clip('a', false)])
    expect(mount(FavoritesClips, { global }).find('section').exists()).toBe(false)
  })
})
