import type { Ref } from 'vue'
import type { FormReference } from '#common/utils/match-form'
import type { FormBenchmarkResponse } from '~/utils/form-benchmark'
import { formReference } from '~/utils/form-benchmark'

/**
 * The tier reference for the dashboard's form tiles (#2076): the benchmark of
 * the player's main role, read once per position for the app's life, set
 * against their Solo/Duo tier. No tier or no main role — no reference.
 */
export function useFormBenchmark(position: Ref<string | null>, tier: Ref<string | null>) {
  const benchmarks = useState<Record<string, FormBenchmarkResponse | null>>('form-benchmarks', () => ({}))

  watch(position, async (current) => {
    if (!current || current in benchmarks.value) return
    benchmarks.value = { ...benchmarks.value, [current]: null }
    try {
      const answer = await apiGet<FormBenchmarkResponse>('/benchmarks/form', { position: current }, { background: true })
      benchmarks.value = { ...benchmarks.value, [current]: answer }
    }
    catch {
      // Unreachable: the tiles show the player's own form alone, and the next visit asks again.
      const { [current]: _, ...rest } = benchmarks.value
      benchmarks.value = rest
    }
  }, { immediate: true })

  const reference = computed<FormReference | null>(() => {
    const current = position.value
    return formReference(current ? benchmarks.value[current] ?? null : null, tier.value)
  })

  return { reference }
}
