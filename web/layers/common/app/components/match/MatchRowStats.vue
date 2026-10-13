<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'
import { formatDuration } from '#common/utils/relativeTime'

// Secondary stats of a match row: the LP the game moved when it is known (a
// measurement, so the data axis rather than the result's blue and red), the
// duration otherwise, and CS per minute under it.
const props = defineProps<{
  self: MatchSummarySelf
  durationSeconds: number
}>()

const lp = computed(() => props.self.lpDelta ?? null)
const csPerMin = computed(() => (props.self.cs / Math.max(props.durationSeconds / 60, 1)).toFixed(1))
</script>

<template>
  <div class="w-14 text-right leading-tight tabular-nums">
    <p v-if="lp !== null" class="text-xs font-semibold" :class="lp >= 0 ? 'text-data-good' : 'text-data-bad'">
      {{ lp > 0 ? '+' : '' }}{{ lp }} LP
    </p>
    <p v-else class="text-xs text-muted">{{ formatDuration(durationSeconds) }}</p>
    <p class="text-[11px] text-dimmed">{{ csPerMin }} CS/m</p>
  </div>
</template>
