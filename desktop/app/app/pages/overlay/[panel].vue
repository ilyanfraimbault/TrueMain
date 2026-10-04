<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'
import { OVERLAY_PANELS } from '~/types/overlay'
import { SAMPLE_NEXT_ITEM, sampleGame, sampleLoadingView } from '~/utils/overlay-sample'

/**
 * One panel of the in-game overlay (#1795) — `next-item`, `win-probability`
 * or `item-value` — loaded by that panel's own window (`src-tauri/src/overlay`),
 * never by the app's window, and drawn outside the app's shell (`app.vue`).
 *
 * The panels are click-through and never take the keyboard, so nothing here
 * is interactive: they are read at a glance over the game. Each measures
 * itself and the shell sizes its window to it, so a panel covers no more of
 * the game than its content. In the settings' preview each can be dragged
 * into place, and says so; with no game to read, each then shows a sample
 * (`utils/overlay-sample.ts`) so it is placed at the size it will have over
 * the game — a panel flush against an edge stays flush against it.
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
  'stats': 'w-[176px]',
  'loading': 'w-[440px]',
}
const PLACEHOLDERS: Record<OverlayPanel, string> = {
  'next-item': 'Your next item shows here during a game.',
  'win-probability': 'The win probability shows here during a game.',
  'item-value': 'Each team\'s item gold shows here while TAB is held.',
  'stats': 'Your CS and gold per minute show here during a game.',
  'loading': 'Each player\'s form shows here on the loading screen.',
}

const { game, syncedAt } = useLiveGame()
const { view: loading } = useLoadingPlayers()
const { view, fit } = useGameOverlay()

const settings = computed(() => view.value?.settings ?? null)
const preview = computed(() => (view.value?.preview ?? false) || devPreview.value)
const scale = computed(() => settings.value?.scale ?? 1)
// The next item and the pace are ours; the other panels read a spectated game too.
const OURS: OverlayPanel[] = ['next-item', 'stats']
const playing = computed(() => (!OURS.includes(panel.value) || game.value?.myTeam ? game.value : null))

// A real game always wins over the sample.
const sample = computed(() => (preview.value && !playing.value ? sampleGame() : null))
const shown = computed(() => playing.value ?? sample.value)
const players = computed(() => {
  if (loading.value.players.length) return loading.value.players
  return preview.value ? sampleLoadingView().players : []
})
const isSample = computed(() => (panel.value === 'loading' ? !loading.value.players.length : !playing.value))
const sampleSyncedAt = Date.now()
const { items: statics } = useStaticData()

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
      <LoadingBoard v-if="panel === 'loading' && players.length" :players="players" />
      <template v-else-if="shown && panel !== 'loading'">
        <template v-if="panel === 'next-item'">
          <OverlayNextItem v-if="playing" :game="playing" />
          <OverlayNextItemCard
            v-else
            :item="statics[SAMPLE_NEXT_ITEM.itemId] ?? null"
            :name="statics[SAMPLE_NEXT_ITEM.itemId]?.name ?? 'Zhonya\'s Hourglass'"
            :missing="SAMPLE_NEXT_ITEM.missing"
          />
        </template>
        <OverlayWinProbability v-else-if="panel === 'win-probability'" :game="shown" :synced-at="playing ? syncedAt : sampleSyncedAt" />
        <OverlayStats v-else-if="panel === 'stats'" :game="shown" />
        <OverlayItemValue v-else :game="shown" />
      </template>
      <div v-else class="flex items-center gap-2.5">
        <AppMark class="size-4 shrink-0" />
        <p class="text-[11px] leading-snug text-muted">{{ PLACEHOLDERS[panel] }}</p>
      </div>

      <!--
        Over everything in the preview, so a drag starts wherever it is grabbed;
        drawn over the panel rather than under it, so the panel keeps the size
        it has over the game. The badge steps aside once the panel is grabbed.
      -->
      <div v-if="preview" data-tauri-drag-region class="group absolute inset-0 cursor-grab ring-2 ring-inset ring-primary/70">
        <span class="pointer-events-none absolute right-1 bottom-1 flex transition-opacity group-hover:opacity-0 items-center gap-1 rounded bg-primary px-1 py-0.5 text-[10px] font-semibold text-inverted">
          <UIcon name="i-lucide-move" class="size-2.5" />
          {{ isSample ? 'Sample · drag' : 'Drag' }}
        </span>
      </div>
    </div>
  </div>
</template>
