<script setup lang="ts">
import type { ActivityMode } from '#shared/types/activity'

// The activity card's window switch. A segmented control rather than four loose
// buttons: the views are one choice, and the shared inset strip is what says so.
defineProps<{
  options: { key: ActivityMode, label: string }[]
  modelValue: ActivityMode
}>()

// Emitted on every click, the active window's included: the card closes its
// hover panel on any press of the switch.
const emit = defineEmits<{ 'update:modelValue': [mode: ActivityMode] }>()
</script>

<template>
  <div class="flex gap-0.5 rounded-md bg-elevated p-0.5">
    <button
      v-for="option in options"
      :key="option.key"
      type="button"
      class="rounded px-2 py-1 text-xs font-medium transition-colors"
      :class="modelValue === option.key
        ? 'bg-accented text-highlighted'
        : 'text-dimmed hover:text-default'"
      @click="emit('update:modelValue', option.key)"
    >
      {{ option.label }}
    </button>
  </div>
</template>
