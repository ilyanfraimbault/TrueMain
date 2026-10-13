<script setup lang="ts">
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { isChampionPosition, positionLabel } from '#common/utils/positions'

/** Whose build the draft's build pane shows: the champion, its lane, and the way back to ours. */
defineProps<{
  championId: number
  position: string
  /** Another champion's build, or ours opened by hand, is on screen. */
  canGoBack: boolean
}>()

const emit = defineEmits<{ back: [] }>()

const { nameOf } = useChampionStatics()
</script>

<template>
  <div class="flex items-center gap-2.5">
    <ChampionPortrait :champion-id="championId" size="sm" class="size-10! rounded-lg!" />
    <p class="min-w-0 truncate text-sm font-semibold text-highlighted">{{ nameOf(championId) }}</p>
    <img
      v-if="isChampionPosition(position)"
      :src="getPositionIconUrl(position)"
      :alt="positionLabel(position, { short: true })"
      :title="positionLabel(position, { short: true })"
      class="size-4 shrink-0"
    >
    <span class="flex-1" />
    <UButton v-if="canGoBack" size="xs" color="neutral" variant="ghost" icon="i-lucide-arrow-left" aria-label="Back" @click="emit('back')" />
  </div>
</template>
