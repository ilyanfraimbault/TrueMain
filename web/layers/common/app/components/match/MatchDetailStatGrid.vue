<script setup lang="ts">
// A row of four headline figures, each over its label — the laning and the
// per-minute cards of the player breakdown.
export interface MatchDetailStat {
  value: string
  label: string
  /** Text colour of the figure. */
  tone: string
}

defineProps<{
  stats: MatchDetailStat[]
}>()
</script>

<template>
  <div class="grid grid-cols-4 gap-2 text-center">
    <div v-for="stat in stats" :key="stat.label">
      <p class="text-sm font-bold tabular-nums" :class="stat.tone">{{ stat.value }}</p>
      <p class="text-[10px] text-muted">{{ stat.label }}</p>
    </div>
  </div>
</template>
