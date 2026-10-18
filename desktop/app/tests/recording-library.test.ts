import { describe, expect, it } from 'vitest'
import type { Clip, RecordingLibrary } from '../app/types/recordings'
import { latestClips } from '../app/utils/recording-library'

function clip(id: string, createdAtMs: number, gameStartedAtMs = 0, startMs = 0): Clip {
  return {
    id,
    title: id,
    gameId: gameStartedAtMs,
    recordingId: null,
    queueId: 420,
    championId: 103,
    win: true,
    kills: 1,
    deaths: 0,
    assists: 2,
    gameStartedAtMs,
    createdAtMs,
    startMs,
    endMs: startMs + 10_000,
    durationMs: 10_000,
    favorite: false,
    sizeBytes: 1,
    videoPath: `/clips/${id}.mp4`,
    thumbnailPath: null,
    moments: [],
    youtubeUrl: null,
    videoOnDisk: true,
  }
}

const library = (clips: Clip[]): RecordingLibrary => ({ games: [], clips, usedBytes: 0, budgetBytes: 0, folder: '/clips' })

describe('latestClips', () => {
  it('lists the most recently saved clips first, up to the count', () => {
    const items = latestClips(library([clip('a', 1), clip('b', 3), clip('c', 2), clip('d', 5), clip('e', 4)]), 4)
    expect(items.map(item => item.id)).toEqual(['d', 'e', 'b', 'c'])
    expect(items.every(item => item.kind === 'clip')).toBe(true)
  })

  it('breaks a tie on the game, then on the moment', () => {
    const items = latestClips(library([clip('old-game', 9, 100, 50), clip('early', 9, 200, 10), clip('late', 9, 200, 90)]), 4)
    expect(items.map(item => item.id)).toEqual(['late', 'early', 'old-game'])
  })

  it('has nothing without a library', () => {
    expect(latestClips(null, 4)).toEqual([])
    expect(latestClips(library([]), 4)).toEqual([])
  })
})
