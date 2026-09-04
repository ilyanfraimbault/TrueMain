<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'
import { pickRateTone } from '~/utils/rate-tone'

// `games` is what the percentages are computed from. It used to sit next to the
// badges (#923) because a matchup slice is usually small — measured on
// production, the median champion-vs-opponent pair holds 4 games on a patch — so
// "100% win" must stay readable as "1 of 1". It now lives in the hover tooltip
// instead, one labelled line per stat: the sample size is one gesture away.
//
// The win rate joined it there in #1469, and the visible chip came down to a
// single bare percentage — the pick rate, no suffix. Two chips reading
// "76.0% pick 53.4% win" spent most of a card's width restating their own
// labels, which is what kept three variation categories from sharing a row.
// What is left is the number that answers the card's question ("how many people
// build this?"); the rest is one hover away, and a chip small enough to read at
// a glance is what makes that hover worth doing.
const props = defineProps<{
  pickRate: number
  winRate: number
  games?: number
  /**
   * True while the surrounding panel is scaffolding rather than showing a
   * measurement (see `utils/build-placeholder`). The two chips keep their box
   * and turn into skeletons: the placeholder aggregate carries filler numbers,
   * and rendering them would put invented rates on screen.
   */
  pending?: boolean
}>()

/**
 * The chip reads its value through `utils/rate-tone`, the same module the
 * tier-list chip uses — a rate must not be accented in one place and neutral in
 * another on pages a reader moves between. The bands and their calibration live
 * there; the pick-rate scale is one-sided, because a niche build is not *bad* at
 * being picked.
 *
 * The tone stayed with the chip when the win rate moved into the tooltip
 * (#1469): the accent marks the good end of a measurement (#1096), and a
 * tooltip line is not a place an accent reads.
 */
const pickRateClass = computed(() => pickRateTone(props.pickRate))

// One line per stat, label and value in their own column, so the three numbers
// read as a small table instead of a sentence to parse.
const rows = computed(() => {
  const stats: { label: string, value: string }[] = []

  if (props.games !== undefined) {
    stats.push({ label: props.games === 1 ? 'Game' : 'Games', value: props.games.toLocaleString('en-US') })
  }

  // The tooltip keeps its decimal where the chip drops it: rounding the chip is
  // only defensible if the precision is still reachable.
  stats.push(
    { label: 'Pick rate', value: formatPercentage(props.pickRate) },
    { label: 'Win rate', value: formatPercentage(props.winRate) },
  )

  return stats
})
</script>

<template>
  <div
    v-if="pending"
    class="flex shrink-0 items-center gap-2"
    aria-hidden="true"
  >
    <USkeleton class="h-[22px] w-11 rounded-sm" />
  </div>

  <UTooltip
    v-else
    :delay-duration="150"
    :ui="{ content: 'h-auto items-start p-1.5' }"
  >
    <template #content>
      <div class="grid grid-cols-[auto_auto] gap-x-4 gap-y-0.5 text-xs">
        <template
          v-for="row in rows"
          :key="row.label"
        >
          <span class="text-muted">{{ row.label }}</span>
          <span class="text-right font-medium tabular-nums text-default">{{ row.value }}</span>
        </template>
      </div>
    </template>

    <!-- Not a `UBadge`: its `color` takes the Nuxt UI semantic palette, and the
         data axis is deliberately not in it. The fill stays one neutral step so
         the *colour* carries only the reading — `rate-tone` returns a text
         class, and letting a background vary too would be a second,
         unsynchronised encoding of the same number.

         Whole percent, not one decimal: a variation's pick rate is read as
         "most people" / "some people", and `76.0%` claims a precision nobody
         spends. `formatPercentage`'s own default is left alone — the callers
         that want whole numbers ask for them. -->
    <span
      class="inline-flex shrink-0 items-center whitespace-nowrap rounded-sm bg-elevated px-1.5 py-0.5 text-xs font-medium tabular-nums ring-1 ring-inset ring-accented"
      :class="pickRateClass"
    >
      {{ formatPercentage(pickRate, 0) }}
    </span>
  </UTooltip>
</template>
