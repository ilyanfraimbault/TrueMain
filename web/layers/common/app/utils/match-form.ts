import type { MatchSummaryResponse } from '#shared/types/matches'

/** How many of the newest games a form tile weighs against the whole sample. */
export const RECENT_GAMES = 5
/** The shortest sample a move is read on: under it, the "recent" games are most of the sample. */
export const MIN_GAMES_FOR_DELTA = RECENT_GAMES + 3

export interface FormTile {
  key: 'kda' | 'killParticipation' | 'csPerMinute' | 'deaths'
  label: string
  value: string
  /** The newest `RECENT_GAMES` against the sample's average; null on too short a sample. */
  delta: { text: string, direction: 'up' | 'down', good: boolean | null } | null
  /** Game by game, oldest first — the tile's sparkline. */
  series: number[]
}

interface Metric {
  key: FormTile['key']
  label: string
  read: (match: MatchSummaryResponse) => number
  format: (value: number) => string
  higherIsBetter: boolean
}

const METRICS: Metric[] = [
  {
    key: 'kda',
    label: 'KDA',
    read: ({ self }) => (self.kills + self.assists) / Math.max(self.deaths, 1),
    format: value => value.toFixed(2),
    higherIsBetter: true,
  },
  {
    key: 'killParticipation',
    label: 'Kill part.',
    read: ({ self }) => self.killParticipation,
    format: value => `${Math.round(value * 100)}%`,
    higherIsBetter: true,
  },
  {
    key: 'csPerMinute',
    label: 'CS / min',
    read: ({ self, gameDurationSeconds }) => self.cs / Math.max(gameDurationSeconds / 60, 1),
    format: value => value.toFixed(1),
    higherIsBetter: true,
  },
  {
    key: 'deaths',
    label: 'Deaths',
    read: ({ self }) => self.deaths,
    format: value => value.toFixed(1),
    higherIsBetter: false,
  },
]

const average = (values: number[]) => values.reduce((sum, value) => sum + value, 0) / values.length

/**
 * A player's recent form, the four tiles a profile opens on (#1682's dashboard,
 * shared): the average over the games given, how the newest `RECENT_GAMES`
 * compare with it, and the run game by game. The same reading on the site's
 * profile and the app's dashboard, so the two pages tell the same story.
 * `matches` is newest first, as every history is served.
 */
export function formTiles(matches: MatchSummaryResponse[]): FormTile[] {
  if (!matches.length) return []
  const oldestFirst = [...matches].reverse()
  return METRICS.map((metric) => {
    const series = oldestFirst.map(metric.read)
    const mean = average(series)
    const recent = series.length >= MIN_GAMES_FOR_DELTA ? average(series.slice(-RECENT_GAMES)) : null
    const diff = recent === null ? 0 : recent - mean
    return {
      key: metric.key,
      label: metric.label,
      value: metric.format(mean),
      delta: recent === null || diff === 0
        ? null
        : {
            text: metric.format(Math.abs(diff)),
            direction: diff > 0 ? 'up' : 'down',
            good: (diff > 0) === metric.higherIsBetter,
          },
      series,
    }
  })
}
