<script setup lang="ts">
// Navbar companion to `useLiveRefresh` (#1411): says how old the panel's live
// blocks are and lets the operator stop the timer.
//
// It ticks every second and prints seconds below the minute, unlike the shared
// `formatTimeAgo` which collapses that whole range to "just now": the point of
// the strip is to show that a 30 s cycle is actually running, and "just now" for
// half a minute is exactly what makes an operator doubt it.
const props = defineProps<{
  /** Epoch ms of the last refresh — `useLiveRefresh`'s `lastUpdatedAt`. */
  lastUpdatedAt: number
  /** Whether the operator has paused the timer. */
  paused: boolean
}>()

const emit = defineEmits<{ toggle: [] }>()

const now = ref(Date.now())
useIntervalFn(() => {
  now.value = Date.now()
}, 1000)

const label = computed(() => {
  const seconds = Math.max(0, Math.round((now.value - props.lastUpdatedAt) / 1000))
  if (seconds < 5) {
    return 'just now'
  }
  if (seconds < 60) {
    return `${seconds} s ago`
  }
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) {
    return `${minutes} min ago`
  }
  return `${Math.floor(minutes / 60)} h ago`
})
</script>

<template>
  <div class="flex items-center gap-1.5 text-xs text-muted">
    <span
      class="size-1.5 rounded-full shrink-0"
      :class="paused ? 'bg-muted' : 'bg-primary animate-pulse'"
      aria-hidden="true"
    />
    <span class="tabular-nums hidden sm:inline">
      updated {{ label }} · {{ paused ? 'paused' : 'live' }}
    </span>
    <UButton
      :icon="paused ? 'i-lucide-play' : 'i-lucide-pause'"
      color="neutral"
      variant="ghost"
      size="xs"
      :aria-label="paused ? 'Resume live refresh' : 'Pause live refresh'"
      @click="emit('toggle')"
    />
  </div>
</template>
