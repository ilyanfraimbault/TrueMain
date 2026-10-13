<script setup lang="ts">
import { formatKilo } from '#common/utils/match-detail-format'

// A scoreboard player's damage to champions: the bar first, then the figure and
// its per-minute rate under it. The side's top dealer gets the full-strength
// fill so the carry reads at a glance; everyone else sits on a softer tone.
const props = defineProps<{
  damage: number
  damagePerMin: number
  /** Highest damage on the side — the bar's full width. */
  maxDamage: number
}>()

const isTop = computed(() => props.damage === props.maxDamage)
const percent = computed(() => props.maxDamage > 0 ? Math.round((props.damage / props.maxDamage) * 100) : 0)
</script>

<template>
  <div class="flex w-[6.5rem] flex-col gap-1">
    <div class="h-1.5 w-full overflow-hidden rounded-full bg-white/8">
      <div
        class="h-full rounded-full transition-[width]"
        :class="isTop ? 'bg-primary shadow-[0_0_8px] shadow-primary/60' : 'bg-primary/45'"
        :style="{ width: `${percent}%` }"
      />
    </div>
    <span class="whitespace-nowrap text-[11px] leading-none tabular-nums">
      <span class="font-semibold text-default">{{ formatKilo(damage) }}</span>
      <span class="text-muted"> ({{ Math.round(damagePerMin) }}/m)</span>
    </span>
  </div>
</template>
