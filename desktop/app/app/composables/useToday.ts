import type { Ref } from 'vue'
import { computed, onScopeDispose, ref } from 'vue'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { PlayerGame } from '~/types/record'
import type { DaySummary } from '~/utils/today'
import { todayOf } from '~/utils/today'

const MINUTE_MS = 60_000

/**
 * The dashboard's Today card (`utils/today`), kept current: re-read whenever the
 * games or the LP history change, and each minute, so the day turns at midnight
 * even while no game is played. The clock stops with the scope that started it.
 */
export function useToday(
  games: Ref<PlayerGame[]>,
  allGames: Ref<PlayerGame[]>,
  history: Ref<RankHistoryEntry[]>,
): Ref<DaySummary | null> {
  const now = ref(new Date())
  const clock = setInterval(() => { now.value = new Date() }, MINUTE_MS)
  onScopeDispose(() => clearInterval(clock))
  return computed(() => todayOf(games.value, allGames.value, history.value, now.value))
}
