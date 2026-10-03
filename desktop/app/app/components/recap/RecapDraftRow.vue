<script setup lang="ts">
import type { DraftClip } from '~/composables/useRecapDrafts'
import { formatClock } from '~/utils/recording-moments'

/** One range being cut: its name (proposed, editable), its place in the game, a preview, and Save. */
const props = defineProps<{ draft: DraftClip, selected: boolean }>()
const emit = defineEmits<{
  select: []
  rename: [title: string]
  preview: []
  remove: []
  save: []
}>()

const length = computed(() => formatClock(props.draft.endMs - props.draft.startMs))
</script>

<template>
  <div
    class="flex flex-col gap-1.5 rounded-lg p-2 ring-1 transition-colors"
    :class="selected ? 'bg-accented ring-primary/60' : 'bg-elevated ring-default hover:ring-accented'"
    @click="emit('select')"
  >
    <UInput
      :model-value="draft.title"
      size="sm"
      variant="none"
      placeholder="Name this clip"
      aria-label="Clip name"
      :maxlength="80"
      :disabled="draft.saving"
      class="-mx-1"
      :ui="{ base: 'px-1 font-medium text-highlighted focus:bg-muted rounded' }"
      @update:model-value="emit('rename', String($event))"
      @keydown.enter="($event.target as HTMLInputElement).blur()"
    />
    <div class="flex items-center gap-1">
      <span class="text-[11px] tabular-nums text-muted">
        {{ formatClock(draft.startMs) }} → {{ formatClock(draft.endMs) }}
        <span class="text-dimmed">· {{ length }}</span>
      </span>
      <div class="ml-auto flex items-center gap-0.5">
        <UButton icon="i-lucide-play" color="neutral" variant="ghost" size="xs" aria-label="Preview" title="Play this range" :disabled="draft.saving" @click.stop="emit('preview')" />
        <UButton icon="i-lucide-x" color="neutral" variant="ghost" size="xs" aria-label="Remove" title="Remove this range" :disabled="draft.saving" @click.stop="emit('remove')" />
        <UButton
          label="Save"
          icon="i-lucide-save"
          size="xs"
          :loading="draft.saving"
          @click.stop="emit('save')"
        />
      </div>
    </div>
  </div>
</template>
