<script setup lang="ts">
// The age label of the navigation cache (#2306): while a page draws payloads stored on
// an earlier visit and their revalidation has not landed, say how old the oldest one is
// — once it is older than `OPS_CACHE_LABEL_AFTER_MS`. A revalidation that lands clears
// it; one that fails leaves it, beside the page's own single stale notice (#1668).
import { useIntervalFn } from '@vueuse/core'
import { computed, ref } from 'vue'
import { OPS_CACHE_LABEL_AFTER_MS, oldestShownAt, opsCacheShown } from '~/utils/ops-cache'

const props = withDefaults(defineProps<{
  /** The registry to read — the portal's own unless a test passes one. */
  shown?: ReadonlyMap<string, number>
  /** Clock, injectable for tests. */
  now?: () => number
}>(), {
  shown: () => opsCacheShown,
  now: () => Date.now(),
})

const tick = ref(props.now())
useIntervalFn(() => {
  tick.value = props.now()
}, 5_000)

const ageMs = computed(() => {
  const oldest = oldestShownAt(props.shown)
  return oldest === null ? null : Math.max(tick.value, props.now()) - oldest
})

const label = computed(() => {
  if (ageMs.value === null || ageMs.value <= OPS_CACHE_LABEL_AFTER_MS) {
    return null
  }
  const minutes = Math.floor(ageMs.value / 60_000)
  return minutes < 60 ? `${minutes} min ago` : `${Math.floor(minutes / 60)} h ago`
})
</script>

<template>
  <output
    v-if="label"
    aria-label="Age of the cached figures"
    class="pointer-events-none fixed bottom-4 right-4 z-50 flex items-center gap-1.5 rounded-full bg-elevated/90 px-3 py-1.5 text-xs text-muted ring ring-default"
  >
    <UIcon name="i-lucide-history" class="size-3.5 shrink-0" />
    <span class="tabular-nums">Showing figures cached {{ label }}</span>
  </output>
</template>
