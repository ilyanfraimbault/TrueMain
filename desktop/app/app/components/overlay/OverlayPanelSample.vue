<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import { SAMPLE_NEXT_ITEM, sampleGame, sampleLoadingView } from '~/utils/overlay-sample'

/**
 * One overlay panel's content drawn over the sample game
 * (`utils/overlay-sample.ts`): the real component with made-up values, so a
 * panel is placed — in the layout editor or the on-screen preview — at the
 * size and look it will have over a game.
 */
defineProps<{ panel: OverlayPanel }>()

const game = sampleGame()
const players = sampleLoadingView().players
const syncedAt = Date.now()
const { items } = useStaticData()
const next = computed(() => items.value[SAMPLE_NEXT_ITEM.itemId] ?? null)
</script>

<template>
  <LoadingBoard v-if="panel === 'loading'" :players="players" />
  <OverlayNextItemCard v-else-if="panel === 'next-item'" :item="next" :name="next?.name ?? 'Zhonya\'s Hourglass'" :missing="SAMPLE_NEXT_ITEM.missing" />
  <OverlayWinProbability v-else-if="panel === 'win-probability'" :game="game" :synced-at="syncedAt" />
  <OverlayStats v-else-if="panel === 'stats'" :game="game" />
  <OverlayItemValue v-else :game="game" />
</template>
