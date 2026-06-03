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
// against a large one (winrate ≈ 50%). Here the primary series keeps the
// real left axis while the secondary series is rescaled into the same
// [0, 1] plotting band so it uses the full height; the right axis relabels
// those tick positions back to the secondary series' real values. That
// keeps both lines readable without hand-managing Unovis scale ranges
// (the container owns the single [0, 1] domain both axes read).
//
// Conventions match the sibling chart wrappers: emerald palette via the
// caller's `categories`, a fixed-height container so empty/loading states
// never collapse the layout, and a <ClientOnly> gate so SSR never renders
// the canvas. The tooltip follows the same hidden-wrapper pattern the
// upstream charts use — the crosshair pulls rendered HTML from a hidden
// slot — but the markup is ours so each series shows its own real value
// rather than a single shared formatter.

interface Props {
  data: TItem[]
  /** Legend entries, keyed by `primaryKey` and `secondaryKey`. */
  categories: Record<string, BulletLegendItemInterface>
  /** Data key plotted on the left axis (kept at its real scale). */
  primaryKey: keyof TItem & string
  /** Data key plotted on the right axis (rescaled into the left band). */
  secondaryKey: keyof TItem & string
  height?: number
  loading?: boolean
  emptyMessage?: string
  /** X tick label, by data index. */
  xFormatter?: axisFormatter
  /** Left axis + tooltip label for the real primary value. */
  primaryFormatter?: axisFormatter
  /** Right axis + tooltip label for the real secondary value. */
  secondaryFormatter?: axisFormatter
  lineWidth?: number
}

const props = withDefaults(defineProps<Props>(), {
  height: 280,
  loading: false,
  emptyMessage: 'No data available',
  lineWidth: 2.5,
})

const isEmpty = computed(
  () => !props.loading && (props.data?.length ?? 0) === 0,
)

// Largest secondary value in the set; the secondary line and right axis are
// both expressed as a fraction of it. Falls back to 1 so an all-zero series
// can't divide by zero (it simply draws flat along the floor).
const secondaryMax = computed(() => {
  const max = Math.max(
    0,
    ...props.data.map(row => Number(row[props.secondaryKey]) || 0),
  )
  return max > 0 ? max : 1
})

const primaryColor = computed(() => resolveColor(props.categories[props.primaryKey]?.color))
const secondaryColor = computed(() => resolveColor(props.categories[props.secondaryKey]?.color))

function resolveColor(color: string | string[] | undefined): string | undefined {
  return Array.isArray(color) ? color[0] : color
}

const x = (_row: TItem, i: number): number => i
const primaryY = (row: TItem): number => Number(row[props.primaryKey]) || 0
// Rescale the secondary value into the primary's [0, 1] plotting band so it
// uses the full chart height instead of hugging the floor.
const secondaryY = (row: TItem): number =>
  (Number(row[props.secondaryKey]) || 0) / secondaryMax.value

// `axisFormatter` is a union over number/Date ticks; our ticks are always
// numeric, so narrow to the numeric overload before calling.
const fmt = (formatter: axisFormatter | undefined, value: number): string =>
  formatter ? (formatter as (tick: number) => string)(value) : String(value)

const xTickFormat = (tick: number): string => fmt(props.xFormatter, tick)
const primaryTickFormat = (tick: number): string => fmt(props.primaryFormatter, tick)
// Right axis shares the container's [0, 1] domain; map each tick position
// back to the secondary series' real value before formatting.
const secondaryTickFormat = (tick: number): string =>
  fmt(props.secondaryFormatter, tick * secondaryMax.value)

const legendItems = computed(() =>
  Object.values(props.categories).map(item => ({
    ...item,
    color: resolveColor(item.color),
  })),
)

// Crosshair circles: index 0 is the primary line, index 1 the secondary.
const crosshairY = [primaryY, secondaryY]
const crosshairColor = (_row: TItem, i: number): string | undefined =>
  i === 0 ? primaryColor.value : secondaryColor.value

// Hidden-wrapper tooltip plumbing (mirrors the upstream charts): the
// crosshair template reads the innerHTML of the hidden tooltip after we
// stash the hovered datum in `hovered`.
const slotWrapper = useTemplateRef<HTMLElement>('slotWrapper')
const hovered = ref<TItem | null>(null)
const crosshairTemplate = (datum: TItem): string => {
  hovered.value = datum
  if (typeof window === 'undefined') return ''
  return slotWrapper.value?.innerHTML ?? ''
}

const tooltipRows = computed(() => {
  const row = hovered.value
  if (!row) return []
  return [
    {
      key: props.primaryKey,
      label: props.categories[props.primaryKey]?.name ?? props.primaryKey,
      color: primaryColor.value,
      value: fmt(props.primaryFormatter, Number(row[props.primaryKey]) || 0),
    },
    {
      key: props.secondaryKey,
      label: props.categories[props.secondaryKey]?.name ?? props.secondaryKey,
      color: secondaryColor.value,
      value: fmt(props.secondaryFormatter, Number(row[props.secondaryKey]) || 0),
    },
  ]
})
const tooltipTitle = computed(() =>
  hovered.value ? fmt(props.xFormatter, props.data.indexOf(hovered.value)) : '',
)
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
        <VisXYContainer
          :data="data"
          :height="height"
          :y-domain="[0, 1]"
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
          <VisLine
            :x="x"
            :y="secondaryY"
            :color="secondaryColor"
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
            :grid-line="true"
            :domain-line="false"
          />
          <VisAxis
            type="y"
            :position="UnovisPosition.Right"
            :tick-format="secondaryTickFormat"
            :grid-line="false"
            :domain-line="false"
          />
          <VisCrosshair
            :y="crosshairY"
            :color="crosshairColor"
            :template="crosshairTemplate"
          />
        </VisXYContainer>
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

    <!-- Hidden tooltip body; the crosshair template lifts its innerHTML. -->
    <div ref="slotWrapper" class="hidden">
      <div class="rounded-md bg-default px-3 py-2 text-xs shadow-lg ring-1 ring-default">
        <p class="mb-1 font-medium text-default">
          {{ tooltipTitle }}
        </p>
        <ul class="flex flex-col gap-1">
          <li
            v-for="row in tooltipRows"
            :key="row.key"
            class="flex items-center gap-2"
          >
            <span
              class="size-2 shrink-0 rounded-full"
              :style="{ backgroundColor: row.color }"
            />
            <span class="text-muted">{{ row.label }}</span>
            <span class="ml-auto font-medium text-default">{{ row.value }}</span>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>
