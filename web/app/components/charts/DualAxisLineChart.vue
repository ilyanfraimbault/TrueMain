<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import type { axisFormatter, BulletLegendItemInterface } from 'vue-chrts/types'
// Aliased so they don't collide with the same-named identifiers Nuxt
// auto-imports from `vue-chrts/enums` (which would reduce the enum types to
// `never` at the use site).
import { CurveType as UnovisCurveType, Position as UnovisPosition } from '@unovis/ts'
import {
  VisXYContainer,
  VisLine,
  VisAxis,
  VisCrosshair,
  VisTooltip,
  VisBulletLegend,
} from '@unovis/vue'

// Two-line chart with an independent Y axis per series. The shared
// <ChartsLineChart>/<ChartsAreaChart> wrappers plot every series on one
// Y scale, which flattens a small-magnitude line (pickrate ≈ a few %)
// against a large one (winrate ≈ 50%).
//
// `VisXYContainer` only keeps one axis per type (it tracks axes by "x"/"y"),
// so a single container can't carry two independent Y axes — the second one
// silently replaces the first. We therefore stack two containers: the base
// owns the win-rate line, the left axis, the shared X axis and the crosshair;
// a transparent overlay (identical size + margins so the X positions line up)
// owns the pick-rate line and the right axis on its own [0, max] domain. Each
// axis then labels its own series' real values — no rescaling, nothing
// misleading.
//
// Conventions match the sibling chart wrappers: emerald palette via the
// caller's `categories`, a fixed-height container so empty/loading states
// never collapse the layout, and a <ClientOnly> gate so SSR never renders
// the canvas. The tooltip HTML is built as a string inside the crosshair
// template so each series shows its own real value — no hidden DOM node is
// lifted (a hoisted ref'd node trips Vue's "ref on hoisted vnode" error).

interface Props {
  data: TItem[]
  /** Legend entries, keyed by `primaryKey` and `secondaryKey`. */
  categories: Record<string, BulletLegendItemInterface>
  /** Data key plotted on the left axis (its own real scale). */
  primaryKey: keyof TItem & string
  /** Data key plotted on the right axis (its own real scale). */
  secondaryKey: keyof TItem & string
  height?: number
  loading?: boolean
  emptyMessage?: string
  /** X tick label, by data index. */
  xFormatter?: axisFormatter
  /** Left axis + tooltip label for the primary value. */
  primaryFormatter?: axisFormatter
  /** Right axis + tooltip label for the secondary value. */
  secondaryFormatter?: axisFormatter
  /** Upper bound of the left (primary) axis. Win rate reads best on a fixed
   *  0–100% scale, so it defaults to 1 rather than auto-scaling tiny swings. */
  primaryMax?: number
  lineWidth?: number
}

const props = withDefaults(defineProps<Props>(), {
  height: 280,
  loading: false,
  emptyMessage: 'No data available',
  primaryMax: 1,
  lineWidth: 2.5,
})

// Identical fixed margins on both containers keep the two plot areas (and
// therefore the X position of every patch) pixel-aligned where they overlay.
// autoMargin is off so each container can't size itself to only its own axis
// (which would shift the base right for the left axis and the overlay left for
// the right axis, desyncing the X positions). Left + right both reserve room
// for a "100%"-width tick so either axis fits.
const chartMargin = { left: 48, right: 48, top: 12, bottom: 24 }

const isEmpty = computed(
  () => !props.loading && (props.data?.length ?? 0) === 0,
)

// Upper bound of the right axis: the largest secondary value, with a little
// headroom so the peak isn't glued to the ceiling. Falls back to 1 so an
// all-zero series still yields a valid domain.
const secondaryMax = computed(() => {
  const max = Math.max(
    0,
    ...props.data.map(row => Number(row[props.secondaryKey]) || 0),
  )
  return max > 0 ? max * 1.1 : 1
})

const primaryDomain = computed<[number, number]>(() => [0, props.primaryMax])
const secondaryDomain = computed<[number, number]>(() => [0, secondaryMax.value])

function resolveColor(color: string | string[] | undefined): string | undefined {
  return Array.isArray(color) ? color[0] : color
}

const primaryColor = computed(() => resolveColor(props.categories[props.primaryKey]?.color))
const secondaryColor = computed(() => resolveColor(props.categories[props.secondaryKey]?.color))

const x = (_row: TItem, i: number): number => i
const primaryY = (row: TItem): number => Number(row[props.primaryKey]) || 0
const secondaryY = (row: TItem): number => Number(row[props.secondaryKey]) || 0

// `axisFormatter` is a union over number/Date ticks; our ticks are always
// numeric, so narrow to the numeric overload before calling.
const fmt = (formatter: axisFormatter | undefined, value: number): string =>
  formatter ? (formatter as (tick: number) => string)(value) : String(value)

const xTickFormat = (tick: number): string => fmt(props.xFormatter, tick)
const primaryTickFormat = (tick: number): string => fmt(props.primaryFormatter, tick)
const secondaryTickFormat = (tick: number): string => fmt(props.secondaryFormatter, tick)

const legendItems = computed(() =>
  Object.values(props.categories).map(item => ({
    ...item,
    color: resolveColor(item.color),
  })),
)

// Build the tooltip HTML as a string from the hovered datum and its index.
// Unovis passes the x-accessor value (our data index) as the template's
// second argument, so the title reads straight off it without any
// referential-equality lookup that would go stale when `data` is rebuilt.
const escapeHtml = (value: string): string =>
  value.replace(/[&<>"']/g, ch => (
    ch === '&' ? '&amp;'
      : ch === '<' ? '&lt;'
        : ch === '>' ? '&gt;'
          : ch === '"' ? '&quot;'
            : '&#39;'
  ))

const tooltipRow = (label: string | number, color: string | undefined, value: string): string =>
  `<li class="flex items-center gap-2">`
  + `<span class="size-2 shrink-0 rounded-full" style="background-color:${escapeHtml(color ?? 'transparent')}"></span>`
  + `<span class="text-muted">${escapeHtml(String(label))}</span>`
  + `<span class="ml-auto font-medium text-default">${escapeHtml(value)}</span>`
  + `</li>`

const crosshairTemplate = (datum: TItem, xValue: number): string => {
  const title = fmt(props.xFormatter, xValue)
  const primaryName = props.categories[props.primaryKey]?.name ?? props.primaryKey
  const secondaryName = props.categories[props.secondaryKey]?.name ?? props.secondaryKey
  const primaryVal = fmt(props.primaryFormatter, Number(datum[props.primaryKey]) || 0)
  const secondaryVal = fmt(props.secondaryFormatter, Number(datum[props.secondaryKey]) || 0)
  return (
    `<div class="rounded-md bg-default px-3 py-2 text-xs shadow-lg ring-1 ring-default">`
    + `<p class="mb-1 font-medium text-default">${escapeHtml(title)}</p>`
    + `<ul class="flex flex-col gap-1">`
    + tooltipRow(primaryName, primaryColor.value, primaryVal)
    + tooltipRow(secondaryName, secondaryColor.value, secondaryVal)
    + `</ul></div>`
  )
}
</script>

<template>
  <div class="flex w-full flex-col gap-3">
    <div
      :style="{ height: `${height}px` }"
      class="relative w-full"
    >
      <USkeleton v-if="loading" class="absolute inset-0 size-full" />
      <div
        v-else-if="isEmpty"
        class="absolute inset-0 flex items-center justify-center text-sm text-muted"
      >
        {{ emptyMessage }}
      </div>
      <ClientOnly v-else>
        <!-- Base: win-rate line, left axis, shared X axis, crosshair/tooltip. -->
        <VisXYContainer
          :data="data"
          :height="height"
          :y-domain="primaryDomain"
          :margin="chartMargin"
          :auto-margin="false"
        >
          <VisTooltip
            :horizontal-placement="UnovisPosition.Right"
            :vertical-placement="UnovisPosition.Top"
          />
          <VisLine
            :x="x"
            :y="primaryY"
            :color="primaryColor"
            :line-width="lineWidth"
            :curve-type="UnovisCurveType.MonotoneX"
          />
          <VisAxis
            type="x"
            :tick-format="xTickFormat"
            :grid-line="false"
            :domain-line="false"
          />
          <VisAxis
            type="y"
            :tick-format="primaryTickFormat"
            :grid-line="false"
            :domain-line="false"
          />
          <VisCrosshair
            :y="primaryY"
            :color="primaryColor"
            :template="crosshairTemplate"
          />
        </VisXYContainer>

        <!-- Overlay: pick-rate line + right axis on its own [0, max] domain.
             Transparent and pointer-events-none so the base owns interaction;
             identical margins keep the X positions aligned. -->
        <div class="pointer-events-none absolute inset-0">
          <VisXYContainer
            :data="data"
            :height="height"
            :y-domain="secondaryDomain"
            :margin="chartMargin"
            :auto-margin="false"
          >
            <VisLine
              :x="x"
              :y="secondaryY"
              :color="secondaryColor"
              :line-width="lineWidth"
              :curve-type="UnovisCurveType.MonotoneX"
            />
            <VisAxis
              type="y"
              :position="UnovisPosition.Right"
              :tick-format="secondaryTickFormat"
              :grid-line="false"
              :domain-line="false"
            />
          </VisXYContainer>
        </div>
        <template #fallback>
          <USkeleton class="absolute inset-0 size-full" />
        </template>
      </ClientOnly>
    </div>

    <VisBulletLegend
      v-if="!isEmpty && !loading"
      :items="legendItems"
      class="flex justify-center gap-4 text-xs"
    />
  </div>
</template>
