<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import { OVERLAY_PANEL_INFO } from '~/types/overlay'

/**
 * The layout editor's side: the panels that are off, each dragged onto the
 * screen or clicked to come back where it last was — and the drop zone a panel
 * dragged off the screen goes to.
 */
defineProps<{
  hidden: OverlayPanel[]
  /** The panel being dragged, once it has moved. */
  dragged: OverlayPanel | null
  /** A panel from the screen is held over this side: dropping it here takes it off. */
  dropping: boolean
}>()

const emit = defineEmits<{ grab: [panel: OverlayPanel, event: PointerEvent], show: [panel: OverlayPanel] }>()
</script>

<template>
  <section
    class="flex flex-col gap-2 rounded-lg p-2 ring-1 transition-colors"
    :class="dropping ? 'bg-error/5 ring-error/50' : 'ring-transparent'"
  >
    <h3 class="stat-label">Hidden panels</h3>
    <p v-if="!hidden.length" class="text-xs text-muted">
      {{ dropping ? 'Drop to hide it.' : 'Every panel is on your screen. Remove one with its × or drag it here.' }}
    </p>
    <button
      v-for="panel in hidden"
      :key="panel"
      type="button"
      class="flex cursor-grab touch-none items-start gap-2.5 rounded-md bg-elevated/60 p-2.5 text-left ring-1 ring-default transition hover:ring-primary/60"
      :class="dragged === panel && 'opacity-40'"
      :title="`Drag onto the screen, or click to show ${OVERLAY_PANEL_INFO[panel].label} where it was`"
      @pointerdown="emit('grab', panel, $event)"
      @keydown.enter.prevent="emit('show', panel)"
      @keydown.space.prevent="emit('show', panel)"
    >
      <OverlayPanelIcon :panel="panel" class="mt-0.5 size-4 shrink-0 text-primary" />
      <span class="flex min-w-0 flex-col gap-0.5">
        <span class="text-sm font-medium text-highlighted">{{ OVERLAY_PANEL_INFO[panel].label }}</span>
        <span class="text-xs text-muted">{{ OVERLAY_PANEL_INFO[panel].description }}</span>
      </span>
    </button>
  </section>
</template>
