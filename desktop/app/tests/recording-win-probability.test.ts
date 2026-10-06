import { describe, expect, it } from 'vitest'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import type { RecordingWinProbability } from '~/types/recordings'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import { SWINGS_SHOWN } from '#common/utils/win-probability-swing'
import { curveAt, recapWinProbability, videoMsAt } from '~/utils/recording-win-probability'

const timeline = fixture.timeline as WinProbabilityTimeline

function recording(teamId: number, clock = [{ fromGameMs: 0, offsetMs: 20_000 }], durationMs: number | null = 1_900_000): RecordingWinProbability {
  return {
    teamId,
    champions: Array.from({ length: 10 }, (_, index) => ({ participantId: index + 1, championId: index + 1 })),
    timeline,
    clock,
    durationMs,
  }
}

const name = (championId: number) => `Champ${championId}`

describe('videoMsAt', () => {
  const clock = [{ fromGameMs: 0, offsetMs: 12_000 }, { fromGameMs: 600_000, offsetMs: 45_000 }]

  it('adds the offset of the segment the moment falls in, as the shell does', () => {
    expect(videoMsAt(clock, 100_000, null)).toBe(112_000)
    expect(videoMsAt(clock, 600_000, null)).toBe(645_000)
  })

  it('uses the first segment before any, and never goes before the video', () => {
    expect(videoMsAt([{ fromGameMs: 5_000, offsetMs: -3_000 }], 1_000, null)).toBe(0)
  })

  it('stops at the end of the video', () => {
    expect(videoMsAt(clock, 1_800_000, 1_000_000)).toBe(1_000_000)
  })
})

describe('recapWinProbability', () => {
  it('places the curve on the video, read for the player', () => {
    const blue = recapWinProbability(recording(100), name)!
    const red = recapWinProbability(recording(200), name)!
    expect(blue.curve[0]!.videoMs).toBe(timeline.frames[0]!.ms + 20_000)
    expect(blue.curve.at(-1)!.p).toBeCloseTo(fixture.expected.points.at(-1)!.p)
    expect(red.curve.at(-1)!.p).toBeCloseTo(1 - fixture.expected.points.at(-1)!.p)
  })

  it('lists the largest swings from the player\'s side, named and on the video', () => {
    const blue = recapWinProbability(recording(100), name)!
    const red = recapWinProbability(recording(200), name)!
    expect(blue.turningPoints).toHaveLength(SWINGS_SHOWN)
    const [first] = blue.turningPoints
    const swing = fixture.expected.swings[0]!
    expect(first!.gameMs).toBe(swing.ms)
    expect(first!.videoMs).toBe(swing.ms + 20_000)
    expect(first!.delta).toBeCloseTo(swing.delta)
    expect(red.turningPoints[0]!.delta).toBeCloseTo(-swing.delta)
    expect(red.turningPoints[0]!.ours).toBe(!first!.ours)
    const kill = blue.turningPoints.find(point => point.icon === 'kill')
    if (kill) expect(kill.label).toMatch(/Champ\d+ killed Champ\d+/)
  })

  it('is null without an anchor, or for a game the model draws no curve for', () => {
    expect(recapWinProbability(recording(100, []), name)).toBeNull()
    expect(recapWinProbability({ ...recording(100), timeline: { ...timeline, durationMs: 600_000 } }, name)).toBeNull()
  })
})

describe('curveAt', () => {
  const curve = [{ videoMs: 0, p: 0.5 }, { videoMs: 100, p: 0.7 }, { videoMs: 200, p: 0.3 }]

  it('interpolates between points and holds outside them', () => {
    expect(curveAt(curve, 50)).toBeCloseTo(0.6)
    expect(curveAt(curve, 150)).toBeCloseTo(0.5)
    expect(curveAt(curve, -10)).toBe(0.5)
    expect(curveAt(curve, 500)).toBe(0.3)
    expect(curveAt([], 10)).toBe(0.5)
  })
})
