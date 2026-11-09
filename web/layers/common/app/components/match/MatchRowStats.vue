<script setup lang="ts">
import type { MatchSummarySelfReadModel } from '#shared/contract/api'
import { formatDuration } from '#common/utils/relativeTime'
import { formatSigned } from '#shared/utils/deltas'

// Secondary stats of a match row: the LP the game moved when it is known (a
// measurement, so the data axis rather than the result's blue and red), the
// duration otherwise, and CS per minute under it.
const props = defineProps<{
  self: MatchSummarySelfReadModel
  durationSeconds: number
  /** A game past match retention kept no CS (#2358), and a fabricated 0.0 is worse than a gap. */
  truncated?: boolean
}>()

const lp = computed(() => props.self.lpDelta ?? null)
const csPerMin = computed(() => (props.self.cs / Math.max(props.durationSeconds / 60, 1)).toFixed(1))
</script>

<template>
  <div class="w-14 text-right leading-tight tabular-nums">
    <p v-if="lp !== null" class="text-xs font-semibold" :class="lp >= 0 ? 'text-data-good' : 'text-data-bad'">
      {{ formatSigned(lp) }} LP
    </p>
    <p v-else class="text-xs text-muted">{{ formatDuration(durationSeconds) }}</p>
    <p v-if="!truncated" class="text-[11px] text-dimmed">{{ csPerMin }} CS/m</p>
  </div>
</template>
