<script setup lang="ts">
import type { ItemSetSubject } from '~/composables/useItemSetImport'

/**
 * The item set import button (#1908): the rune button's twin, in the corner of
 * the build's items, there whenever the client is. The label lives in its
 * tooltip; the toast says how it went.
 */
const props = defineProps<{
  subject: ItemSetSubject
}>()

const { available, pending, done, importItemSet } = useItemSetImport()
</script>

<template>
  <UTooltip v-if="available" :text="done ? 'Imported' : 'Import items into the client'" :content="{ side: 'left' }">
    <UButton
      size="xs"
      color="neutral"
      variant="ghost"
      square
      :icon="done ? 'i-lucide-check' : 'i-lucide-download'"
      :loading="pending"
      aria-label="Import items into the client"
      class="text-dimmed hover:text-highlighted"
      @click="importItemSet(props.subject)"
    />
  </UTooltip>
</template>
