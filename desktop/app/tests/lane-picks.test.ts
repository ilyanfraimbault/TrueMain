import { describe, expect, it } from 'vitest'
import { lanePicks, lanePicksLoading } from '~/utils/lane-picks'

const ranked: Record<string, number[]> = {
  TOP: [1, 2, 3, 4, 5, 6, 7],
  MIDDLE: [8],
}
const laneEntries = (lane: string) => ranked[lane] ?? []

describe('lanePicks', () => {
  it('keeps every lane in lane order, labelled', () => {
    const columns = lanePicks(laneEntries)
    expect(columns.map(column => column.lane)).toEqual(['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'])
    expect(columns[0]!.label).toBe('Top')
  })

  it('leads with the best entry and keeps the next four', () => {
    const [top] = lanePicks(laneEntries)
    expect(top!.lead).toBe(1)
    expect(top!.rest).toEqual([2, 3, 4, 5])
  })

  it('leaves a lane without entries empty, and one with a single entry with no rest', () => {
    const columns = lanePicks(laneEntries)
    expect(columns[1]).toMatchObject({ lead: null, rest: [] })
    expect(columns[2]).toMatchObject({ lead: 8, rest: [] })
  })

  it('cuts to the requested size', () => {
    expect(lanePicks(laneEntries, 3)[0]!.rest).toEqual([2, 3])
  })
})

describe('lanePicksLoading', () => {
  const empty = lanePicks(() => [])

  it('shows skeletons while nothing has landed, failures included', () => {
    expect(lanePicksLoading(empty, 'pending')).toBe(true)
    expect(lanePicksLoading(empty, 'error')).toBe(true)
  })

  it('stops once the list is in, even with every lane empty', () => {
    expect(lanePicksLoading(empty, 'ready')).toBe(false)
  })

  it('stops as soon as one lane has a pick', () => {
    expect(lanePicksLoading(lanePicks(laneEntries), 'pending')).toBe(false)
  })
})
