import type { ChampionBuildResponse } from '~/types/build'

/**
 * A champion's builds on one lane — the site's champion page endpoint, one
 * build per first completed item, most played first. Answers are kept for the
 * session: the builds move with a patch, and flicking between champions in a
 * draft should not ask twice for the same one.
 */
export function useChampionBuilds(championId: Ref<number | null>, position: Ref<string | null>) {
  const cache = useState<Record<string, ChampionBuildResponse>>('champion-builds', () => ({}))
  const pending = ref(false)
  const error = ref<string | null>(null)

  const key = computed(() => (championId.value && position.value ? `${championId.value}:${position.value}` : null))
  const response = computed(() => (key.value ? cache.value[key.value] ?? null : null))

  let latest = 0

  async function fetchBuilds(champion: number, lane: string): Promise<ChampionBuildResponse> {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<ChampionBuildResponse>('champion_build', { championId: champion, position: lane, opponentChampionId: null })
    }
    return await $fetch<ChampionBuildResponse>(`/api/champions/${champion}`, { query: { position: lane } })
  }

  watch(key, async (next) => {
    error.value = null
    if (!next || cache.value[next]) return
    const id = ++latest
    pending.value = true
    try {
      const answer = await trackLoad(fetchBuilds(championId.value!, position.value!))
      cache.value = { ...cache.value, [next]: answer }
    }
    catch (cause) {
      if (id === latest) error.value = String(cause)
    }
    finally {
      if (id === latest) pending.value = false
    }
  }, { immediate: true })

  return { response, pending, error }
}
