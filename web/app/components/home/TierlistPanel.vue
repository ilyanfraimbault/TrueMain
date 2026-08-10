<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '~/utils/positions'

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
    class="surface rounded-2xl p-3 sm:p-4"
    aria-labelledby="home-tierlist-title"
  >
    <header class="flex items-center justify-between gap-3 pb-3">
      <h2
        id="home-tierlist-title"
        class="text-sm font-semibold text-default"
      >
        Tier list
      </h2>
      <UButton
        to="/champions"
        color="neutral"
        variant="ghost"
        size="sm"
        trailing-icon="i-lucide-arrow-right"
        label="Full tier list"
      />
    </header>

    <ul
      v-if="rows.length > 0"
      class="space-y-1"
    >
      <li
        v-for="(row, index) in rows"
        :key="`${row.championId}-${row.position}`"
      >
        <!-- `-mx-2 px-2` bleeds the hover background slightly into the panel
             padding while keeping the rank flush with the section header
             instead of indenting the whole row. -->
        <NuxtLink
          :to="{ path: `/champions/${row.championId}`, query: { position: row.position } }"
          class="surface-hover -mx-2 flex items-center gap-3 rounded-lg px-2 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <span class="w-4 shrink-0 text-center text-xs tabular-nums text-dimmed">
            {{ index + 1 }}
          </span>

          <SkeletonImage
            :src="row.iconUrl"
            :alt="row.name"
            width="36"
            height="36"
            class="size-9 shrink-0 rounded-lg"
          />

          <span class="min-w-0 flex-1 truncate font-medium">{{ row.name }}</span>

          <SkeletonImage
            v-if="row.positionOption?.iconUrl"
            :src="row.positionOption.iconUrl"
            :alt="row.positionOption.label"
            :width="18"
            :height="18"
            class="size-[18px] shrink-0 opacity-80"
          />

          <TierBadge
            :tier="row.tier"
            class="shrink-0"
          />

          <span class="w-14 shrink-0 text-right text-sm font-semibold tabular-nums">
            {{ formatPercentage(row.winRate) }}
            <span class="block text-[10px] font-normal uppercase tracking-wide text-muted">WR</span>
          </span>
          <span class="hidden w-14 shrink-0 text-right text-sm font-semibold tabular-nums text-muted sm:block">
            {{ formatPercentage(row.pickRate) }}
            <span class="block text-[10px] font-normal uppercase tracking-wide text-muted">PR</span>
          </span>
        </NuxtLink>
      </li>
    </ul>

    <p
      v-else
      class="px-3 py-8 text-center text-sm text-muted"
    >
      No champion stats for this patch yet.
    </p>
  </section>
</template>
