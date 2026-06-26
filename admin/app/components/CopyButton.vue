<script setup lang="ts">
// Reusable copy-to-clipboard button. Uses VueUse's `useClipboard` (already a
// project dependency via @vueuse/nuxt) and a toast for feedback. Hidden when the
// browser has no clipboard support. `@click.stop` so it can sit inside a clickable
// row/panel without triggering the row's own click.
const props = withDefaults(defineProps<{
  /** The text placed on the clipboard when clicked. */
  text: string
  /** Button label; defaults to "Copy". */
  label?: string
}>(), {
  label: 'Copy',
})

const { copy, copied, isSupported } = useClipboard()
const toast = useToast()

async function onCopy() {
  if (!isSupported.value) {
    return
  }
  await copy(props.text)
  toast.add({
    title: 'Copied to clipboard',
    icon: 'i-lucide-check',
    color: 'success',
  })
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
