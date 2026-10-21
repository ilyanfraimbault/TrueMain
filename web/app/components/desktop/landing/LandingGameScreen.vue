<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { LandingOverlayPanelKind } from './LandingOverlayPanel.vue'

/**
 * A player's screen during a game — a real one, one of our own Yone games at
 * 13:23 (`public/desktop/game.webp`) — with the overlay's panels drawn where
 * the app puts them by default (`shell-state::overlay`: win probability top
 * left, objectives top centre, next item top right, pace centre left, item value
 * centre right (#2184), 16 points
 * from the sides and 56 from the top on a 1920-point screen).
 *
 * Panels are drawn a little larger than life (a 1920-point screen shrunk to a page
 * column would make them unreadable): their scale follows the screen's width.
 */
const props = defineProps<{
  shown: LandingOverlayPanelKind[]
  tabHeld: boolean
  patch: string | null
  items: Record<number, StaticItemData> | null
}>()

const ANCHORS: Record<LandingOverlayPanelKind, string> = {
  'win-probability': 'left-[0.85%] top-[5.2%] origin-top-left',
  'item-value': 'right-[0.85%] top-1/2 origin-right -translate-y-1/2',
  'next-item': 'right-[0.85%] top-[5.2%] origin-top-right',
  'pace': 'left-[0.85%] top-1/2 origin-left -translate-y-1/2',
  'objectives': 'left-1/2 top-[5.2%] origin-top -translate-x-1/2',
}

const screen = ref<HTMLElement | null>(null)
const scale = ref(0.5)
onMounted(() => {
  const el = screen.value
  if (!el || typeof ResizeObserver === 'undefined') return
  const observer = new ResizeObserver(([entry]) => {
    scale.value = Math.min(0.6, (entry?.contentRect.width ?? 900) / 1500)
  })
  observer.observe(el)
  onBeforeUnmount(() => observer.disconnect())
})

const visible = (panel: LandingOverlayPanelKind) => props.shown.includes(panel) && (panel !== 'item-value' || props.tabHeld)
</script>

<template>
  <div ref="screen" class="relative aspect-video overflow-hidden rounded-xl bg-black ring-1 ring-white/10 shadow-[0_30px_80px_-24px_rgb(0_0_0/0.9)]">
    <SkeletonPicture src="/desktop/game.webp" :width="1280" :height="720" alt="" loading="lazy" class="absolute inset-0 brightness-[0.8]" />
    <div class="absolute inset-0 bg-black/45 transition-opacity duration-500" :class="tabHeld ? 'opacity-100' : 'opacity-0'" />

    <div
      v-for="(anchor, panel) in ANCHORS"
      :key="panel"
      class="landing-panel absolute"
      :class="[anchor, { 'is-on': visible(panel) }]"
      :style="{ '--landing-scale': scale }"
    >
      <DesktopLandingOverlayPanel :panel="panel" :patch="patch" :items="items" />
    </div>

    <kbd
      class="keycap absolute bottom-[4%] left-[3%] rounded-md bg-elevated px-2 py-1 font-mono text-[11px] font-semibold text-highlighted ring-1 ring-white/10 transition-all duration-300"
      :class="tabHeld ? 'translate-y-px opacity-100' : 'translate-y-2 opacity-0'"
    >TAB</kbd>

  </div>
</template>

<style scoped>
.landing-panel {
  opacity: 0;
  scale: calc(var(--landing-scale) * 0.96);
  filter: blur(6px);
  transition: opacity 450ms cubic-bezier(0.2, 0, 0, 1), scale 450ms cubic-bezier(0.2, 0, 0, 1), filter 450ms cubic-bezier(0.2, 0, 0, 1);
}

.landing-panel.is-on {
  opacity: 1;
  scale: var(--landing-scale);
  filter: none;
}

@media (prefers-reduced-motion: reduce) {
  .landing-panel { transition: none; }
}
</style>
