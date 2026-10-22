<script setup lang="ts">
import type { LpMove } from '~/utils/lp-history'
import { formatSigned } from '#shared/utils/deltas'

// The LP the latest measured Solo/Duo games moved, newest last, under the
// ranked card's curve (`latestLpMoves`). A measurement, so the data axis's
// colours rather than the result's blue and red, as on the match rows.
defineProps<{ moves: readonly LpMove[] }>()
</script>

<template>
  <div v-if="moves.length > 0" class="flex flex-col gap-1.5" data-testid="lp-moves">
    <span class="text-[11px] text-muted">Last {{ moves.length === 1 ? 'game' : `${moves.length} games` }}</span>
    <!-- Eight columns: the full strip holds on one line in the profile's narrow aside. -->
    <ol class="grid grid-cols-8 gap-1">
      <li
        v-for="move in moves"
        :key="move.gameId"
        class="rounded-md bg-elevated py-0.5 text-center text-[11px] font-semibold tabular-nums"
        :class="move.delta >= 0 ? 'text-data-good' : 'text-data-bad'"
      >
        {{ formatSigned(move.delta) }}
      </li>
    </ol>
  </div>
</template>
