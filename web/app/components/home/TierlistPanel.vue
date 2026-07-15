<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '~/utils/positions'

// Homepage teaser of the champion tier list: the strongest rows of the
// active patch, linking through to the full /champions directory. Purely
// presentational — the page owns both fetches and passes them down.
const props = defineProps<{
  summaries: ChampionSummaryResponse[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
}>()

const ROW_COUNT = 8

// Best tier first, then games as the tiebreaker. Winrate would be the
// obvious second key, but it floats micro-sample rows (a 90% WR off-lane
// pick with a handful of games) to the very top — the opposite of the
// "honest sample sizes" promise. Most-played S-tiers read as the meta.
const TIER_ORDER: Record<string, number> = { S: 0, A: 1, B: 2, C: 3, D: 4 }

const rows = computed(() =>
  [...props.summaries]
    .sort((a, b) =>
      (TIER_ORDER[a.tier] ?? 9) - (TIER_ORDER[b.tier] ?? 9)
      || b.games - a.games,
    )
    .slice(0, ROW_COUNT)
    .map((summary) => {
      const champ = props.championsById.get(summary.championId)
      return {
        ...summary,
        name: champ?.name ?? `Champion ${summary.championId}`,
        iconUrl: champ?.iconUrl ?? '',
        positionOption: POSITION_BY_VALUE.get(summary.position),
      }
    }),
)
</script>

<template>
  <section
    class="glass rounded-2xl p-3 sm:p-4"
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

    <div
      v-if="pending"
      class="space-y-1"
      aria-hidden="true"
    >
      <div
        v-for="i in ROW_COUNT"
        :key="i"
        class="-mx-2 flex items-center gap-3 rounded-lg px-2 py-2"
      >
        <USkeleton class="size-9 rounded-lg" />
        <USkeleton class="h-4 w-32" />
        <USkeleton class="ml-auto h-4 w-24" />
      </div>
    </div>

    <ul
      v-else-if="rows.length > 0"
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
          class="glass-hover -mx-2 flex items-center gap-3 rounded-lg px-2 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
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
