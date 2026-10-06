import type { LoadingView } from '~/types/loading'
import type { TruemainMark } from '~/utils/player-intel'
import { lookupPlayers, markOf } from '~/utils/player-intel'

interface MarksState {
  /** The players last asked about, as `lookupPlayers` lists them. */
  asked: string[]
  marks: TruemainMark[]
}

/**
 * The true-main marks of the game being played (#1910): which of its named
 * players TrueMain tracks as a true main of the champion they are on, asked
 * in one `GET /truemains/lookup` once the game's platform is known. Asked
 * again only when the roster names a player it had not — the loading board
 * fills progressively (#1869) — and kept in `useState`, so the loading board
 * and the Game page read the same answer and a page switch asks nothing.
 *
 * The mark is optional: a failed or slow answer leaves the boards as they are.
 *
 * Called by the page that follows the game (`pages/game.vue`); the boards read
 * the answer through `useTruemainMarkOf`.
 */
export function useTruemainMarks(view: MaybeRefOrGetter<LoadingView>) {
  const state = useMarksState()
  let request = 0

  watch(
    () => {
      const current = toValue(view)
      return { platformId: current.platformId, players: lookupPlayers(current.players) }
    },
    ({ platformId, players }) => {
      if (!players.length) {
        // No roster: the game is over, and the next one starts afresh.
        if (!toValue(view).players.length) state.value = { asked: [], marks: [] }
        return
      }
      if (!platformId || players.every(player => state.value.asked.includes(player))) return
      state.value = { ...state.value, asked: players }
      const asked = ++request
      lookup(platformId, players)
        .then((marks) => {
          if (asked === request) state.value = { ...state.value, marks }
        })
        .catch(() => {})
    },
    { immediate: true, deep: true },
  )
}

/** The mark of a loading line, from the answer `useTruemainMarks` keeps; null for none. */
export function useTruemainMarkOf() {
  const state = useMarksState()
  return (line: Parameters<typeof markOf>[1]) => markOf(state.value.marks, line)
}

function useMarksState() {
  return useState<MarksState>('truemain-marks', () => ({ asked: [], marks: [] }))
}

async function lookup(platformId: string, players: string[]): Promise<TruemainMark[]> {
  if (import.meta.dev && !insideTauri()) {
    const { devTruemainMarks } = await import('~/utils/loading-dev')
    return devTruemainMarks()
  }
  const answer = await apiGet<{ players: TruemainMark[] }>('/truemains/lookup', { platformId, player: players }, { background: true })
  return answer.players
}
