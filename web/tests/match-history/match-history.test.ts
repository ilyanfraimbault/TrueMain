import { describe, expect, it } from 'vitest'
import type { MatchSummaryResponse } from '~~/shared/types/matches'
import { formatMatchDay, groupMatchesByDay } from '~~/app/utils/match-history'

// Local-midnight timestamps, so the grouping assertions below don't depend on
// the timezone the suite happens to run in — a UTC-literal fixture would land
// on the previous day west of Greenwich and silently change the expected
// grouping.
function at(year: number, month: number, day: number, hour: number): string {
  return new Date(year, month - 1, day, hour).toISOString()
}

function match(matchId: string, gameStartTimeUtc: string): MatchSummaryResponse {
  return { matchId, gameStartTimeUtc } as MatchSummaryResponse
}

const NOW = new Date(2026, 7, 20, 12)

describe('formatMatchDay', () => {
  it('renders a day and short month for dates in the current year', () => {
    expect(formatMatchDay(at(2026, 8, 11, 21), NOW)).toBe('11 Aug')
  })

  it('adds the year outside the current one, so two seasons never collide', () => {
    expect(formatMatchDay(at(2024, 8, 11, 21), NOW)).toBe('11 Aug 2024')
  })

  it('returns an empty label for an unparseable timestamp', () => {
    expect(formatMatchDay('not-a-date', NOW)).toBe('')
  })
})

describe('groupMatchesByDay', () => {
  it('cuts the list into consecutive same-day runs, preserving order', () => {
    const groups = groupMatchesByDay([
      match('a', at(2026, 8, 11, 22)),
      match('b', at(2026, 8, 11, 20)),
      match('c', at(2026, 8, 9, 18)),
    ], NOW)

    expect(groups.map(g => [g.label, g.matches.map(m => m.matchId)])).toEqual([
      ['11 Aug', ['a', 'b']],
      ['9 Aug', ['c']],
    ])
  })

  it('keys groups on the local day, not the UTC instant', () => {
    const groups = groupMatchesByDay([match('a', at(2026, 8, 11, 23))], NOW)
    expect(groups[0]!.key).toBe('2026-08-11')
  })

  it('opens a new group when a day repeats non-consecutively', () => {
    // Never happens on a time-ordered page, but a caller that ordered by
    // something else must not have its list silently reshuffled.
    const groups = groupMatchesByDay([
      match('a', at(2026, 8, 11, 22)),
      match('b', at(2026, 8, 9, 18)),
      match('c', at(2026, 8, 11, 10)),
    ], NOW)

    expect(groups.map(g => g.matches.map(m => m.matchId))).toEqual([['a'], ['b'], ['c']])
  })

  it('gives unparseable timestamps their own unlabelled group', () => {
    const groups = groupMatchesByDay([match('a', 'not-a-date')], NOW)
    expect(groups).toHaveLength(1)
    expect(groups[0]!.label).toBe('')
  })

  it('returns nothing for an empty page', () => {
    expect(groupMatchesByDay([], NOW)).toEqual([])
  })
})
