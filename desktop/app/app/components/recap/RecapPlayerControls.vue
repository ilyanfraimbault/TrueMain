<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import { formatClock } from '~/utils/recording-moments'
import { formatRate } from '~/utils/recording-timeline'

/** The recap player's control row: play, the moments either side, the clock, the clip's in and out, speed, sound and full screen. */
defineProps<{
  playing: boolean
  currentMs: number
  lengthMs: number
  /** The recap of a full game: ranges are cut here. */
  editable: boolean
  rate: number
  rateItems: DropdownMenuItem[]
  /** Where the speed menu opens: inside the player while it is full screen, so it shows over it. */
  menuPortal: HTMLElement | true
  muted: boolean
  immersive: boolean
}>()

const emit = defineEmits<{
  toggle: []
  previous: []
  next: []
  setIn: []
  setOut: []
  mute: []
  fullscreen: []
}>()
</script>

<template>
  <div class="flex items-center gap-1">
    <UButton :icon="playing ? 'i-lucide-pause' : 'i-lucide-play'" color="neutral" variant="ghost" size="sm" :aria-label="playing ? 'Pause' : 'Play'" title="Play / pause (Space)" @click="emit('toggle')" />
    <UButton icon="i-lucide-skip-back" color="neutral" variant="ghost" size="sm" aria-label="Previous moment" title="Previous moment (Shift+←)" @click="emit('previous')" />
    <UButton icon="i-lucide-skip-forward" color="neutral" variant="ghost" size="sm" aria-label="Next moment" title="Next moment (Shift+→)" @click="emit('next')" />
    <span class="ml-1 text-xs tabular-nums text-muted">
      <span class="text-highlighted">{{ formatClock(currentMs) }}</span> / {{ formatClock(lengthMs) }}
    </span>

    <div class="ml-auto flex items-center gap-1">
      <template v-if="editable">
        <UButton label="Set in" color="neutral" variant="outline" size="xs" title="Start a clip at the playhead (I)" @click="emit('setIn')">
          <template #trailing><UKbd value="I" size="sm" /></template>
        </UButton>
        <UButton label="Set out" color="neutral" variant="outline" size="xs" title="End a clip at the playhead (O)" @click="emit('setOut')">
          <template #trailing><UKbd value="O" size="sm" /></template>
        </UButton>
      </template>
      <UDropdownMenu :items="rateItems" :portal="menuPortal" :content="{ align: 'end', side: 'top' }" :ui="{ content: 'min-w-28' }">
        <UButton
          :label="formatRate(rate)"
          color="neutral"
          variant="ghost"
          size="sm"
          class="tabular-nums"
          :class="rate !== 1 && 'text-primary'"
          aria-label="Playback speed"
          title="Playback speed (< / >)"
        />
      </UDropdownMenu>
      <UButton :icon="muted ? 'i-lucide-volume-x' : 'i-lucide-volume-2'" color="neutral" variant="ghost" size="sm" :aria-label="muted ? 'Unmute' : 'Mute'" title="Mute (M)" @click="emit('mute')" />
      <UButton :icon="immersive ? 'i-lucide-minimize' : 'i-lucide-maximize'" color="neutral" variant="ghost" size="sm" :aria-label="immersive ? 'Exit full screen' : 'Full screen'" :title="immersive ? 'Exit full screen (F / Esc)' : 'Full screen (F)'" @click="emit('fullscreen')" />
    </div>
  </div>
</template>
