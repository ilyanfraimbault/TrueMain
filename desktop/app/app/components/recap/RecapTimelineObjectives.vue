<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import type { TimelineView } from '~/utils/recording-timeline'
import { formatClock, isMarked, isStructure, momentLabel, momentTone } from '~/utils/recording-moments'
import { viewPercent } from '~/utils/recording-timeline'

/**
 * The game's objectives above the timeline's lanes — the side that took one is
 * its colour — and the moments the player marked with the mark key (#2079).
 */
defineProps<{
  /** In view, structures first so an epic monster's badge is drawn over a tower that fell at the same time. */
  objectives: Moment[]
  view: TimelineView
}>()

const emit = defineEmits<{ moment: [moment: Moment] }>()
</script>

<template>
  <button
    v-for="(moment, index) in objectives"
    :key="`objective-${index}`"
    type="button"
    class="absolute top-0.5 flex -translate-x-1/2 items-center justify-center rounded-md transition-transform hover:z-10 hover:scale-125"
    :class="[
      momentTone(moment),
      isStructure(moment)
        ? 'size-5 opacity-75 hover:opacity-100'
        : isMarked(moment)
          ? 'size-5 bg-primary/15 ring-1 ring-primary/60'
          : ['size-5 ring-1', moment.ally === false ? 'bg-enemy/15 ring-enemy/50' : 'bg-ally/15 ring-ally/50'],
    ]"
    :style="{ left: viewPercent(view, moment.videoMs) }"
    :title="`${momentLabel(moment)} · ${formatClock(moment.videoMs)}`"
    :aria-label="`${momentLabel(moment)} at ${formatClock(moment.videoMs)}`"
    @pointerdown.stop
    @click="emit('moment', moment)"
  >
    <RecapMomentIcon :kind="moment.kind" :class="isStructure(moment) ? 'size-3' : 'size-3.5'" />
  </button>
</template>
