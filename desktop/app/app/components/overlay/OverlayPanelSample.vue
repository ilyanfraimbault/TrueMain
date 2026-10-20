<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import { SAMPLE_BUILD_ORDER, SAMPLE_NEXT_ITEM, SAMPLE_PACE_REFERENCE, sampleGame } from '~/utils/overlay-sample'

/**
 * One overlay panel's content drawn over the sample game
 * (`utils/overlay-sample.ts`): the real component with made-up values, so a
 * panel is placed — in the layout editor or the on-screen preview — at the
 * size and look it will have over a game.
 */
withDefaults(defineProps<{ panel: OverlayPanel, lanes?: boolean }>(), { lanes: true })

const game = sampleGame()
const syncedAt = Date.now()
const { items } = useStaticData()
const next = computed(() => items.value[SAMPLE_NEXT_ITEM.itemId] ?? null)
</script>

<template>
  <div v-if="panel === 'next-item'" class="space-y-2">
    <OverlayNextItemCard :item="next" :name="next?.name ?? 'Zhonya\'s Hourglass'" :missing="SAMPLE_NEXT_ITEM.missing" />
    <GameBuildOrder :steps="SAMPLE_BUILD_ORDER" :statics="items" :size="22" />
  </div>
  <OverlayWinProbability v-else-if="panel === 'win-probability'" :game="game" :synced-at="syncedAt" />
  <OverlayStats v-else-if="panel === 'stats'" :game="game" :reference="SAMPLE_PACE_REFERENCE" />
  <OverlayObjectives v-else-if="panel === 'objectives'" :game="game" :synced-at="syncedAt" />
  <OverlayLaneGold v-else-if="panel === 'lane-gold'" :game="game" />
  <OverlayItemValue v-else :game="game" :lanes="lanes" />
</template>
