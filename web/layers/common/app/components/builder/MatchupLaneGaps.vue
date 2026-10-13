<script setup lang="ts">
import type { CompositionLane } from '#shared/types/composition'
import { formatGoldDiff, formatXpDiff, laneVerdict } from '#common/utils/lane-verdict'

// The two gaps and the verdict under the matchup strip's lane rate. They ride
// under the rate they explain, never as cells of their own: a reader comparing
// "+967 gold" with a win rate three columns away has to be told they share a
// sample, and they don't.
const props = defineProps<{
  lane: CompositionLane | null
  /** The jungle reads the same figures as a matchup tempo rather than a lane (#939). */
  laneNoun: 'lane' | 'matchup'
}>()

const goldDiff = computed(() => props.lane?.averageGoldDiffAt15 ?? null)
const measuredLanes = computed(() => props.lane?.measuredGames ?? 0)
const xpDiff = computed(() => props.lane?.averageXpDiffAt15 ?? null)
const verdict = computed(() => laneVerdict(goldDiff.value, measuredLanes.value, props.laneNoun))

/**
 * The two gaps on one line under the lane rate. Gold and XP sit together because
 * they are the same measurement of the same moment and are only interesting
 * *against each other*: a lead in gold over a deficit in XP is a lane won on
 * kills and lost on waves, which the next all-in reverses. Separating them across
 * two cells would have hidden exactly that.
 */
const gapLine = computed(() => {
  if (goldDiff.value === null && xpDiff.value === null) return null
  const parts: string[] = []
  if (goldDiff.value !== null) parts.push(`${formatGoldDiff(goldDiff.value)} gold`)
  if (xpDiff.value !== null) parts.push(`${formatXpDiff(xpDiff.value)} xp`)
  return `${parts.join(' · ')} @15`
})
</script>

<template>
  <!-- Uncoloured on purpose: one line carries two gaps that can point
       opposite ways, and a single tone would have to pick one of them
       to speak for both. The verdict badge below bands the gold gap,
       which is the only one with product-defined edges. -->
  <p
    v-if="gapLine"
    class="text-xs tabular-nums text-muted"
  >
    {{ gapLine }}
  </p>
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
