<script setup lang="ts">
import type { RankChartPoint } from '#common/utils/rank-chart'
import { tierHex } from '#common/utils/tiers'
import {
  areaFillOf,
  crestBands,
  lineStrokeOf,
  rankChartDateLabel,
  rankChartYDomain,
  tierGradientStops,
} from '#common/utils/rank-chart'

// The ranked card's LP curve: one series of rank scores, coloured by tier along
// the x-axis (`utils/rank-chart.ts` says why), with the tier crests as its
// y-axis. `ChartsAreaChart` is the host's: vue-chrts on the site, plain SVG in
// the app.
const props = defineProps<{
  points: RankChartPoint[]
  currentTier: string | null
}>()

const CHART_HEIGHT = 150

const categories = computed(() => ({
  score: { name: 'LP', color: tierHex(props.currentTier) },
}))

const stops = computed(() => tierGradientStops(props.points))
const yDomain = computed(() => rankChartYDomain(props.points))
const bands = computed(() => crestBands(props.points))

// Shared by the line's stroke and the area's fill so both repaint from the
// same tier boundaries.
const tierGradientId = `rank-tier-gradient-${useId()}`

// The area's own vertical opacity fade, applied as a <mask> instead of via
// `gradient-stops` (vue-chrts' own per-category gradient) because that
// gradient bakes in a *flat* colour — it can't also carry the horizontal
// tier-boundary hues `tierGradientId` needs.
const areaFadeGradientId = `rank-area-fade-gradient-${useId()}`
const areaFadeMaskId = `rank-area-fade-mask-${useId()}`

const lineStroke = computed(() => lineStrokeOf(props.points, props.currentTier, `url(#${tierGradientId})`))
const areaFill = computed(() => areaFillOf(props.points, props.currentTier, `url(#${tierGradientId})`))

const xFormatter = (tick: number): string => {
  const point = props.points[tick]
  return point ? rankChartDateLabel(point.entry.capturedAtUtc) : ''
}
</script>

<template>
  <div class="rank-chart flex gap-2">
    <ProfileRankChartPaint
      :tier-gradient-id="tierGradientId"
      :stops="stops"
      :area-fade-gradient-id="areaFadeGradientId"
      :area-fade-mask-id="areaFadeMaskId"
    />

    <ProfileRankCrestAxis :bands="bands" :y-domain="yDomain" :height="CHART_HEIGHT" />

    <div class="min-w-0 flex-1">
      <ChartsAreaChart
        :data="points"
        :categories="categories"
        :height="CHART_HEIGHT"
        :x-formatter="xFormatter"
        :y-domain="yDomain"
        hide-y-axis
        hide-legend
      >
        <template #tooltip="{ values }">
          <ProfileRankChartTooltip v-if="values" :entry="values.entry" />
        </template>
      </ChartsAreaChart>
    </div>
  </div>
</template>

<style scoped>
/*
 * Repaint the rank line with the tier gradient. Unovis writes the stroke as a
 * presentation attribute (`.attr('stroke', …)` in @unovis/ts
 * `components/line/index.js`), which a plain CSS declaration outranks without
 * needing `!important`. Emotion suffixes the path's generated class with
 * "-linePath" (see `label: linePath` in that package's `components/line/
 * style.js`) — same targeting trick as the tooltip override in theme.css.
 */
.rank-chart :deep([class*="-linePath"]) {
  stroke: v-bind(lineStroke);
}

/*
 * Repaint the area's fill with the same tier gradient, faded vertically by
 * the mask instead of vue-chrts' own per-category gradient (which only ever
 * carries one flat colour — see `areaFadeGradientId`). Unlike the line's
 * stroke, Unovis sets this `fill` via `.style(...)` (@unovis/ts
 * `components/area/index.js`), i.e. an *inline* style, which only an
 * `!important` declaration outranks. The selector is tag-qualified —
 * `-area-component` (the wrapping <g>) also matches a bare class substring
 * of "-area", and letting the rule land there too would apply the mask
 * twice through inheritance and double-darken the fade.
 */
.rank-chart :deep(path[class*="-area"]) {
  fill: v-bind(areaFill) !important;
  mask: v-bind('`url(#${areaFadeMaskId})`');
}
</style>
