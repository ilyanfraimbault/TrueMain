<script setup lang="ts">
/** The one "are you sure" of the recordings pages: a deletion, which cannot be undone. */
defineProps<{
  title: string
  description: string
  confirmLabel: string
}>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ confirm: [] }>()

function confirm() {
  open.value = false
  emit('confirm')
}
</script>

<template>
  <UModal v-model:open="open" :title="title" :description="description" :ui="{ footer: 'justify-end' }">
    <template #footer>
      <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
      <UButton :label="confirmLabel" color="error" icon="i-lucide-trash-2" @click="confirm" />
    </template>
  </UModal>
</template>
