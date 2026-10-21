<script setup lang="ts">
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { isChampionPosition, positionLabel } from '#common/utils/positions'

/**
 * Whose build the draft's build pane shows: the champion, its lane, and the way back to ours. With an active kit
 * on the pick (#2185), its name and the switch between it and TrueMain's build.
 */
defineProps<{
  championId: number
  position: string
  /** Another champion's build, or ours opened by hand, is on screen. */
  canGoBack: boolean
  /** The player's active kit on this champion and lane, by name. */
  kitName?: string | null
}>()

const onKit = defineModel<boolean>('onKit', { default: false })
const emit = defineEmits<{ back: [] }>()

const { nameOf } = useChampionStatics()
</script>

<template>
  <div class="flex flex-col gap-2">
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
    <div v-if="kitName" class="flex items-center gap-1.5 text-xs">
      <UIcon name="i-lucide-swords" class="size-3.5 shrink-0" :class="onKit ? 'text-primary' : 'text-dimmed'" />
      <span class="min-w-0 truncate" :class="onKit ? 'text-highlighted' : 'text-muted'">Your kit: {{ kitName }}</span>
      <span class="flex-1" />
      <UButton
        size="xs"
        color="neutral"
        variant="link"
        class="shrink-0 px-0"
        :label="onKit ? 'TrueMain\'s build' : 'Your kit'"
        @click="onKit = !onKit"
      />
    </div>
  </div>
</template>
