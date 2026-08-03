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

// One line per stat, label and value in their own column, so the three numbers
// read as a small table instead of a sentence to parse.
const rows = computed(() => {
  const stats: { label: string, value: string }[] = []

  if (props.games !== undefined) {
    stats.push({ label: 'Games', value: props.games.toLocaleString('en-US') })
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
    :ui="{ content: 'p-1.5' }"
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
      <UBadge
        color="primary"
        size="sm"
      >
        {{ formatPercentage(winRate) }} win
      </UBadge>
    </div>
  </UTooltip>
</template>
