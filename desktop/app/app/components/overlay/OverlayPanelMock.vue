<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import type { Size } from '~/utils/overlay-layout'
import { OVERLAY_PANEL_INFO } from '~/types/overlay'
import { PANEL_WIDTHS } from '~/utils/overlay-layout'

/**
 * One overlay panel as the layout editor draws it: the panel itself over the
 * sample game (`OverlayPanelSample`), laid out at its width in game and shrunk
 * by `zoom` to the copy of the screen — so what is placed is what shows, edges
 * included. Its size before the shrink is reported (`measure`) for the editor
 * to place it by. On the screen, a × takes it off, over the corner while the
 * panel is hovered or focused, and the chord that brings it up (#1915) is
 * marked in its corner, unshrunk.
 */
const props = defineProps<{ panel: OverlayPanel, zoom: number, removable?: boolean, chord?: string | null }>()
const emit = defineEmits<{ remove: [], measure: [size: Size] }>()

const info = computed(() => OVERLAY_PANEL_INFO[props.panel])

const frame = ref<HTMLElement | null>(null)
let observer: ResizeObserver | undefined
onMounted(() => {
  observer = new ResizeObserver(() => {
    if (frame.value) emit('measure', { width: frame.value.offsetWidth, height: frame.value.offsetHeight })
  })
  if (frame.value) observer.observe(frame.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div class="relative size-full overflow-hidden rounded-sm bg-default ring-1 ring-default" :title="info.label">
    <div
      ref="frame"
      class="pointer-events-none origin-top-left px-3 py-2.5 text-default select-none"
      :style="{ width: `${PANEL_WIDTHS[panel]}px`, transform: `scale(${zoom})` }"
      aria-hidden="true"
    >
      <OverlayPanelSample :panel="panel" />
    </div>
    <span v-if="chord" class="pointer-events-none absolute right-0.5 bottom-0.5 rounded bg-primary px-1 font-mono text-[9px] leading-tight font-semibold text-inverted">{{ chord }}</span>
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
