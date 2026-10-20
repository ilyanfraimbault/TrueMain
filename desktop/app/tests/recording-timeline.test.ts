import { describe, expect, it } from 'vitest'
import type { Moment } from '~/types/recordings'
import { momentSide } from '~/utils/recording-moments'
import { clampView, clusterMoments, clusterNear, fullView, isZoomed, MIN_VIEW_SPAN_MS, panView, revealInView, stepRate, tickStep, viewTicks, zoomView } from '~/utils/recording-timeline'

const LENGTH = 1_800_000

describe('the timeline view', () => {
  it('keeps the window inside the video and above the floor', () => {
    expect(clampView({ startMs: -5_000, spanMs: 60_000 }, LENGTH)).toEqual({ startMs: 0, spanMs: 60_000 })
    expect(clampView({ startMs: LENGTH, spanMs: 60_000 }, LENGTH)).toEqual({ startMs: LENGTH - 60_000, spanMs: 60_000 })
    expect(clampView({ startMs: 0, spanMs: 10 }, LENGTH).spanMs).toBe(MIN_VIEW_SPAN_MS)
    expect(clampView({ startMs: 0, spanMs: LENGTH * 3 }, LENGTH)).toEqual(fullView(LENGTH))
  })

  it('never asks for more than a video shorter than the floor', () => {
    expect(clampView({ startMs: 0, spanMs: 1_000 }, 2_000)).toEqual({ startMs: 0, spanMs: 2_000 })
  })

  it('zooms keeping the anchor under the same point', () => {
    const view = { startMs: 600_000, spanMs: 400_000 }
    const anchor = 700_000 // a quarter in
    const zoomed = zoomView(view, LENGTH, anchor, 0.5)
    expect(zoomed.spanMs).toBe(200_000)
    expect((anchor - zoomed.startMs) / zoomed.spanMs).toBeCloseTo(0.25)
  })

  it('zooms out back to the whole video, never past it', () => {
    let view = zoomView(fullView(LENGTH), LENGTH, 900_000, 1 / 16)
    expect(isZoomed(view, LENGTH)).toBe(true)
    for (let step = 0; step < 10; step++) view = zoomView(view, LENGTH, 900_000, 2)
    expect(view).toEqual(fullView(LENGTH))
    expect(isZoomed(view, LENGTH)).toBe(false)
  })

  it('pans within the video', () => {
    const view = { startMs: 100_000, spanMs: 60_000 }
    expect(panView(view, LENGTH, 30_000).startMs).toBe(130_000)
    expect(panView(view, LENGTH, -500_000).startMs).toBe(0)
  })

  it('pages to the playhead only when it left the view', () => {
    const view = { startMs: 100_000, spanMs: 60_000 }
    expect(revealInView(view, LENGTH, 130_000)).toBe(view)
    expect(revealInView(view, LENGTH, 400_000)).toEqual({ startMs: 394_000, spanMs: 60_000 })
    expect(revealInView(view, LENGTH, LENGTH)).toEqual({ startMs: LENGTH - 60_000, spanMs: 60_000 })
  })
})

describe('the clock labels', () => {
  it('keeps about eight labels at every zoom', () => {
    expect(tickStep(LENGTH)).toBe(300_000)
    expect(tickStep(60_000)).toBe(10_000)
    expect(tickStep(MIN_VIEW_SPAN_MS)).toBe(1_000)
  })

  it('labels the window, away from its edges and never 0', () => {
    expect(viewTicks({ startMs: 0, spanMs: 60_000 })).toEqual([10_000, 20_000, 30_000, 40_000, 50_000])
    expect(viewTicks({ startMs: 12_000, spanMs: 60_000 })).toEqual([20_000, 30_000, 40_000, 50_000, 60_000])
  })
})

describe('the playback speed', () => {
  it('steps up and down the list and stays at its ends', () => {
    expect(stepRate(1, 1)).toBe(1.25)
    expect(stepRate(1, -1)).toBe(0.75)
    expect(stepRate(4, 1)).toBe(4)
    expect(stepRate(0.25, -1)).toBe(0.25)
  })

  it('steps from a speed off the list to its neighbours', () => {
    expect(stepRate(1.1, 1)).toBe(1.25)
    expect(stepRate(1.1, -1)).toBe(1)
  })
})

const moment = (kind: Moment['kind'], videoMs: number, ally: boolean | null = null): Moment =>
  ({ kind, videoMs, endVideoMs: videoMs, kills: kind === 'kill' ? 1 : 0, ally, killerId: null, victimIds: [], dragon: null })

describe('the timeline events', () => {
  it('colours an event by the side it went', () => {
    expect(momentSide(moment('kill', 0))).toBe('ally')
    expect(momentSide(moment('assist', 0))).toBe('ally')
    expect(momentSide(moment('death', 0))).toBe('enemy')
    expect(momentSide(moment('dragon', 0, true))).toBe('ally')
    expect(momentSide(moment('baron', 0, false))).toBe('enemy')
  })

  it('merges events too close to tell apart into one dot, chained', () => {
    const clusters = clusterMoments([moment('death', 10_000), moment('kill', 1_000), moment('assist', 1_500), moment('kill', 2_000)], 600)
    expect(clusters.map(cluster => cluster.moments.length)).toEqual([3, 1])
    expect(clusters[0]).toMatchObject({ ms: 1_500, side: 'ally' })
    expect(clusters[1]).toMatchObject({ ms: 10_000, side: 'enemy' })
  })

  it('splits a dot whose events went both ways', () => {
    expect(clusterMoments([moment('kill', 1_000), moment('grubs', 1_200, false)], 500)[0]!.side).toBe('mixed')
  })

  it('draws a mark in the primary alone, and leaves a mark beside an event its side', () => {
    expect(momentSide(moment('marked', 0))).toBe('marked')
    expect(clusterMoments([moment('marked', 1_000)], 500)[0]!.side).toBe('marked')
    expect(clusterMoments([moment('marked', 1_000), moment('death', 1_200)], 500)[0]!.side).toBe('enemy')
  })

  it('keeps every event apart without a gap', () => {
    expect(clusterMoments([moment('kill', 1_000), moment('kill', 1_001)], 0)).toHaveLength(2)
  })

  it('finds the nearest cluster within reach of the pointer only', () => {
    const clusters = clusterMoments([moment('kill', 1_000), moment('death', 2_000)], 100)
    expect(clusterNear(clusters, 1_400, 500)?.ms).toBe(1_000)
    expect(clusterNear(clusters, 1_600, 500)?.ms).toBe(2_000)
    expect(clusterNear(clusters, 5_000, 500)).toBeNull()
  })
})
