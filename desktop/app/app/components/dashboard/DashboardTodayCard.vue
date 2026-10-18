<!--
  The player's session so far, under the ranked card: the day's record, its
  net LP on Solo/Duo, and how many games and how long. Nothing renders on a day
  without a game. The LP is the sum of the gains the local history measured; a
  day with Solo/Duo games it could not measure says so beside the figure.
-->
<script setup lang="ts">
import type { DaySummary } from '~/utils/today'
import { formatPlayTime, partialLpNote } from '~/utils/today'

const props = defineProps<{ summary: DaySummary | null }>()

const partial = computed(() => partialLpNote(props.summary))
</script>

<template>
  <section v-if="summary" class="flex flex-col gap-2 surface rounded-lg px-4 py-3">
    <div class="flex items-baseline justify-between gap-2">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
        Today
      </h2>
      <span class="text-xs tabular-nums text-muted">
        {{ summary.games }} game{{ summary.games === 1 ? '' : 's' }} · {{ formatPlayTime(summary.seconds) }}
      </span>
    </div>

    <div class="flex items-baseline justify-between gap-2 tabular-nums">
      <p class="text-lg font-semibold leading-none text-highlighted">
        {{ summary.wins }}W – {{ summary.losses }}L
      </p>
      <p
        v-if="summary.lp !== null"
        class="flex items-center gap-1.5 text-sm font-semibold text-default"
        :aria-label="`${summary.lp >= 0 ? 'Gained' : 'Lost'} ${Math.abs(summary.lp)} LP today`"
      >
        <WarningDot v-if="partial" :text="partial" />
        <UIcon
          :name="summary.lp >= 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
          class="size-3.5 shrink-0"
          :class="summary.lp >= 0 ? 'text-data-good' : 'text-data-bad'"
        />
        {{ Math.abs(summary.lp) }} LP
      </p>
    </div>
  </section>
</template>
