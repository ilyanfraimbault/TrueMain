<script setup lang="ts">
import type { Clip } from '~/types/recordings'
import { formatBytes, revealLabel } from '~/utils/recording-library'
import { formatClock } from '~/utils/recording-moments'

/** The publish dialog's clip, dragged onto YouTube's upload page — or found in its folder. */
defineProps<{
  clip: Clip
  thumbnail: string | null
}>()

const emit = defineEmits<{ thumbnailError: [] }>()

const { reveal } = useRecordings()
const { startDrag } = useClipPublish()
</script>

<template>
  <div
    draggable="true"
    role="button"
    tabindex="-1"
    aria-label="Drag the clip's video onto YouTube"
    class="surface surface-hover flex cursor-grab items-center gap-3 rounded-lg p-2 active:cursor-grabbing"
    @dragstart.prevent="startDrag(clip.id)"
  >
    <UIcon name="i-lucide-grip-vertical" class="size-4 shrink-0 text-dimmed" />
    <img v-if="thumbnail" :src="thumbnail" alt="" draggable="false" class="aspect-video w-24 shrink-0 rounded object-cover" @error="emit('thumbnailError')">
    <span v-else class="flex aspect-video w-24 shrink-0 items-center justify-center rounded bg-ink-900">
      <UIcon name="i-lucide-scissors" class="size-4 text-ink-700" />
    </span>
    <div class="min-w-0 flex-1 leading-tight">
      <p class="truncate text-sm font-medium text-highlighted">{{ clip.title }}</p>
      <p class="text-xs text-muted tabular-nums">{{ formatClock(clip.durationMs) }} · {{ formatBytes(clip.sizeBytes) }}</p>
    </div>
    <UIcon name="i-lucide-hand-grab" class="size-4 shrink-0 text-dimmed" />
  </div>
  <UButton
    :label="revealLabel()"
    icon="i-lucide-folder-open"
    color="neutral"
    variant="link"
    size="xs"
    class="self-start px-0"
    @click="reveal(clip.videoPath)"
  />
</template>
