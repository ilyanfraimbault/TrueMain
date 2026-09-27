import type { LeaderboardResponse, TruemainRow } from '~/types/truemains'

/**
 * The best true mains of one champion — the site's leaderboard filtered to it.
 * What the app has in place of a "pro builds" list: players who main the
 * champion, each with the keystone and first item they run on it.
 */
export function useChampionMains(championId: Ref<number | null>, size = 5) {
  const cache = useState<Record<number, TruemainRow[]>>('champion-mains', () => ({}))
  const pending = ref(false)

  const rows = computed(() => (championId.value ? cache.value[championId.value] ?? null : null))

  watch(championId, async (id) => {
    if (!id || cache.value[id]) return
    pending.value = true
    try {
      const answer = await apiGet<LeaderboardResponse>('/truemains', { championId: id, pageSize: size })
      cache.value = { ...cache.value, [id]: answer.rows }
    }
    catch {
      // A missing list is an empty section, not a broken build view.
    }
    finally {
      pending.value = false
    }
  }, { immediate: true })

  return { rows, pending }
}
