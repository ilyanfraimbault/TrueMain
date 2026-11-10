import type { FormBenchmarkResponse, FormBenchmarkTierReadModel } from '#shared/contract/api'
import type { FormReference } from '#common/utils/match-form'

/**
 * The form benchmark (#2076): the end-of-game figures of a tier's laners, read
 * from `GET /benchmarks/form?position=` and set beside the dashboard's form
 * tiles. As with the pace benchmark (#1912), the API answers every tier of a
 * position and the tier is picked here, so the rank read from the client is
 * never sent (#1805).
 */

export type { FormBenchmarkResponse }
/** Every figure null under the sample floor. */
export type FormBenchmarkTier = FormBenchmarkTierReadModel

/**
 * What the tiles are set against for a player of `tier`: the tier's medians
 * on the benchmark's lane. Null without a Solo/Duo tier, or when the tier has
 * no game there; a metric under the sample floor is left out, so its tile
 * shows no reference rather than a thin one.
 */
export function formReference(benchmark: FormBenchmarkResponse | null, tier: string | null): FormReference | null {
  if (!benchmark || !tier) return null
  const row = benchmark.tiers.find(entry => entry.tier === tier.toUpperCase())
  if (!row) return null
  const medians: FormReference['medians'] = {}
  if (row.kda) medians.kda = row.kda.median
  if (row.killParticipation) medians.killParticipation = row.killParticipation.median
  if (row.csPerMinute) medians.csPerMinute = row.csPerMinute.median
  if (row.deaths) medians.deaths = row.deaths.median
  if (!Object.keys(medians).length) return null
  return { tier: row.tier, position: benchmark.position, samples: row.samples, medians }
}
