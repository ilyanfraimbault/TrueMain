import type { Ref } from 'vue'
import type { GameState } from '~/types/game'
import type { PaceBenchmarkResponse, PaceReference } from '~/utils/pace-benchmark'
import { readRankHistory } from '~/utils/lp-history'
import { paceReference } from '~/utils/pace-benchmark'

/**
 * The tier reference for the overlay's pace panel (#1912): the benchmark of
 * our lane, read once per position, and our Solo/Duo tier as this machine last
 * saw it (`utils/lp-history`, written each time the app reads the player's
 * record). No Solo/Duo tier — unranked, or Flex only — means no comparison.
 */
export function usePaceBenchmark(game: Ref<GameState | null>) {
  const { state } = useLcuState()
  const benchmarks = useState<Record<string, PaceBenchmarkResponse | null>>('pace-benchmarks', () => ({}))

  const position = computed(() => game.value?.players.find(player => player.isMe)?.position || null)

  // The history is written by the app's window; an overlay window hears of it
  // through the storage event, so a standing read mid-session reaches the panel.
  const stored = ref(0)
  const onStorage = () => stored.value++
  onMounted(() => window.addEventListener('storage', onStorage))
  onBeforeUnmount(() => window.removeEventListener('storage', onStorage))
  const tier = computed(() => {
    void stored.value
    const riotId = state.value.riotId
    return riotId ? readRankHistory(riotId).at(-1)?.tier ?? null : null
  })

  watch(position, async (current) => {
    if (!current || current in benchmarks.value) return
    benchmarks.value = { ...benchmarks.value, [current]: null }
    try {
      const answer = await apiGet<PaceBenchmarkResponse>('/benchmarks/pace', { position: current }, { background: true })
      benchmarks.value = { ...benchmarks.value, [current]: answer }
    }
    catch {
      // Unreachable: the panel shows our pace alone, and the next game asks again.
      const { [current]: _, ...rest } = benchmarks.value
      benchmarks.value = rest
    }
  }, { immediate: true })

  const reference = computed<PaceReference | null>(() => {
    const current = position.value
    return paceReference(current ? benchmarks.value[current] ?? null : null, tier.value, game.value?.pace.samples.at(-1) ?? null)
  })

  return { reference }
}
