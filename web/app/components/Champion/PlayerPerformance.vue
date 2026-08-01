<script setup lang="ts">
import type { PerformanceComponentKind } from '~~/shared/types/performance'
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
 * Human wording per component. The parenthetical on the two lead components is
 * load-bearing: they are the same measurement over two different phases, and
 * without the minute ranges a reader cannot tell them apart.
 */
const COMPONENT_LABELS: Record<PerformanceComponentKind, string> = {
  Combat: 'Combat (KDA)',
  KillParticipation: 'Kill participation',
  DamageShare: 'Damage share',
  GoldShare: 'Gold share',
  Farming: 'Farming',
  Vision: 'Vision',
  Laning: 'Laning leads (≤ 15 min)',
  MidGame: 'Mid-game leads (> 15 min)',
  Roam: 'Roaming',
}

// Only components the role actually grades, heaviest first — the order the
// score itself cares about, so the rows a reader scans first are the ones that
// moved the number most. A component the role zeroes out (roam for a jungler)
// is dropped entirely rather than shown as a permanent 0.
const gradedComponents = computed(() =>
  (data.value?.components ?? [])
    .filter(c => c.weight > 0)
    .map(c => ({
      ...c,
      label: COMPONENT_LABELS[c.kind] ?? c.kind,
    }))
    .sort((a, b) => b.weight - a.weight),
)

/**
 * Verdict on the average. Deliberately coarse — four bands, and only the top
 * one gets the brand accent, so the emphasis lands on a genuinely strong player
 * instead of colouring every row. 50 is the model's "average on every available
 * component" midpoint, which is what makes it the band boundary.
 */
const verdict = computed<{ label: string, tone: string } | null>(() => {
  const score = data.value?.averageScore
  if (score == null) return null
  if (score >= 75) return { label: 'Exceptional', tone: 'text-primary' }
  if (score >= 62) return { label: 'Strong', tone: 'text-default' }
  if (score >= 50) return { label: 'Solid', tone: 'text-muted' }
  return { label: 'Developing', tone: 'text-muted' }
})

const barWidth = (value: number | null) =>
  value === null ? '0%' : `${Math.round(Math.min(1, Math.max(0, value)) * 100)}%`
</script>

<template>
  <SectionCard
    title="Performance"
    :subtitle="`How ${playerName} has been playing ${championLabel}, scored game by game.`"
    :level="3"
  >
    <USkeleton
      v-if="isLoading"
      class="h-56 w-full rounded-lg"
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

    <div
      v-else-if="data"
      class="flex flex-col gap-5"
    >
      <!-- Headline: the average, its verdict, and the sample it came from. -->
      <div class="glass flex flex-wrap items-end justify-between gap-4 rounded-lg p-4">
        <div class="flex items-end gap-3">
          <span class="text-4xl font-semibold leading-none tabular-nums text-primary">
            {{ data.averageScore!.toFixed(1) }}
          </span>
          <div class="flex flex-col gap-0.5 pb-0.5">
            <span class="text-xs text-muted">/ 100 average</span>
            <span
              v-if="verdict"
              class="text-sm font-medium"
              :class="verdict.tone"
            >
              {{ verdict.label }}
            </span>
          </div>
        </div>

        <dl class="flex flex-wrap gap-x-6 gap-y-1 text-xs">
          <div class="flex flex-col gap-0.5">
            <dt class="text-muted">Best</dt>
            <dd class="font-semibold tabular-nums">{{ data.bestScore }}</dd>
          </div>
          <div class="flex flex-col gap-0.5">
            <dt class="text-muted">Worst</dt>
            <dd class="font-semibold tabular-nums">{{ data.worstScore }}</dd>
          </div>
          <div class="flex flex-col gap-0.5">
            <dt class="text-muted">Top of team</dt>
            <dd class="font-semibold tabular-nums">
              {{ formatPercentage(data.topOfTeamRate ?? 0, 0) }}
            </dd>
          </div>
          <div class="flex flex-col gap-0.5">
            <dt class="text-muted">Games</dt>
            <dd class="font-semibold tabular-nums">{{ data.games }}</dd>
          </div>
        </dl>
      </div>

      <!-- Per-component breakdown. Each bar is the mean 0..1 grade; the
           midpoint tick marks 0.5, which is what an even lane / an average
           share scores, so a reader can see at a glance which axes are above
           the model's own middle. -->
      <ul class="flex flex-col gap-2.5">
        <li
          v-for="component in gradedComponents"
          :key="component.kind"
          class="flex flex-col gap-1"
        >
          <div class="flex items-baseline justify-between gap-2 text-xs">
            <span class="text-muted">{{ component.label }}</span>
            <span class="flex items-baseline gap-2">
              <span
                v-if="component.games < data.games"
                class="text-[11px] text-dimmed"
                :title="`Averaged over the ${component.games} of ${data.games} games where this signal was available.`"
              >
                {{ component.games }}/{{ data.games }} games
              </span>
              <span
                class="font-semibold tabular-nums"
                :class="component.value === null ? 'text-dimmed' : ''"
              >
                {{ component.value === null ? '–' : formatPercentage(component.value, 0) }}
              </span>
            </span>
          </div>
          <div class="relative h-2 w-full overflow-hidden rounded-full bg-elevated">
            <div
              class="absolute inset-y-0 left-0 rounded-full bg-gradient-to-r from-rosegold-600/70 to-rosegold-400 transition-[width] duration-500"
              :style="{ width: barWidth(component.value) }"
            />
            <div
              class="absolute inset-y-0 left-1/2 w-px bg-default/60"
              aria-hidden="true"
            />
          </div>
        </li>
      </ul>

      <p class="text-[11px] text-dimmed">
        Scored over {{ playerName }}'s last {{ data.window }} ranked games on
        {{ championLabel }}. A component with no data in a game lowers its own
        sample, never its average.
      </p>
    </div>
  </SectionCard>
</template>
