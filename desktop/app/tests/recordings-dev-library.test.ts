import { describe, expect, it, vi } from 'vitest'
import { devRecordingCall } from '../app/utils/recordings-dev'

/** The dev stand-in for the shell's library commands keeps, frees and deletes like the shell. */
describe('the dev recordings library', () => {
  it('keeps, reads and deletes a game, unlinking its clips', async () => {
    const library = await devRecordingCall('recordingLibrary')
    const game = library.games.find(entry => library.clips.some(clip => clip.recordingId === entry.id))!
    expect((await devRecordingCall('recordingSetKept', game.id, true)).kept).toBe(true)
    expect((await devRecordingCall('recordingGet', game.id)).id).toBe(game.id)
    await devRecordingCall('recordingWinProbability', game.id)
    expect(await devRecordingCall('recordingDelete', game.id)).toBeNull()
    const after = await devRecordingCall('recordingLibrary')
    expect(after.games.some(entry => entry.id === game.id)).toBe(false)
    expect(after.clips.some(clip => clip.recordingId === game.id)).toBe(false)
    await expect(devRecordingCall('recordingGet', game.id)).rejects.toThrow('No recording')
  })

  it('deletes a clip, and answers the commands that only open something', async () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => {})
    const clip = (await devRecordingCall('recordingLibrary')).clips[0]!
    await devRecordingCall('revealRecordingFile', clip.videoPath)
    await devRecordingCall('openYoutubeUpload')
    await devRecordingCall('clipStartDrag', clip.id)
    expect(info).toHaveBeenCalledTimes(3)
    expect(await devRecordingCall('clipDelete', clip.id)).toBeNull()
    await expect(devRecordingCall('clipDelete', clip.id)).rejects.toThrow('No clip')
    info.mockRestore()
  })

  it('grants the capture permission it was waiting for', async () => {
    const status = await devRecordingCall('requestCapturePermission')
    expect(status.availability).toBe('ready')
  })
})
