<script setup lang="ts">
import type { BuildRunePage } from '~/types/build'

/**
 * The rune import button (#1678): a small icon in the corner of the runes,
 * there whenever the client is, out of the way of the page it imports. The
 * label lives in its tooltip; the toast says how it went.
 */
const props = defineProps<{
  page: BuildRunePage
  champion: string
}>()

const { available, pending, done, importRunes } = useRuneImport()
</script>

<template>
  <UTooltip v-if="available" :text="done ? 'Imported' : 'Import runes into the client'" :content="{ side: 'left' }">
    <UButton
      size="xs"
      color="neutral"
      variant="ghost"
      square
      :icon="done ? 'i-lucide-check' : 'i-lucide-download'"
      :loading="pending"
      aria-label="Import runes into the client"
      class="text-dimmed hover:text-highlighted"
      @click="importRunes(props.page, props.champion)"
    />
  </UTooltip>
</template>
