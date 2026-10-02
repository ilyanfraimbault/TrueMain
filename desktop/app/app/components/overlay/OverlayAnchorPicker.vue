<script setup lang="ts">
import type { OverlayAnchor, OverlayPanelSettings } from '~/types/overlay'

/**
 * Where one panel sits: the screen, small, with a spot per anchor, and the
 * place the panel was dragged to when there is one. Picking a spot drops the
 * dragged place.
 */
defineProps<{ settings: OverlayPanelSettings }>()
const emit = defineEmits<{ pick: [anchor: OverlayAnchor] }>()

const ANCHORS: { value: OverlayAnchor, label: string, spot: string }[] = [
  { value: 'top-left', label: 'Top left', spot: 'left-1 top-1.5' },
  { value: 'top-center', label: 'Top centre', spot: 'left-1/2 top-1.5 -translate-x-1/2' },
  { value: 'top-right', label: 'Top right', spot: 'right-1 top-1.5' },
  { value: 'center-left', label: 'Middle left', spot: 'left-1 top-1/2 -translate-y-1/2' },
  { value: 'center-right', label: 'Middle right', spot: 'right-1 top-1/2 -translate-y-1/2' },
]
</script>

<template>
  <div class="relative aspect-video w-24 shrink-0 rounded-md bg-elevated ring-1 ring-default">
    <button
      v-for="anchor in ANCHORS"
      :key="anchor.value"
      type="button"
      :aria-label="anchor.label"
      :title="anchor.label"
      class="absolute h-2.5 w-5 rounded-sm ring-1 transition-colors"
      :class="[anchor.spot, !settings.custom && settings.anchor === anchor.value ? 'bg-primary ring-primary' : 'bg-accented ring-default hover:ring-primary/60']"
      @click="emit('pick', anchor.value)"
    />
    <span
      v-if="settings.custom"
      class="absolute h-2.5 w-5 -translate-x-1/2 -translate-y-1/2 rounded-sm bg-primary ring-1 ring-primary"
      title="Where you dragged it"
      :style="{ left: `${settings.custom.x * 100}%`, top: `${settings.custom.y * 100}%` }"
    />
  </div>
</template>
