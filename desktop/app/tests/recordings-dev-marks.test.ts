import { describe, expect, it } from 'vitest'
import type { Chord, TriggerCheck } from '../app/types/overlay'
import type { Clip, RecordingLibrary, RecordingSettingsView } from '../app/types/recordings'
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
      expect(clip?.title).toMatch(/^Marked at \d+:\d\d$/)
    }
  })

  it('offer the default key and refuse what the shell refuses', () => {
    const settings = devSettings()
    expect(settings.settings.markKey).toEqual(chord({ alt: true, shift: true, key: 'Digit5' }))
    expect(settings.markKey?.label).toMatch(/5$/)

    expect(devMarkKeyRefusal(chord({ alt: true }))).toContain('modifiers alone')
    expect(devMarkKeyRefusal(chord({ tab: true }))).toContain('TAB alone')
    expect(devMarkKeyRefusal(chord({ key: 'Escape' }))).toContain('Esc and Enter')
    expect(devMarkKeyRefusal(chord({ meta: true, key: 'KeyQ' }))).toContain('system')
    expect(devMarkKeyRefusal(chord({ alt: true, shift: true, key: 'KeyO' }))).toContain('hides the whole overlay')
    expect(devMarkKeyRefusal(chord({ alt: true, shift: true, key: 'Digit1' }))).toBe('Already the shortcut of Next item.')
    expect(devMarkKeyRefusal(chord({ ctrl: true, key: 'KeyM' }))).toBeNull()
    expect(devChordView(chord({ alt: true, key: 'KeyQ' })).warning).toContain('Q ability')
    expect(devChordView(chord({ ctrl: true, key: 'KeyM' })).warning).toBeNull()
  })

  it('check, save and refuse a mark key through the commands', async () => {
    const check = await devRecordingCall('recording_check_mark_key', { chord: chord({ alt: true, shift: true, key: 'Digit1' }) }) as TriggerCheck
    expect(check.refusal).toBe('Already the shortcut of Next item.')

    const view = await devRecordingCall('recording_settings') as RecordingSettingsView
    const off = await devRecordingCall('set_recording_settings', { settings: { ...view.settings, markKey: null } }) as RecordingSettingsView
    expect(off.markKey).toBeNull()
    const moved = await devRecordingCall('set_recording_settings', { settings: { ...view.settings, markKey: chord({ ctrl: true, key: 'KeyM' }) } }) as RecordingSettingsView
    expect(moved.markKey?.label).toMatch(/M$/)
    await expect(devRecordingCall('set_recording_settings', { settings: { ...view.settings, markKey: chord({ key: 'Enter' }) } }))
      .rejects.toThrow('Esc and Enter')
  })

  it('replace a clip with its adjusted range, keeping its favourite', async () => {
    const before = await devRecordingCall('recording_library') as RecordingLibrary
    const target = before.clips.find(clip => clip.title.startsWith('Marked at') && clip.recordingId)!
    await devRecordingCall('clip_update', { id: target.id, favorite: true })

    const saved = await devRecordingCall('clip_save', {
      recordingId: target.recordingId,
      startMs: target.startMs - 10_000,
      endMs: target.endMs,
      title: target.title,
      replaces: target.id,
    }) as Clip

    const after = await devRecordingCall('recording_library') as RecordingLibrary
    expect(after.clips).toHaveLength(before.clips.length)
    expect(after.clips.some(clip => clip.id === target.id)).toBe(false)
    expect(saved).toMatchObject({ startMs: target.startMs - 10_000, endMs: target.endMs, favorite: true })
  })
})
