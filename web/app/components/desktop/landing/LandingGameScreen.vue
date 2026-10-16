<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { LandingOverlayPanelKind } from './LandingOverlayPanel.vue'
import { championSplashUrl, SAMPLE_ME } from '~/utils/desktop-landing-sample'

/**
 * A player's screen during a game, with the overlay's panels drawn where the
 * app puts them by default (`shell-state::overlay`: win probability top left,
 * item value top centre, next item top right, pace centre left). The game
 * itself is not shown — no capture of League is ours to publish — so the
 * screen is our champion's splash, dimmed, under the outlines of the game's
 * minimap and ability bar, the way the app's own overlay page draws a screen.
 *
 * Panels are drawn larger than life (a 1920-point screen shrunk to a page
 * column would make them unreadable): their scale follows the screen's width.
 */
const props = defineProps<{
  shown: LandingOverlayPanelKind[]
  tabHeld: boolean
  patch: string | null
  items: Record<number, StaticItemData> | null
}>()

const ANCHORS: Record<LandingOverlayPanelKind, string> = {
  'win-probability': 'left-[3%] top-[5%] origin-top-left',
  'item-value': 'left-1/2 top-[5%] origin-top -translate-x-1/2',
  'next-item': 'right-[3%] top-[5%] origin-top-right',
  'pace': 'left-[3%] top-1/2 origin-left -translate-y-1/2',
}

const screen = ref<HTMLElement | null>(null)
const scale = ref(0.7)
onMounted(() => {
  const el = screen.value
  if (!el || typeof ResizeObserver === 'undefined') return
  const observer = new ResizeObserver(([entry]) => {
    scale.value = Math.min(1, (entry?.contentRect.width ?? 900) / 860)
  })
  observer.observe(el)
  onBeforeUnmount(() => observer.disconnect())
})

const splash = useCanonicalSplash()(championSplashUrl(SAMPLE_ME))

const visible = (panel: LandingOverlayPanelKind) => props.shown.includes(panel) && (panel !== 'item-value' || props.tabHeld)
</script>

<template>
  <div ref="screen" class="relative aspect-video overflow-hidden rounded-xl bg-black ring-1 ring-white/10 shadow-[0_30px_80px_-24px_rgb(0_0_0/0.9)]">
    <div
      class="landing-drift absolute inset-0 bg-cover bg-center brightness-[0.4] saturate-[0.75] blur-[1.5px]"
      :style="{ backgroundImage: `url(${splash})` }"
    />
    <div class="absolute inset-0 bg-[radial-gradient(ellipse_at_center,transparent_40%,rgb(0_0_0/0.65))]" />
    <div class="absolute inset-0 bg-black/40 transition-opacity duration-500" :class="tabHeld ? 'opacity-100' : 'opacity-0'" />

    <!-- The game's own HUD, outlined: the panels keep clear of it, as in game. -->
    <div class="absolute bottom-[3%] right-[2%] aspect-square w-[15%] rounded border border-dashed border-white/25" />
    <div class="absolute bottom-[2%] left-1/2 h-[11%] w-[32%] -translate-x-1/2 rounded border border-dashed border-white/25" />

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

    <span class="absolute bottom-[20%] right-[2%] rounded-full bg-black/50 px-2 py-0.5 text-[10px] font-medium text-muted">Sample game · 26:00</span>
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

.landing-drift {
  animation: landing-drift 24s ease-in-out infinite alternate;
}

@keyframes landing-drift {
  from { transform: scale(1.04) translate(0, 0); }
  to { transform: scale(1.1) translate(-1.5%, -1%); }
}

@media (prefers-reduced-motion: reduce) {
  .landing-panel { transition: none; }
  .landing-drift { animation: none; }
}
</style>
