<script setup lang="ts">
import type { RecapTurningPoint } from '~/utils/recording-win-probability'
import { formatGameClock, formatSwingPoints } from '#common/utils/win-probability-swing'

/**
 * The moments that swung the game most (#1911), largest first: when in the
 * game, what happened, and how many points of win probability it moved for
 * the player's side. A click jumps a few seconds before it, like a moment.
 */
defineProps<{ points: RecapTurningPoint[] }>()
const emit = defineEmits<{ jump: [point: RecapTurningPoint] }>()
</script>

<template>
  <div class="flex flex-col gap-1">
    <h3 class="stat-label px-1">Turning points</h3>
    <p class="px-1 pb-1 text-xs leading-relaxed text-muted">
      The moments that moved your side's chance to win most, in points.
    </p>
    <ul class="flex flex-col">
      <li v-for="(point, index) in points" :key="index">
        <button
          type="button"
          class="flex w-full items-start gap-2 rounded-md px-2 py-1.5 text-left text-xs transition-colors hover:bg-accented"
          :title="point.gold ? `${point.label} · ${point.gold} gold` : point.label"
          @click="emit('jump', point)"
        >
          <span class="w-9 shrink-0 tabular-nums text-dimmed">{{ formatGameClock(point.gameMs) }}</span>
          <RecapMomentIcon :kind="point.icon" class="mt-px size-3.5 shrink-0" :class="point.ours ? 'text-ally' : 'text-enemy'" />
          <span class="min-w-0 flex-1 leading-snug" :class="point.ours ? 'text-default' : 'text-muted'">{{ point.label }}</span>
          <span class="shrink-0 font-medium tabular-nums" :class="point.delta >= 0 ? 'text-ally' : 'text-enemy'">
            {{ formatSwingPoints(point.delta) }}
          </span>
        </button>
      </li>
    </ul>
  </div>
</template>
