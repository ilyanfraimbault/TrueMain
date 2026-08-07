<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { isLoadingStatus } from '~/utils/async-data'

const props = defineProps<{
  nameTag: string
  /**
   * The account this page is about, as shown to the reader — the card names
   * them rather than saying "you", since a visitor is almost always looking at
   * someone else's profile.
   */
  playerName: string
  championId: number
  /** Champion display name; null while the client-only statics are still loading. */
  championName: string | null
  patch?: string | null
  position?: ChampionPosition | null
}>()

const { data, status, error } = usePlayerChampionPerformance(
  () => props.nameTag,
  () => props.championId,
  {
    patch: () => props.patch,
    position: () => props.position,
  },
)

// `idle` counts as loading: with a lazy client-only fetch the SSR shell and the
// first client tick sit in `idle`, and treating that as settled would flash the
// empty state before the request even started. Same rationale as MainsDivergence.
const isLoading = computed(() => isLoadingStatus(status.value) && !data.value)

const hasSample = computed(() => data.value?.averageScore != null)

const championLabel = computed(() => props.championName ?? 'this champion')

/**
 * Verdict on the average, on the same S→D ladder the champions list already
 * uses — a reader who knows the tier colours reads the headline number without
 * reading the word next to it. Colours are the `--color-tier-*` tokens, written
 * as static class strings so Tailwind's scanner emits them.
 *
 * 50 is the model's "average on every available component" midpoint, which is
 * what makes it a band boundary.
 */
const verdict = computed<{ label: string, tone: string } | null>(() => {
  const score = data.value?.averageScore
  if (score == null) return null
  if (score >= 75) return { label: 'Exceptional', tone: 'text-tier-s' }
  if (score >= 62) return { label: 'Strong', tone: 'text-tier-a' }
  if (score >= 50) return { label: 'Solid', tone: 'text-tier-b' }
  return { label: 'Developing', tone: 'text-tier-c' }
})

/**
 * The four sample figures, each with the one line that says what it actually
 * measures. The hints are the whole point of the block: "Top of team 25%" is
 * meaningless until you know it counts the games this player outscored their
 * own four teammates.
 */
const stats = computed(() => {
  const d = data.value
  if (!d) return []
  return [
    {
      key: 'best',
      label: 'Best',
      value: `${d.bestScore}`,
      hint: 'Highest score in a single game.',
    },
    {
      key: 'worst',
      label: 'Worst',
      value: `${d.worstScore}`,
      hint: 'Lowest score in a single game.',
    },
    {
      key: 'top-of-team',
      label: 'Top of team',
      value: formatPercentage(d.topOfTeamRate ?? 0, 0),
      hint: 'Games scored above all four teammates.',
    },
    {
      key: 'games',
      label: 'Games',
      value: `${d.games}`,
      hint: `Ranked games scored, out of the last ${d.window}.`,
    },
  ]
})
</script>

<template>
  <SectionCard
    title="Performance"
    :level="3"
  >
    <USkeleton
      v-if="isLoading"
      class="h-36 w-full rounded-lg"
    />

    <UAlert
      v-else-if="error"
      color="error"
      variant="soft"
      title="Failed to load performance"
      description="The performance score for this champion could not be loaded."
    />

    <!-- Honest empty state: the API returns the real counts even when it
         suppresses the averages, so the copy quotes the actual sample instead
         of a vague "not enough data". -->
    <p
      v-else-if="!data || !hasSample"
      class="glass rounded-lg px-4 py-8 text-center text-sm text-muted"
    >
      <template v-if="data && data.games > 0">
        Only {{ data.games }} {{ data.games === 1 ? 'game' : 'games' }} on record here —
        we score a champion from {{ data.minGames }} games up, so this one is not rated yet.
      </template>
      <template v-else>
        No ranked games on {{ championLabel }} to score yet.
      </template>
    </p>

    <!-- No inner card here: the section is already a card, and the headline is
         all that is left of the panel, so a second frame around it would be
         pure boxing. -->
    <div
      v-else-if="data"
      class="@container flex flex-col gap-4"
    >
      <div class="flex items-end gap-3">
        <span
          class="text-5xl font-semibold leading-none tabular-nums"
          :class="verdict?.tone"
        >
          {{ data.averageScore!.toFixed(1) }}
        </span>
        <div class="flex flex-col gap-1 pb-1">
          <span class="text-xs text-muted">/ 100 average</span>
          <span
            v-if="verdict"
            class="text-sm font-semibold"
            :class="verdict.tone"
          >
            {{ verdict.label }}
          </span>
        </div>
      </div>

      <!-- Container-relative, not viewport-relative (#967): this card lives in
           the narrow right rail on desktop and full-width on mobile, so the
           four figures only get their own column once the card itself is wide
           enough to keep each hint on two lines. -->
      <dl class="grid grid-cols-2 gap-x-6 gap-y-4 border-t border-default pt-4 @lg:grid-cols-4">
        <div
          v-for="stat in stats"
          :key="stat.key"
          class="flex flex-col gap-0.5"
        >
          <dt class="text-xs text-muted">{{ stat.label }}</dt>
          <dd class="text-lg font-semibold leading-none tabular-nums">{{ stat.value }}</dd>
          <dd class="text-[11px] leading-snug text-dimmed">{{ stat.hint }}</dd>
        </div>
      </dl>
    </div>
  </SectionCard>
</template>
