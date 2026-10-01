<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage, formatPercentageOrDash } from '~~/shared/utils/ddragon'
import { formatCount } from '~~/shared/utils/counts'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { banRateTone, pickRateTone, winRateTone } from '~/utils/rate-tone'
import { CHAMPIONS_TABLE_GRID } from '~/utils/list-tables'

// One (champion, lane) line of the /champions table (#1726), laid on the
// table's grid so each figure sits under its column header — the desktop
// app's champion table, plus the site's top build (keystone and core path).
//
// Per #147 the line is a button-style target rather than a link: a
// `div role="button"` pushed programmatically, activated by Enter and Space.
// The item and rune icons inside keep their hover cards.
const props = defineProps<{
  row: ChampionSummaryResponse & { name: string, iconUrl: string }
  /** 1-based place in the filtered list — the directory is ordered by pick rate. */
  rank: number
  destination: RouteLocationRaw
  runeTree: RuneTreeResponse | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
}>()

const router = useRouter()
const activate = () => void router.push(props.destination)

const { perk, perkStyle, item } = useBuildResolvers(() => props.runeTree, () => props.itemsMap ?? undefined)

const lane = computed(() => POSITION_BY_VALUE.get(props.row.position) ?? null)
const keystone = computed(() => (props.row.topBuild ? perk(props.row.topBuild.primaryKeystoneId) : null))
const secondaryStyle = computed(() => (props.row.topBuild ? perkStyle(props.row.topBuild.secondaryStyleId) : null))

// The consensus path capped at six — the full ADC core and the detail page's
// worst case; the analyzer can emit a seventh, which would widen the column.
const buildPath = computed(() => (props.row.topBuild?.itemPath ?? []).slice(0, 6))
</script>

<template>
  <div
    role="button"
    tabindex="0"
    :aria-label="`View ${row.name} builds`"
    class="grid h-12 cursor-pointer items-center gap-2 border-b border-default/60 px-3 transition-colors last:border-b-0 hover:bg-accented focus:outline-none focus-visible:bg-accented focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
    :class="CHAMPIONS_TABLE_GRID"
    @click="activate"
    @keydown.enter.prevent="activate"
    @keydown.space.prevent="activate"
  >
    <span class="text-xs font-semibold tabular-nums text-muted @xl:text-sm">{{ rank }}</span>

    <div class="flex min-w-0 items-center gap-2 @xl:gap-2.5">
      <SkeletonImage :src="row.iconUrl" :alt="row.name" width="36" height="36" class="size-7 shrink-0 rounded @xl:size-9" />
      <span class="truncate text-sm font-semibold text-highlighted">{{ row.name }}</span>
    </div>

    <SkeletonImage
      v-if="lane?.iconUrl"
      :src="lane.iconUrl"
      :alt="lane.label"
      :title="lane.label"
      :width="20"
      :height="20"
      class="mx-auto size-5"
    />
    <span v-else />

    <span class="flex justify-center"><TierBadge :tier="row.tier" /></span>

    <div class="hidden justify-center @2xl:flex">
      <div v-if="keystone" class="relative size-7">
        <GameTooltipPerkIcon :perk="keystone" :width="28" :height="28" class="size-7 rounded-full" />
        <GameTooltipPerkStyleIcon
          v-if="secondaryStyle"
          :style="secondaryStyle"
          :width="14"
          :height="14"
          class="absolute -bottom-0.5 -right-1.5 size-3.5"
        />
      </div>
      <span v-else class="text-sm text-dimmed">—</span>
    </div>

    <div class="hidden items-center gap-0.5 @4xl:flex">
      <template v-for="(itemId, index) in buildPath" :key="`${itemId}-${index}`">
        <GameTooltipItemIcon :item="item(itemId)" :width="24" :height="24" class="size-6 rounded" />
        <UIcon v-if="index < buildPath.length - 1" name="i-lucide-chevron-right" class="size-3 shrink-0 text-dimmed" />
      </template>
      <span v-if="!buildPath.length" class="text-sm text-dimmed">—</span>
    </div>

    <span class="text-right text-sm font-semibold tabular-nums" :class="winRateTone(row.winRate)">{{ formatPercentage(row.winRate, 1) }}</span>
    <span class="text-right text-sm tabular-nums" :class="pickRateTone(row.pickRate)">{{ formatPercentage(row.pickRate, 1) }}</span>
    <!-- A dash on patches predating ban ingestion (#920): "not observed" is not 0%. -->
    <span class="hidden text-right text-sm tabular-nums @xl:block" :class="banRateTone(row.banRate)">{{ formatPercentageOrDash(row.banRate, 1) }}</span>
    <span class="hidden text-right text-sm tabular-nums text-muted @xl:block">{{ formatCount(row.games) }}</span>
  </div>
</template>
