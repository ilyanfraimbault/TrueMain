<script setup lang="ts">
import type { RankChartPoint } from '#common/utils/rank-chart'
import { lpDeltaSince } from '#common/utils/rank-chart'

// The LP the player moved over the last 7 and 30 days, under the ranked card's
// rank. Nothing renders without a comparison to make (see `lpDeltaSince`).
const props = defineProps<{ points: readonly RankChartPoint[] }>()

const delta7d = computed(() => lpDeltaSince(props.points, 7))
const delta30d = computed(() => lpDeltaSince(props.points, 30))
</script>

<template>
  <!-- Side by side in the profile's narrow column, 7 days then 30, each in half its width. -->
  <div v-if="delta7d !== null || delta30d !== null" class="grid grid-cols-2 gap-1.5">
    <ProfileLpDeltaChip v-if="delta7d !== null" label="Last 7d" :delta="delta7d" />
    <ProfileLpDeltaChip v-if="delta30d !== null" label="Last 30d" :delta="delta30d" />
  </div>
</template>
