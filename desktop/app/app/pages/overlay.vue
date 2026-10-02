<script setup lang="ts">
/**
 * The in-game overlay's content (#1747), loaded by the overlay's own panel
 * (`src-tauri/src/overlay`) — never by the app's window, and drawn outside
 * the app's shell (`app.vue`).
 *
 * The panel is click-through and never takes the keyboard, so nothing here
 * is interactive: it is read at a glance over the game. It measures itself
 * and the shell sizes the window to it, so the panel covers no more of the
 * game than its content. In the settings' preview it can be dragged into
 * place, and says so.
 *
 * In a browser-only `npm run dev`, `?scenario=<id>` plays a dev scenario's
 * game here (`useDevScenarios`) and `?preview` shows the preview's state.
 */
const { game } = useLiveGame()
const { view, fit } = useGameOverlay()

const settings = computed(() => view.value?.settings ?? null)
const preview = computed(() => (view.value?.preview ?? false) || devPreview.value)
const scale = computed(() => settings.value?.scale ?? 1)
const playing = computed(() => game.value?.myTeam ? game.value : null)

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
  if (element) void fit(element.offsetWidth * scale.value, element.offsetHeight * scale.value)
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
    <div ref="content" class="relative w-[232px] px-3 py-2.5 text-default">
      <OverlayNextItem v-if="playing" :game="playing" />
      <div v-else class="flex items-center gap-2.5">
        <AppMark class="size-4 shrink-0" />
        <p class="text-[11px] leading-snug text-muted">Your next item shows here during a game.</p>
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
