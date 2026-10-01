<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import { formatClock, momentLabel, momentTone } from '~/utils/recording-moments'

/** The game's moments as a list to jump through; the one the playhead last passed is marked. */
const props = defineProps<{ moments: Moment[], currentMs: number }>()
const emit = defineEmits<{ jump: [moment: Moment] }>()

const sorted = computed(() => [...props.moments].sort((a, b) => a.videoMs - b.videoMs))
const lastPassed = computed(() => {
  let index = -1
  sorted.value.forEach((moment, at) => {
    if (moment.videoMs <= props.currentMs) index = at
  })
  return index
})
</script>

<template>
  <p v-if="!sorted.length" class="px-1 py-6 text-center text-xs text-muted">
    No moments for this game: its clock could not be tied to the video.
  </p>
  <ul v-else class="flex flex-col">
    <li v-for="(moment, index) in sorted" :key="index">
      <button
        type="button"
        class="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs transition-colors hover:bg-accented"
        :class="index === lastPassed && 'bg-elevated'"
        @click="emit('jump', moment)"
      >
        <RecapMomentIcon :kind="moment.kind" class="size-3.5 shrink-0" :class="momentTone(moment)" />
        <span class="truncate" :class="moment.ally === false ? 'text-muted' : 'text-default'">{{ momentLabel(moment) }}</span>
        <span class="ml-auto tabular-nums text-dimmed">{{ formatClock(moment.videoMs) }}</span>
      </button>
    </li>
  </ul>
</template>
