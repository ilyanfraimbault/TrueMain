<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import { OVERLAY_PANELS } from '~/types/overlay'

/**
 * One panel of the in-game overlay (#1795) — `next-item`, `win-probability`
 * or `item-value` — loaded by that panel's own window (`src-tauri/src/overlay`),
 * never by the app's window, and drawn outside the app's shell (`app.vue`).
 *
 * The panels are click-through and never take the keyboard, so nothing here
 * is interactive: they are read at a glance over the game. Each measures
 * itself and the shell sizes its window to it, so a panel covers no more of
 * the game than its content. In the settings' preview each can be dragged
 * into place, and says so.
 *
 * In a browser-only `npm run dev`, `?scenario=<id>` plays a dev scenario's
 * game here (`useDevScenarios`) and `?preview` shows the preview's state.
 */
const route = useRoute()
const panel = computed<OverlayPanel>(() => {
  const slug = String(route.params.panel)
  return (OVERLAY_PANELS as string[]).includes(slug) ? slug as OverlayPanel : 'next-item'
})

const WIDTHS: Record<OverlayPanel, string> = {
  'next-item': 'w-[232px]',
  'win-probability': 'w-[160px]',
  'item-value': 'w-[300px]',
}
const PLACEHOLDERS: Record<OverlayPanel, string> = {
  'next-item': 'Your next item shows here during a game.',
  'win-probability': 'The win probability shows here during a game.',
  'item-value': 'Each team\'s item gold shows here while TAB is held.',
}

const { game } = useLiveGame()
const { view, fit } = useGameOverlay()

const settings = computed(() => view.value?.settings ?? null)
const preview = computed(() => (view.value?.preview ?? false) || devPreview.value)
const scale = computed(() => settings.value?.scale ?? 1)
// The next item is ours to buy; the other panels read a spectated game too.
const playing = computed(() => (panel.value !== 'next-item' || game.value?.myTeam ? game.value : null))

const devPreview = ref(false)
onMounted(async () => {
  if (!import.meta.dev || insideTauri()) return
  const query = new URL(window.location.href).searchParams
  devPreview.value = query.has('preview')
  const scenario = query.get('scenario')
  if (!scenario) return
  const { load, select } = useDevScenarios()
  await load()
  select(scenario)
})

// The window takes the content's size, scale included. The content is laid
// out at its natural size and scaled by a transform, so what is measured
// never depends on the scale it is drawn at.
const content = ref<HTMLElement | null>(null)
function measure() {
  const element = content.value
  if (element) void fit(panel.value, element.offsetWidth * scale.value, element.offsetHeight * scale.value)
}
let observer: ResizeObserver | undefined
onMounted(() => {
  observer = new ResizeObserver(measure)
  if (content.value) observer.observe(content.value)
})
onBeforeUnmount(() => observer?.disconnect())
watch(scale, measure)

useHead({
  title: 'TrueMain overlay',
  htmlAttrs: { class: 'overflow-hidden bg-default' },
  bodyAttrs: { class: 'overflow-hidden bg-default' },
})
</script>

<template>
  <div class="origin-top-left" :style="{ transform: `scale(${scale})` }">
    <div ref="content" class="relative px-3 py-2.5 text-default" :class="WIDTHS[panel]">
      <template v-if="playing">
        <OverlayNextItem v-if="panel === 'next-item'" :game="playing" />
        <OverlayWinProbability v-else-if="panel === 'win-probability'" :game="playing" />
        <OverlayItemValue v-else :game="playing" />
      </template>
      <div v-else class="flex items-center gap-2.5">
        <AppMark class="size-4 shrink-0" />
        <p class="text-[11px] leading-snug text-muted">{{ PLACEHOLDERS[panel] }}</p>
      </div>

      <div v-if="preview" class="mt-2 flex items-center gap-1.5 border-t border-default pt-1.5 text-[11px] text-primary">
        <UIcon name="i-lucide-move" class="size-3" />
        Drag to place it
      </div>

      <!-- Over everything in the preview, so a drag starts wherever it is grabbed. -->
      <div v-if="preview" data-tauri-drag-region class="absolute inset-0 cursor-grab" />
    </div>
  </div>
</template>
