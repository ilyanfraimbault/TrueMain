<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import {
  SAMPLE_ALLIES, SAMPLE_BUILD_ORDER, SAMPLE_ENEMIES, SAMPLE_ITEMS, SAMPLE_NEXT_ITEM, SAMPLE_PACE, SAMPLE_PACE_REFERENCE,
  championIconUrl, sampleWinProbability,
} from '~/utils/desktop-landing-sample'

/**
 * One of the in-game overlay's four panels, redrawn for `/download` (#2072) the
 * way the app draws it over its sample game (`desktop/app/app/components/overlay/*`):
 * same widths, type and colours, the sample's values. An illustration, so the
 * page labels it as a sample and hides it from assistive tech; the section's
 * copy says what each panel does.
 */
export type LandingOverlayPanelKind = 'next-item' | 'win-probability' | 'pace' | 'item-value'

const props = defineProps<{
  panel: LandingOverlayPanelKind
  patch: string | null
  items: Record<number, StaticItemData> | null
}>()

/** The app's panel widths, in points (`PANEL_WIDTHS`). */
const WIDTHS: Record<LandingOverlayPanelKind, number> = { 'next-item': 232, 'win-probability': 160, 'pace': 176, 'item-value': 300 }

const canonicalIcon = useCanonicalIcon()
const itemIcon = (id: number) => (props.patch ? `https://ddragon.leagueoflegends.com/cdn/${props.patch}/img/item/${id}.png` : null)
const nextItemName = computed(() => props.items?.[SAMPLE_NEXT_ITEM.itemId]?.name ?? 'Infinity Edge')

const ours = Math.round(sampleWinProbability() * 100)

const last = SAMPLE_PACE[SAMPLE_PACE.length - 1]!
const csPerMinute = (last.cs / last.minute).toFixed(1)
const goldPerMinute = Math.round(last.gold / last.minute)

// The pace curve on one scale with the tier's median, as `OverlayStats` draws it.
const CURVE = { width: 72, height: 26 }
const curve = (() => {
  const points = SAMPLE_PACE.map(sample => sample.cs / sample.minute)
  const values = [...points, ...SAMPLE_PACE_REFERENCE.csCurve]
  const low = Math.min(...values)
  const range = Math.max(...values) - low || 1
  const first = SAMPLE_PACE[0]!.minute
  const span = last.minute - first
  const toLine = (line: readonly number[]) => line
    .map((value, index) => `${(((SAMPLE_PACE[index]!.minute - first) / span) * CURVE.width).toFixed(1)},${(2 + (1 - (value - low) / range) * (CURVE.height - 4)).toFixed(1)}`)
    .join(' ')
  const line = toLine(points)
  const end = line.split(' ').at(-1)!.split(',').map(Number)
  return { line, area: `0,${CURVE.height} ${line} ${CURVE.width},${CURVE.height}`, median: toLine(SAMPLE_PACE_REFERENCE.csCurve), end: { x: end[0]!, y: end[1]! } }
})()
const gradient = `landing-pace-${useId()}`

/** Item gold per lane, consumables and trinkets out (#1752's rule); null until the item map is read. */
const lanes = computed(() => {
  const items = props.items
  if (!items) return null
  const gold = (ids: number[]) => ids.reduce((total, id) => {
    const item = items[id]
    return !item || item.tags?.includes('Consumable') || item.tags?.includes('Trinket') ? total : total + item.totalGold
  }, 0)
  return SAMPLE_ALLIES.map((ally, index) => ({
    ally, enemy: SAMPLE_ENEMIES[index]!, ours: gold(SAMPLE_ITEMS.ally[index]!), theirs: gold(SAMPLE_ITEMS.enemy[index]!),
  }))
})
const teams = computed(() => lanes.value?.reduce((sum, lane) => ({ ours: sum.ours + lane.ours, theirs: sum.theirs + lane.theirs }), { ours: 0, theirs: 0 }) ?? null)
const number = (value: number | undefined) => (value === undefined ? '–' : formatCount(value))
</script>

<template>
  <div class="bg-default px-3 py-2.5 text-default" :style="{ width: `${WIDTHS[panel]}px` }">
    <div v-if="panel === 'next-item'" class="space-y-2">
      <div class="flex items-center gap-2.5">
        <img v-if="itemIcon(SAMPLE_NEXT_ITEM.itemId)" :src="canonicalIcon(itemIcon(SAMPLE_NEXT_ITEM.itemId))" alt="" width="36" height="36" class="size-9 shrink-0 rounded-md ring-1 ring-primary/60">
        <span v-else class="size-9 shrink-0 rounded-md bg-elevated" />
        <div class="min-w-0 flex-1">
          <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
            <DesktopLandingMark class="size-2.5" /> Next item
          </p>
          <p class="truncate text-[13px] font-semibold leading-tight text-highlighted">{{ nextItemName }}</p>
          <p class="text-[11px] tabular-nums text-muted"><span class="text-stat-gold">{{ SAMPLE_NEXT_ITEM.missing }}</span> gold to go</p>
        </div>
      </div>
      <!-- The build row (`GameBuildOrder`, #2101): done dimmed, the next ringed, the rest to come. -->
      <div class="flex items-center gap-1" data-build-row>
        <template v-for="step in SAMPLE_BUILD_ORDER" :key="step.itemId">
          <img
            v-if="itemIcon(step.itemId)"
            :src="canonicalIcon(itemIcon(step.itemId))"
            alt=""
            width="22"
            height="22"
            class="size-[22px] rounded"
            :class="{ 'opacity-35 grayscale': step.state === 'Completed', 'ring-2 ring-primary': step.state === 'Next', 'opacity-80': step.state === 'Upcoming' }"
          >
          <span v-else class="block size-[22px] rounded bg-elevated" />
        </template>
      </div>
    </div>

    <div v-else-if="panel === 'win-probability'" class="flex flex-col gap-1.5">
      <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
        <DesktopLandingMark class="size-2.5" /> Win probability
      </p>
      <div class="flex items-end justify-between gap-3">
        <span class="stat-value text-2xl leading-none tabular-nums text-ally">{{ ours }}<span class="text-sm">%</span></span>
        <span class="stat-value text-2xl leading-none tabular-nums text-enemy">{{ 100 - ours }}<span class="text-sm">%</span></span>
      </div>
      <div class="flex h-1 overflow-hidden rounded-full bg-elevated">
        <span class="bg-ally/80" :style="{ width: `${ours}%` }" />
        <span class="flex-1 bg-enemy/80" />
      </div>
    </div>

    <div v-else-if="panel === 'pace'" class="flex flex-col gap-2">
      <div class="flex items-end justify-between gap-3">
        <div class="flex flex-col">
          <span class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">CS/min</span>
          <span class="stat-value text-xl leading-none tabular-nums text-highlighted">{{ csPerMinute }}</span>
        </div>
        <svg :width="CURVE.width" :height="CURVE.height" :viewBox="`0 0 ${CURVE.width} ${CURVE.height}`" class="overflow-visible text-primary" aria-hidden="true">
          <defs>
            <linearGradient :id="gradient" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stop-color="currentColor" stop-opacity="0.28" />
              <stop offset="1" stop-color="currentColor" stop-opacity="0" />
            </linearGradient>
          </defs>
          <polygon :points="curve.area" :fill="`url(#${gradient})`" />
          <polyline :points="curve.median" fill="none" class="text-data-mid" stroke="currentColor" stroke-opacity="0.6" stroke-width="1" stroke-dasharray="2 2" />
          <polyline :points="curve.line" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round" />
          <circle :cx="curve.end.x" :cy="curve.end.y" r="2" fill="currentColor" />
        </svg>
      </div>
      <span class="-mt-1 flex items-center gap-1 whitespace-nowrap text-[10px] leading-none tabular-nums text-muted">
        <UIcon name="i-lucide-arrow-up" class="size-2.5 shrink-0 text-data-good" />
        {{ SAMPLE_PACE_REFERENCE.tier }} {{ SAMPLE_PACE_REFERENCE.cs.median.toFixed(1) }}
      </span>
      <div class="flex flex-col border-t border-default pt-2">
        <span class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">Gold/min</span>
        <span class="stat-value text-xl leading-none tabular-nums text-stat-gold">{{ goldPerMinute }}</span>
        <span class="mt-1 flex items-center gap-1 whitespace-nowrap text-[10px] leading-none tabular-nums text-muted">
          <UIcon name="i-lucide-equal" class="size-2.5 shrink-0 text-data-mid" />
          {{ SAMPLE_PACE_REFERENCE.tier }} {{ SAMPLE_PACE_REFERENCE.gold.median }}
        </span>
      </div>
    </div>

    <div v-else class="flex flex-col gap-2">
      <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-3">
        <span class="stat-value justify-self-end text-lg leading-none text-ally">{{ number(teams?.ours) }}</span>
        <DesktopLandingGoldGap :gap="teams ? teams.ours - teams.theirs : 0" large />
        <span class="stat-value text-lg leading-none text-enemy">{{ number(teams?.theirs) }}</span>
      </div>
      <ul class="flex flex-col gap-1 border-t border-default pt-2">
        <li v-for="(ally, index) in SAMPLE_ALLIES" :key="ally" class="grid grid-cols-[1.25rem_1fr_auto_1fr_1.25rem] items-center gap-2">
          <img v-if="patch" :src="canonicalIcon(championIconUrl(ally, patch))" alt="" width="20" height="20" class="size-5 rounded">
          <span v-else class="size-5 rounded bg-elevated" />
          <span class="justify-self-end text-[11px] tabular-nums text-muted">{{ number(lanes?.[index]?.ours) }}</span>
          <DesktopLandingGoldGap :gap="lanes ? lanes[index]!.ours - lanes[index]!.theirs : 0" />
          <span class="text-[11px] tabular-nums text-muted">{{ number(lanes?.[index]?.theirs) }}</span>
          <img v-if="patch" :src="canonicalIcon(championIconUrl(SAMPLE_ENEMIES[index]!, patch))" alt="" width="20" height="20" class="size-5 rounded">
          <span v-else class="size-5 rounded bg-elevated" />
        </li>
      </ul>
    </div>
  </div>
</template>
