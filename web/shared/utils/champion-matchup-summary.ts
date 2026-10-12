import type {
  BuildSummarySentence,
  BuildSummaryToken,
  ChampionBuildSummary,
  ChampionBuildSummaryMatchups,
  SummaryMatchup,
} from '../types/champion-build-summary'
import type { ChampionMatchupEntry } from '../types/champions'
import type { ChampionStaticListItem } from '../types/static-data'
import { capitalise, figure, joinTokens, lanePhrase, plain } from './champion-build-summary'
import { formatPercentage } from './ddragon'
import { rankMatchups } from './matchup-ranking'

/**
 * The champion page's matchups, in words (#1954).
 *
 * The Matchups panel fetches client-only, so the HTML a crawler received used to
 * hold its empty state — "No matchups with enough games on this lane yet" — on
 * exactly the content a "<champion> counter" search is after, while the API had
 * dozens of measured matchups. These sentences put the panel's head into the
 * server HTML through the build summary's route, which is already SSR-enabled
 * and cached, rather than by server-rendering the panel and reopening #149.
 *
 * Each named opponent links to its own champion page: the only champion→champion
 * links in the server HTML since #1275 removed the A→Z link blocks, and in body
 * text, where an anchor says what the target is about.
 *
 * Same rule as the build sentences: every figure is a measurement, an opponent
 * DDragon cannot name is dropped rather than labelled, and nothing is padded.
 */

/** How many opponents each sentence names. The panel lists five; prose reads three. */
export const SUMMARY_MATCHUPS_PER_SIDE = 3

/**
 * The panel's two lists, cut to the prose's length and resolved to names.
 *
 * Ranked at the panel's own size and *then* sliced — never re-ranked shorter —
 * so the sentences name the first rows of the lists the reader sees beside them
 * (see `rankMatchups`). `undefined` when neither side has an opponent to name.
 */
export function resolveSummaryMatchups(
  entries: readonly ChampionMatchupEntry[] | null | undefined,
  champions: readonly ChampionStaticListItem[] | null | undefined,
): ChampionBuildSummaryMatchups | undefined {
  if (!entries?.length || !champions?.length) return undefined
  const byId = new Map(champions.map(champion => [champion.championId, champion]))
  const named = (list: ChampionMatchupEntry[]): SummaryMatchup[] => list
    .flatMap((entry) => {
      const champion = byId.get(entry.opponentChampionId)
      if (!champion?.name) return []
      return [{
        id: entry.opponentChampionId,
        name: champion.name,
        iconUrl: champion.iconUrl || null,
        games: entry.games,
        winRate: entry.winRate,
      }]
    })
    .slice(0, SUMMARY_MATCHUPS_PER_SIDE)

  const { best, worst } = rankMatchups(entries)
  const matchups = { best: named(best), worst: named(worst) }
  return matchups.best.length || matchups.worst.length ? matchups : undefined
}

/** `Zed (won 58.1% of 312 games)` — the opponent as a linked mark, then its record. */
function opponentTokens(matchup: SummaryMatchup): BuildSummaryToken[] {
  return [
    {
      kind: 'entity',
      text: matchup.name,
      iconUrl: matchup.iconUrl,
      tone: 'champion',
      source: 'champion',
      id: matchup.id,
    },
    plain(' (won '),
    figure(formatPercentage(matchup.winRate)),
    plain(' of '),
    figure(matchup.games.toLocaleString('en-US')),
    plain(matchup.games === 1 ? ' game)' : ' games)'),
  ]
}

function opponentList(matchups: SummaryMatchup[]): BuildSummaryToken[] {
  return joinTokens(matchups.map(opponentTokens), ' and ')
}

/**
 * Up to two sentences, best matchups then worst. The second uses the word
 * "counter" on purpose: it is the word a player searches for, and it is exactly
 * what the worst list measures — the opponents this champion loses to most.
 */
export function championMatchupSentenceTokens(summary: ChampionBuildSummary): BuildSummarySentence[] {
  const name = summary.championName
  const matchups = summary.matchups
  if (!name || summary.games === 0 || !matchups) return []

  const lane = lanePhrase(summary.position)
  const sentences: BuildSummarySentence[] = []

  if (matchups.best.length) {
    sentences.push([
      plain(`${lane ? `${capitalise(lane)}, ` : ''}${name} mains fare best against `),
      ...opponentList(matchups.best),
      plain('.'),
    ])
  }
  if (matchups.worst.length) {
    sentences.push([
      plain(matchups.worst.length === 1
        ? `Their hardest matchup, the champion that counters ${name}, is `
        : `Their hardest matchups, the champions that counter ${name}, are `),
      ...opponentList(matchups.worst),
      plain('.'),
    ])
  }
  return sentences
}
