<script setup lang="ts">
// Reusable copy-to-clipboard button. Uses VueUse's `useClipboard` (already a
// project dependency via @vueuse/nuxt) and a toast for feedback. `legacy: true`
// falls back to `document.execCommand('copy')` when the async Clipboard API is
// unavailable (older/hardened browsers, non-fully-secure contexts) — without it
// `isSupported` goes false and the button silently disappears, which is exactly
// what happened on the admin Logs page. `@click.stop` so it can sit inside a
// clickable row/panel without triggering the row's own click.
const props = withDefaults(defineProps<{
  /** The text placed on the clipboard when clicked. */
  text: string
  /** Button label; defaults to "Copy". */
  label?: string
}>(), {
  label: 'Copy',
})

const { copy, copied, isSupported } = useClipboard({ legacy: true })
const toast = useToast()

async function onCopy() {
  try {
    await copy(props.text)
    toast.add({
      title: 'Copied to clipboard',
      icon: 'i-lucide-check',
      color: 'success',
    })
  } catch {
    toast.add({
      title: 'Could not copy to clipboard',
      icon: 'i-lucide-x',
      color: 'error',
    })
  }
}
</script>

<template>
  <UButton
    v-if="isSupported"
    :icon="copied ? 'i-lucide-check' : 'i-lucide-copy'"
    :color="copied ? 'success' : 'neutral'"
    variant="subtle"
    size="xs"
    :label="label"
    @click.stop="onCopy"
  />
</template>
