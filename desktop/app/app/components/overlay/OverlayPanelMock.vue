<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import { OVERLAY_PANEL_INFO } from '~/types/overlay'

/**
 * One overlay panel as the layout editor draws it (#1819): its icon and name
 * on the panel's own surface, filling the box it is given — at the screen's
 * scale a panel can be a few pixels high, so the name goes first, then the
 * icon, and the name stays in the tooltip — and, on the screen, a × that takes
 * it off, over the corner while the panel is hovered or focused.
 */
const props = defineProps<{ panel: OverlayPanel, removable?: boolean }>()
const emit = defineEmits<{ remove: [] }>()

const info = computed(() => OVERLAY_PANEL_INFO[props.panel])
</script>

<template>
  <div
    class="relative flex size-full items-center justify-center gap-1.5 overflow-hidden rounded-md bg-default/90 px-1.5 ring-1 ring-default backdrop-blur-sm @container"
    :title="info.label"
  >
    <OverlayPanelIcon :panel="panel" class="size-3.5 shrink-0 text-primary" />
    <span class="hidden truncate text-[11px] font-medium text-highlighted @[5rem]:inline">{{ info.label }}</span>
  </div>
  <button
    v-if="removable"
    type="button"
    class="absolute -right-1.5 -top-1.5 z-10 flex size-4.5 items-center justify-center rounded-full bg-elevated text-muted opacity-0 shadow ring-1 ring-default transition hover:bg-error hover:text-white focus-visible:opacity-100 group-hover:opacity-100 group-focus-visible:opacity-100"
    :aria-label="`Remove ${info.label}`"
    :title="`Remove ${info.label}`"
    @pointerdown.stop
    @click.stop="emit('remove')"
  >
    <UIcon name="i-lucide-x" class="size-3" />
  </button>
</template>
