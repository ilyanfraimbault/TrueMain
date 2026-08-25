<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'
import { pickRateTone, winRateTone } from '~/utils/rate-tone'

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
  /**
   * True while the surrounding panel is scaffolding rather than showing a
   * measurement (see `utils/build-placeholder`). The two chips keep their box
   * and turn into skeletons: the placeholder aggregate carries filler numbers,
   * and rendering them would put invented rates on screen.
   */
  pending?: boolean
}>()

/**
 * Both chips read their value through `utils/rate-tone`, the same module the
 * tier-list chip uses — a win rate must not be accented in one place and neutral
 * in another on pages a reader moves between. The bands and their calibration live
 * there (win rate symmetric around 50%; pick rate one-sided, because a niche
 * champion is not *bad* at being picked).
 *
 * The win chip used to be a flat `color="primary"` — the same rose gold
 * whatever the number said, so the accent decorated a value instead of reading
 * it. The accent is back on measurements (#1096), but it now *means* something:
 * it marks the good end, and a losing rate steps down to neutral instead.
 */
const winRateClass = computed(() => winRateTone(props.winRate))
const pickRateClass = computed(() => pickRateTone(props.pickRate))

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
  <div
    v-if="pending"
    class="flex shrink-0 items-center gap-2"
    aria-hidden="true"
  >
    <USkeleton class="h-[22px] w-16 rounded-sm" />
    <USkeleton class="h-[22px] w-[3.75rem] rounded-sm" />
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

    <!-- Neither chip is a `UBadge`: its `color` takes the Nuxt UI semantic
         palette, and the data axis is deliberately not in it. The fill stays
         one neutral step for both so the *colour* carries only the reading —
         `rate-tone` returns a text class, and letting a background vary too
         would be a second, unsynchronised encoding of the same number. -->
    <div class="flex shrink-0 items-center gap-2 whitespace-nowrap">
      <span
        class="inline-flex items-center rounded-sm bg-elevated px-1.5 py-0.5 text-xs font-medium tabular-nums ring-1 ring-inset ring-accented"
        :class="pickRateClass"
      >
        {{ formatPercentage(pickRate) }} pick
      </span>
      <span
        class="inline-flex items-center rounded-sm bg-elevated px-1.5 py-0.5 text-xs font-medium tabular-nums ring-1 ring-inset ring-accented"
        :class="winRateClass"
      >
        {{ formatPercentage(winRate) }} win
      </span>
    </div>
  </UTooltip>
</template>
