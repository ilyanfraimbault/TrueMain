<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import { SAMPLE_PACE, SAMPLE_PACE_REFERENCE, sampleWinProbability } from '~/utils/desktop-landing-sample'

/**
 * One of the in-game overlay's panels, redrawn for `/download` (#2072) the
 * way the app draws it over its sample game (`desktop/app/app/components/overlay/*`):
 * same widths, type and colours, the sample's values. An illustration, so the
 * page labels it as a sample and hides it from assistive tech; the section's
 * copy says what each panel does.
 */
export type LandingOverlayPanelKind = 'next-item' | 'win-probability' | 'pace' | 'objectives' | 'lane-gold'
/** The panels drawn as a panel; the lane-gold plates sit on the scoreboard's rows instead (`LandingLanePlates`). */
export type LandingPanelKind = Exclude<LandingOverlayPanelKind, 'lane-gold'>

defineProps<{
  panel: LandingPanelKind
  patch: string | null
  items: Record<number, StaticItemData> | null
}>()

/** The app's panel widths, in points (`PANEL_WIDTHS`). */
const WIDTHS: Record<LandingPanelKind, number> = { 'next-item': 232, 'win-probability': 160, 'pace': 176, 'objectives': 292 }


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

</script>

<template>
  <div class="bg-default px-3 py-2.5 text-default" :style="{ width: `${WIDTHS[panel]}px` }">
    <DesktopLandingBuildPath v-if="panel === 'next-item'" :patch="patch" :items="items" />

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

    <DesktopLandingObjectives v-else />
  </div>
</template>
