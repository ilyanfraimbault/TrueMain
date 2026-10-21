<script setup lang="ts">
/**
 * The overlay's confirmation that a press of the mark key was kept (#1767):
 * one line over the game — "Last 1 min saved" — that the shell
 * (`src-tauri/src/overlay`, `confirm`) shows a moment and hides, its window
 * sized to the line. Loaded by that window alone, like the panels, never by
 * the app's; click-through and never focused.
 *
 * The text comes with each press (`overlay://confirmation`); a page just
 * built asks for the press that built it (`overlay_confirmation`), emitted
 * before it listened. In a browser-only `npm run dev`, a sample line shows.
 */
const SAMPLE = 'Last 30 s saved'

const text = ref<string | null>(null)
const content = ref<HTMLElement | null>(null)

async function fit() {
  const element = content.value
  if (!element || !insideTauri()) return
  const { invoke } = await import('@tauri-apps/api/core')
  await invoke('overlay_fit', { panel: 'confirmation', width: element.offsetWidth, height: element.offsetHeight })
}

let stopListening: (() => void) | undefined
onBeforeUnmount(() => stopListening?.())
onMounted(async () => {
  if (!insideTauri()) {
    text.value = SAMPLE
    return
  }
  const { listen } = await import('@tauri-apps/api/event')
  stopListening = await listen<string>('overlay://confirmation', (event) => {
    text.value = event.payload
  })
  const { invoke } = await import('@tauri-apps/api/core')
  text.value = (await invoke<string | null>('overlay_confirmation')) ?? text.value
})
watch(text, () => void nextTick(fit))

useHead({
  title: 'TrueMain overlay',
  htmlAttrs: { class: 'overflow-hidden bg-default' },
  bodyAttrs: { class: 'overflow-hidden bg-default' },
})
</script>

<template>
  <div ref="content" class="inline-flex items-center gap-2 px-3 py-2 whitespace-nowrap" role="status">
    <UIcon name="i-lucide-bookmark-check" class="size-4 shrink-0 text-primary" />
    <span class="text-xs font-semibold text-highlighted">{{ text ?? '' }}</span>
  </div>
</template>
