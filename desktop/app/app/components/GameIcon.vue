<script setup lang="ts">
/** A rune, spell or item icon; an empty square while its static data loads. */
defineProps<{
  source: { name: string, icon: string } | null
  /** Tailwind size class. */
  size?: string
  round?: boolean
}>()
</script>

<template>
  <img
    v-if="source"
    :src="source.icon"
    :alt="source.name"
    :title="source.name"
    class="shrink-0 object-cover"
    :class="[size ?? 'size-9', round ? 'rounded-full' : 'rounded']"
  >
  <span v-else class="shrink-0 rounded bg-ink-900" :class="size ?? 'size-9'" />
</template>
