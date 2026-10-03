<script setup lang="ts" generic="T extends string | number">
/** The app's segmented control (the dashboard's queue filter), for a short list of exclusive choices. */
defineProps<{ items: { value: T, label: string }[] }>()
const model = defineModel<T>({ required: true })
</script>

<template>
  <div class="flex w-fit rounded-lg bg-elevated p-0.5 ring-1 ring-default" role="radiogroup">
    <button
      v-for="option in items"
      :key="option.value"
      type="button"
      role="radio"
      :aria-checked="model === option.value"
      class="rounded-md px-2.5 py-1 text-xs font-medium transition-colors"
      :class="model === option.value ? 'bg-accented text-highlighted' : 'text-muted hover:text-highlighted'"
      @click="model = option.value"
    >
      {{ option.label }}
    </button>
  </div>
</template>
