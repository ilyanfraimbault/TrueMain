<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { winRateTone } from '#common/utils/rate-tone'
import { POSITION_BY_VALUE } from '#common/utils/positions'

// Homepage teaser of the champion tier list: the strongest rows of the
// active patch, linking through to the full /champions directory. Purely
// presentational — the page owns the fetch and passes the already-sorted,
// already-limited rows down (`GET /champions/overview`, #972); this
// component only enriches them with name/icon and renders them. Loading is
// the page's own concern too — see `HomeTierlistPanelSkeleton`, rendered by
// the page instead of by this component, so there is exactly one place that
// decides "is this ready yet".
const props = defineProps<{
  topRows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
}>()

const { pathFor } = useChampionSlugs()

// The pick-rate bars share one scale, the most picked row's, so their lengths compare.
const maxPickRate = computed(() => Math.max(...props.topRows.map(row => row.pickRate), 0) || 1)

const rows = computed(() =>
  props.topRows.map((row) => {
    const champ = props.championsById.get(row.championId)
    return {
      ...row,
      name: champ?.name ?? `Champion ${row.championId}`,
      iconUrl: champ?.iconUrl ?? '',
      positionOption: POSITION_BY_VALUE.get(row.position),
    }
  }),
)
</script>

<template>
  <section
    class="flex flex-col gap-3"
    aria-labelledby="home-tierlist-title"
  >
    <header class="flex min-h-6 items-center justify-between">
      <h3
        id="home-tierlist-title"
        class="text-xs font-semibold uppercase tracking-wide text-muted"
      >
        Strongest picks
      </h3>
      <UButton
        to="/champions"
        color="primary"
        variant="link"
        size="xs"
        trailing-icon="i-lucide-arrow-right"
        label="Full tier list"
      />
    </header>

    <div class="surface flex flex-1 flex-col rounded-2xl py-1.5">
      <template v-if="rows.length > 0">
        <!-- Column heads, on the same grid as the rows below. -->
        <div
          class="grid grid-cols-[1rem_minmax(0,1fr)_2rem_2rem_3.5rem] items-center gap-3 px-4 py-2 sm:grid-cols-[1rem_minmax(0,1fr)_2rem_2rem_3.5rem_7rem]"
          aria-hidden="true"
        >
          <span class="stat-label">#</span>
          <span class="stat-label">Champion</span>
          <span class="stat-label text-center">Lane</span>
          <span class="stat-label text-center">Tier</span>
          <span class="stat-label text-right">WR</span>
          <span class="stat-label hidden sm:block">Pick rate</span>
        </div>
        <ul class="divide-y divide-default/60">
          <li
            v-for="(row, index) in rows"
            :key="`${row.championId}-${row.position}`"
          >
            <NuxtLink
              :to="{ path: pathFor(row.championId), query: { position: row.position } }"
              class="grid grid-cols-[1rem_minmax(0,1fr)_2rem_2rem_3.5rem] items-center gap-3 px-4 py-2 transition-colors hover:bg-accented focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary sm:grid-cols-[1rem_minmax(0,1fr)_2rem_2rem_3.5rem_7rem]"
            >
              <span class="text-sm tabular-nums text-dimmed">{{ index + 1 }}</span>
              <span class="flex min-w-0 items-center gap-2.5">
                <SkeletonImage
                  :src="row.iconUrl"
                  :alt="row.name"
                  width="32"
                  height="32"
                  class="size-8 shrink-0 rounded-md"
                />
                <span class="truncate text-sm font-medium text-highlighted">{{ row.name }}</span>
              </span>
              <span class="flex justify-center">
                <SkeletonImage
                  v-if="row.positionOption?.iconUrl"
                  :src="row.positionOption.iconUrl"
                  :alt="row.positionOption.label"
                  :title="row.positionOption.label"
                  :width="16"
                  :height="16"
                  transparent
                  class="size-4"
                />
              </span>
              <span class="flex justify-center"><TierBadge :tier="row.tier" /></span>
              <span
                class="text-right text-sm font-semibold tabular-nums"
                :class="winRateTone(row.winRate)"
              >{{ formatPercentage(row.winRate) }}</span>
              <span class="hidden items-center gap-2 sm:flex">
                <UProgress
                  :model-value="(row.pickRate / maxPickRate) * 100"
                  size="xs"
                  class="flex-1"
                />
                <span class="w-9 text-right text-xs tabular-nums text-muted">{{ formatPercentage(row.pickRate) }}</span>
              </span>
            </NuxtLink>
          </li>
        </ul>
      </template>

      <UEmpty
        v-else
        size="sm"
        variant="naked"
        icon="i-lucide-trending-up"
        description="No champion stats for this patch yet."
      />
    </div>
  </section>
</template>
