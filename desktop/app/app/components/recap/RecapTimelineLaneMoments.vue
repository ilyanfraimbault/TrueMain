<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import type { PlayerLane } from '~/utils/recording-moments'
import type { TimelineView } from '~/utils/recording-timeline'
import { formatClock, momentLabel, momentMark } from '~/utils/recording-moments'
import { timelineLaneCenter, viewPercent } from '~/utils/recording-timeline'

/** The player's own moments on the timeline, each kind on its lane. */
defineProps<{
  /** In view, bigger multi-kills last so a penta is never hidden under a single kill. */
  lanes: { lane: PlayerLane, moments: Moment[] }[]
  view: TimelineView
}>()

const emit = defineEmits<{ moment: [moment: Moment] }>()
</script>

<template>
  <template v-for="{ lane, moments: laneMoments } in lanes" :key="lane">
    <button
      v-for="(moment, index) in laneMoments"
      :key="`${lane}-${index}`"
      type="button"
      class="absolute flex -translate-1/2 items-center justify-center rounded-full text-[9px] font-bold leading-none transition-transform hover:z-10 hover:scale-125"
      :class="[momentMark(moment), moment.kills > 1 && 'px-1', lane !== 'death' && 'ring-2 ring-(--ui-bg-muted)']"
      :style="{ left: viewPercent(view, moment.videoMs), top: `${timelineLaneCenter(lane)}px` }"
      :title="`${momentLabel(moment)} · ${formatClock(moment.videoMs)}`"
      :aria-label="`${momentLabel(moment)} at ${formatClock(moment.videoMs)}`"
      @pointerdown.stop
      @click="emit('moment', moment)"
    >
      <template v-if="moment.kills > 1">×{{ moment.kills }}</template>
      <RecapMomentIcon v-else-if="lane !== 'assist'" :kind="moment.kind" class="size-2.5" />
    </button>
  </template>
</template>
