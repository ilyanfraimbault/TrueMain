<script setup lang="ts">
import { follow, shell, shellEvents } from '~/utils/shell'
/**
 * The overlay's confirmation that a press of the mark key was kept (#1767):
 * one line over the game — "Last 1 min saved" — that the shell
 * (`src-tauri/src/overlay`, `confirm`) shows a moment and hides, its window
 * sized to the line. Loaded by that window alone, like the panels, never by
 * the app's; click-through and never focused.
 *
 * The text comes with each press; a page just built asks for the press that
 * built it, emitted before it listened.
 */
const text = ref<string | null>(null)
const content = ref<HTMLElement | null>(null)

async function fit() {
  const element = content.value
  if (!element) return
  await shell.overlayFit('confirmation', element.offsetWidth, element.offsetHeight)
}

let stopListening: (() => void) | undefined
onBeforeUnmount(() => stopListening?.())
onMounted(async () => {
  stopListening = await follow(shellEvents.overlayConfirmation, shell.overlayConfirmation, (next) => {
    if (next !== null) text.value = next
  })
})
watch(text, () => void nextTick(fit))

useHead({
  title: 'TrueMain overlay',
  htmlAttrs: { class: 'overflow-hidden bg-default' },
  bodyAttrs: { class: 'overflow-hidden bg-default' },
})
</script>

<template>
  <output ref="content" class="inline-flex items-center gap-2 px-3 py-2 whitespace-nowrap">
    <UIcon name="i-lucide-bookmark-check" class="size-4 shrink-0 text-primary" />
    <span class="text-xs font-semibold text-highlighted">{{ text ?? '' }}</span>
  </output>
</template>
