import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import type { Clip, GameRecording, Moment } from '../app/types/recordings'
import { useRecapDrafts } from '../app/composables/useRecapDrafts'
import { MARK_WINDOW_MS, momentLabel, momentStart, momentTone, proposeClipTitle } from '../app/utils/recording-moments'

const marked = (videoMs: number): Moment => ({ kind: 'marked', videoMs, endVideoMs: videoMs, kills: 0, ally: null })

describe('marked moments', () => {
  it('read as the player\'s own mark', () => {
    expect(momentLabel(marked(754_000))).toBe('Marked moment')
    expect(momentTone(marked(754_000))).toBe('text-primary')
  })

  it('land at the start of the 30 s they keep, other moments a lead before', () => {
    expect(momentStart(marked(754_000), 5_000)).toBe(754_000 - MARK_WINDOW_MS)
    expect(momentStart({ kind: 'kill', videoMs: 754_000 }, 5_000)).toBe(749_000)
    expect(momentStart({ videoMs: 754_000 }, 5_000)).toBe(749_000)
    // A mark keeps the length chosen at its press (#1767).
    expect(momentStart({ ...marked(754_000), windowMs: 120_000 }, 5_000)).toBe(634_000)
  })

  it('name a range holding nothing else', () => {
    expect(proposeClipTitle([marked(754_000)], 724_000, 754_000, 'Ahri')).toBe('Marked moment')
    const kill: Moment = { kind: 'kill', videoMs: 740_000, endVideoMs: 740_000, kills: 1, ally: null }
    expect(proposeClipTitle([kill, marked(754_000)], 724_000, 754_000, 'Ahri')).toBe('Kill on Ahri')
  })
})

describe('adjusting a saved clip in the recap', () => {
  const saveClip = vi.fn(async () => ({}) as Clip)
  const state = new Map<string, ReturnType<typeof ref>>()

  beforeEach(() => {
    vi.stubGlobal('ref', ref)
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('useState', (key: string, init: () => unknown) => {
      if (!state.has(key)) state.set(key, ref(init()))
      return state.get(key)
    })
    vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri' }))
    vi.stubGlobal('useRecordings', () => ({ saveClip }))
    saveClip.mockClear()
    state.clear()
  })

  afterEach(() => vi.unstubAllGlobals())

  const recording = ref({ id: 'game-1', championId: 103, moments: [marked(754_000)] } as unknown as GameRecording)
  const clip = { id: 'clip-1', startMs: 724_000, endMs: 754_000, title: 'Marked at 12:34' }

  it('opens the clip\'s range as a draft that replaces it once saved', async () => {
    const drafts = useRecapDrafts(recording, ref(1_800_000))
    const key = drafts.adjust(clip)

    expect(drafts.drafts.value).toHaveLength(1)
    expect(drafts.drafts.value[0]).toMatchObject({ startMs: 724_000, endMs: 754_000, title: 'Marked at 12:34', edited: true, replaces: 'clip-1' })
    expect(drafts.selected.value).toBe(key)
    // Adjusting it again picks the same draft rather than a second one.
    expect(drafts.adjust(clip)).toBe(key)
    expect(drafts.drafts.value).toHaveLength(1)

    // Its ends move; the title the clip had stays.
    drafts.resize(key, 714_000, 760_000)
    await drafts.save(key)
    expect(saveClip).toHaveBeenCalledWith('game-1', 714_000, 760_000, 'Marked at 12:34', 'clip-1')
    expect(drafts.drafts.value).toHaveLength(0)
  })

  it('saves a range cut by hand as a new clip', async () => {
    const drafts = useRecapDrafts(recording, ref(1_800_000))
    const key = drafts.add(724_000, 754_000)
    expect(drafts.drafts.value[0]!.title).toBe('Marked moment')
    await drafts.save(key)
    expect(saveClip).toHaveBeenCalledWith('game-1', 724_000, 754_000, 'Marked moment', undefined)
  })
})
