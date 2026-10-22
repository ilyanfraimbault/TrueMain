<script setup lang="ts">
import type { AutoClipKind } from '~/types/recordings'
import { AUTO_CLIP_KINDS, toggleAutoClip } from '~/utils/auto-clips'

/**
 * The automatic clips (#1766): once a game is finalised, the plays the player
 * picks here are cut as clips of their own, titled from what the game's events
 * show. Off when none is picked; turning it back on picks them all.
 */
const props = defineProps<{ kinds: AutoClipKind[] }>()
const emit = defineEmits<{ change: [kinds: AutoClipKind[]] }>()

const on = computed(() => props.kinds.length > 0)

function toggle(enabled: boolean) {
  emit('change', enabled ? AUTO_CLIP_KINDS.map(kind => kind.value) : [])
}
</script>

<template>
  <section class="flex flex-col gap-3">
    <h3 class="stat-label">Automatic clips</h3>
    <USwitch
      :model-value="on"
      label="Clip my best plays"
      description="Once a game is finalised, each play you pick becomes a clip of its own. Clips are never deleted by the budget."
      @update:model-value="toggle"
    />
    <div v-if="on" class="flex flex-col gap-2 pl-1">
      <UCheckbox
        v-for="kind in AUTO_CLIP_KINDS"
        :key="kind.value"
        :model-value="kinds.includes(kind.value)"
        :label="kind.label"
        :description="kind.description"
        size="sm"
        @update:model-value="emit('change', toggleAutoClip(kinds, kind.value, $event === true))"
      />
    </div>
  </section>
</template>
