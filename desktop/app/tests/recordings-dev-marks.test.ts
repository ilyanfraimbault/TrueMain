import { describe, expect, it } from 'vitest'
import type { Chord } from '../app/types/overlay'
import { OVERLAY_PANELS } from '../app/types/overlay'
import { suggestedChord } from '../app/utils/overlay-keys'
import { devChordView, devLibrary, devMarkKeyRefusal, devSettings } from '../app/fixtures/recordings'
import { devRecordingCall } from '../app/utils/recordings-dev'

// The browser-only stand-in for the shell (`npm run dev`) answers the mark key
// as the shell does, so the recap and the settings can be tried without a game.
const chord = (change: Partial<Chord>): Chord => ({ alt: false, shift: false, ctrl: false, meta: false, tab: false, key: null, ...change })

describe('the dev recordings and the mark key', () => {
  it('cut each marked moment into a clip of the 30 s before it', () => {
    const { games, clips } = devLibrary(Date.UTC(2026, 9, 20))
    const marked = games.flatMap(game => game.moments.filter(moment => moment.kind === 'marked').map(moment => ({ game, moment })))
    expect(marked.length).toBeGreaterThanOrEqual(2)
    for (const { game, moment } of marked) {
      const clip = clips.find(entry => entry.recordingId === game.id && entry.endMs === moment.videoMs)
      expect(clip?.startMs).toBe(moment.videoMs - 30_000)
      expect(clip?.title).toMatch(/^Ahri at \d+:\d\d$/)
    }
  })

  it('offer the default key and refuse what the shell refuses', () => {
    const settings = devSettings()
    expect(settings.settings.markKey).toEqual(chord({ alt: true, shift: true, key: 'Digit0' }))
    expect(settings.markKey?.label).toMatch(/0$/)
    // A panel put on a chord is never offered the mark key's.
    for (const panel of OVERLAY_PANELS) {
      for (const kind of ['whileHeld', 'toggle'] as const) expect(suggestedChord(panel, kind)).not.toEqual(settings.settings.markKey)
    }

    expect(devMarkKeyRefusal(chord({ alt: true }))).toContain('modifiers alone')
    expect(devMarkKeyRefusal(chord({ tab: true }))).toContain('TAB alone')
    expect(devMarkKeyRefusal(chord({ key: 'Escape' }))).toContain('Esc and Enter')
    expect(devMarkKeyRefusal(chord({ meta: true, key: 'KeyQ' }))).toContain('system')
    expect(devMarkKeyRefusal(chord({ alt: true, shift: true, key: 'KeyO' }))).toContain('hides the whole overlay')
    expect(devMarkKeyRefusal(chord({ alt: true, shift: true, key: 'Digit1' }))).toBe('Already the shortcut of Build path.')
    expect(devMarkKeyRefusal(chord({ ctrl: true, key: 'KeyM' }))).toBeNull()
    expect(devChordView(chord({ alt: true, key: 'KeyQ' })).warning).toContain('Q ability')
    expect(devChordView(chord({ ctrl: true, key: 'KeyM' })).warning).toBeNull()
  })

  it('check, save and refuse a mark key through the commands', async () => {
    const check = await devRecordingCall('recordingCheckMarkKey', chord({ alt: true, shift: true, key: 'Digit1' }))
    expect(check.refusal).toBe('Already the shortcut of Build path.')

    const view = await devRecordingCall('recordingSettings')
    const off = await devRecordingCall('setRecordingSettings', { ...view.settings, markKey: null })
    expect(off.markKey).toBeNull()
    const moved = await devRecordingCall('setRecordingSettings', { ...view.settings, markKey: chord({ ctrl: true, key: 'KeyM' }) })
    expect(moved.markKey?.label).toMatch(/M$/)
    await expect(devRecordingCall('setRecordingSettings', { ...view.settings, markKey: chord({ key: 'Enter' }) }))
      .rejects.toThrow('Esc and Enter')
  })

  it('replace a clip with its adjusted range, keeping its favourite', async () => {
    const before = await devRecordingCall('recordingLibrary')
    const target = before.clips.find(clip => clip.title.startsWith('Ahri at') && clip.recordingId)!
    await devRecordingCall('clipUpdate', target.id, null, true)

    const saved = await devRecordingCall('clipSave', target.recordingId!, target.startMs - 10_000, target.endMs, target.title, target.id)

    const after = await devRecordingCall('recordingLibrary')
    expect(after.clips).toHaveLength(before.clips.length)
    expect(after.clips.some(clip => clip.id === target.id)).toBe(false)
    expect(saved).toMatchObject({ startMs: target.startMs - 10_000, endMs: target.endMs, favorite: true })
  })
})
