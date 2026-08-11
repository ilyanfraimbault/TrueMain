<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'

// `games` is what the percentages are computed from. It used to sit next to the
// badges (#923) because a matchup slice is usually small — measured on
// production, the median champion-vs-opponent pair holds 4 games on a patch — so
// "100% win" must stay readable as "1 of 1". It now lives in the hover tooltip
// instead, one labelled line per stat: the sample size is one gesture away, and
// the row keeps a single compact pick/win chip pair.
const props = defineProps<{
  pickRate: number
  winRate: number
  games?: number
}>()

/**
 * Win rate is the one value here with a better/worse reading, so it carries the
 * data axis; pick rate is a popularity share and stays neutral — a 2% pick rate
 * is not a *bad* pick rate.
 *
 * It used to be `color="primary"`, i.e. rose gold on a measurement, which the
 * design system now reserves for brand and interaction. Worse, it was the same
 * chip colour whatever the number said, so the accent was decorating a value
 * instead of reading it.
 *
 * The band is ±2 points around even. Below that the difference is inside the
 * noise of the small samples this badge exists for — measured on production,
 * the median champion-vs-opponent pair holds 4 games — and colouring a 51%
 * teal would be asserting a signal the sample cannot carry.
 */
const WIN_RATE_EVEN_BAND = 0.02

const winRateClass = computed(() => {
  if (props.winRate >= 0.5 + WIN_RATE_EVEN_BAND) return 'bg-data-good/15 text-data-good'
  if (props.winRate <= 0.5 - WIN_RATE_EVEN_BAND) return 'bg-data-bad/15 text-data-bad'
  return 'bg-data-mid/15 text-data-mid'
})

// One line per stat, label and value in their own column, so the three numbers
// read as a small table instead of a sentence to parse.
const rows = computed(() => {
  const stats: { label: string, value: string }[] = []

  if (props.games !== undefined) {
    stats.push({ label: props.games === 1 ? 'Game' : 'Games', value: props.games.toLocaleString('en-US') })
  }

  stats.push(
    { label: 'Pick rate', value: formatPercentage(props.pickRate) },
    { label: 'Win rate', value: formatPercentage(props.winRate) },
  )

  return stats
})
</script>

<template>
  <UTooltip
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

    <div class="flex shrink-0 items-center gap-2 whitespace-nowrap">
      <UBadge
        color="neutral"
        size="sm"
      >
        {{ formatPercentage(pickRate) }} pick
      </UBadge>
      <!-- Not a UBadge: its colours are the Nuxt UI semantic palette, and the
           data axis is deliberately not in it. Same geometry as the `sm` badge
           beside it so the pair still reads as one control. -->
      <span
        class="inline-flex items-center rounded-sm px-1.5 py-0.5 text-xs font-medium tabular-nums"
        :class="winRateClass"
      >
        {{ formatPercentage(winRate) }} win
      </span>
    </div>
  </UTooltip>
</template>
