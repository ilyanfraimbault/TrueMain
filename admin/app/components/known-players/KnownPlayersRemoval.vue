<script setup lang="ts">
// Removing a known player (#2019), the two ways: a plain removal for a player that
// should not exist, a takedown for a person who asked not to be listed. Both ask
// twice; the first click arms, the second sends. Emits; the container calls the API.
import { ref } from 'vue'

const props = defineProps<{
  displayName: string
  busy: boolean
}>()

const emit = defineEmits<{ remove: [takedown: boolean] }>()

const OPTIONS = [
  {
    key: 'remove',
    takedown: false,
    label: 'Remove',
    icon: 'i-lucide-user-x',
    description: 'The page goes and the sources can no longer recreate it. Its accounts stay free for another known player.',
  },
  {
    key: 'takedown',
    takedown: true,
    label: 'Take down',
    icon: 'i-lucide-shield-off',
    description: 'The page goes, and its accounts are barred from every known player, whatever a source lists.',
  },
] as const

const armed = ref<string | null>(null)

function choose(key: string, takedown: boolean) {
  if (props.busy) {
    return
  }
  if (armed.value !== key) {
    armed.value = key
    return
  }
  armed.value = null
  emit('remove', takedown)
}
</script>

<template>
  <section class="space-y-3">
    <p class="text-xs uppercase text-muted">
      Remove {{ displayName }}
    </p>
    <ul class="space-y-3">
      <li v-for="option in OPTIONS" :key="option.key" class="flex items-start gap-3">
        <UButton
          :data-action="option.key"
          :icon="option.icon"
          color="error"
          :variant="armed === option.key ? 'solid' : 'subtle'"
          :disabled="busy"
          :label="armed === option.key ? `Confirm: ${option.label}` : option.label"
          class="shrink-0"
          @click="choose(option.key, option.takedown)"
        />
        <p class="text-xs text-muted pt-1.5">
          {{ option.description }}
        </p>
      </li>
    </ul>
  </section>
</template>
