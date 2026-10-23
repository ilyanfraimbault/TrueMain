<script setup lang="ts">
import type { CompositionLane } from '#shared/types/composition'
import { laneVerdict } from '#common/utils/lane-verdict'

// The verdict under the matchup strip's lane rate, banded on the gold gap at 15 —
// the only gap with product-defined edges. The raw gold / XP gaps are not printed:
// the badge says what they mean, and the duel stays a single figure.
const props = defineProps<{
  lane: CompositionLane | null
  /** The jungle reads the same figures as a matchup tempo rather than a lane (#939). */
  laneNoun: 'lane' | 'matchup'
}>()

const verdict = computed(() => laneVerdict(props.lane?.averageGoldDiffAt15 ?? null, props.lane?.measuredGames ?? 0, props.laneNoun))
</script>

<template>
  <div v-if="verdict">
    <UBadge
      :color="verdict.color"
      :variant="verdict.variant"
      size="sm"
      class="font-semibold"
    >
      {{ verdict.label }}
    </UBadge>
  </div>
</template>
