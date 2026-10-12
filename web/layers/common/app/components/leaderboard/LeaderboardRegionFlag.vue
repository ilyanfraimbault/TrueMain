<script setup lang="ts">
import type { RegionSlug } from '#shared/types/leaderboard'

// A region's flag at a given width, 3:2. The artwork is the layer's `region`
// icon collection (`app/assets/icons/region`), the same one the leaderboard's
// region select passes to its items' native `icon`. Those SVGs are square with
// the flag centred, so the icon is drawn w×w and the w×h box crops the empty
// bands above and below. The flags are intentionally stylised — at this size
// the eye reads the flag from the colour signature, not cartographic accuracy.
const props = defineProps<{
  region: RegionSlug | null | undefined
  width?: number
}>()

const w = computed(() => props.width ?? 24)
const h = computed(() => Math.round(w.value * (16 / 24)))
const label = computed(() => {
  switch (props.region) {
    case 'europe': return 'Europe'
    case 'americas': return 'Americas'
    case 'korea': return 'Korea'
    default: return 'Unknown region'
  }
})
</script>

<template>
  <span
    class="inline-flex shrink-0 items-center justify-center overflow-hidden"
    :style="{ width: `${w}px`, height: `${h}px` }"
    :aria-label="label"
    :title="label"
    role="img"
  >
    <UIcon
      v-if="region"
      :name="`i-region-${region}`"
      class="shrink-0"
      :style="{ width: `${w}px`, height: `${w}px` }"
    />
    <!-- Fallback: neutral globe glyph. -->
    <UIcon
      v-else
      name="i-lucide-globe"
      class="size-3 text-muted"
    />
  </span>
</template>
