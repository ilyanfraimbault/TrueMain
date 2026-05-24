<script setup lang="ts">
import type { RegionSlug } from '~~/shared/types/leaderboard'

// Inline circle-flag SVGs for the three exposed region pills. Kept local
// to avoid pulling in an icon pack (~4 KB total inlined vs. a 20 KB+
// iconify dep). The SVGs are intentionally stylised — the brain reads
// them as flags from the colour signature, not from cartographic accuracy.
const props = defineProps<{
  region: RegionSlug | null | undefined
  size?: number
}>()

const dim = computed(() => props.size ?? 18)
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
    class="inline-flex items-center justify-center overflow-hidden rounded-full ring-1 ring-default/40"
    :style="{ width: `${dim}px`, height: `${dim}px` }"
    :aria-label="label"
    :title="label"
    role="img"
  >
    <!-- Europe — circle of yellow stars on EU blue. -->
    <svg
      v-if="region === 'europe'"
      viewBox="0 0 24 24"
      :width="dim"
      :height="dim"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="12" cy="12" r="12" fill="#003399" />
      <g fill="#FFCC00">
        <circle cx="12" cy="5" r="0.9" />
        <circle cx="15.5" cy="6" r="0.9" />
        <circle cx="18" cy="8.5" r="0.9" />
        <circle cx="19" cy="12" r="0.9" />
        <circle cx="18" cy="15.5" r="0.9" />
        <circle cx="15.5" cy="18" r="0.9" />
        <circle cx="12" cy="19" r="0.9" />
        <circle cx="8.5" cy="18" r="0.9" />
        <circle cx="6" cy="15.5" r="0.9" />
        <circle cx="5" cy="12" r="0.9" />
        <circle cx="6" cy="8.5" r="0.9" />
        <circle cx="8.5" cy="6" r="0.9" />
      </g>
    </svg>

    <!-- Americas — stylised stars-and-stripes (NA1 dominates the group). -->
    <svg
      v-else-if="region === 'americas'"
      viewBox="0 0 24 24"
      :width="dim"
      :height="dim"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="12" cy="12" r="12" fill="#FFFFFF" />
      <g fill="#B22234">
        <rect x="0" y="2" width="24" height="2" />
        <rect x="0" y="6" width="24" height="2" />
        <rect x="0" y="10" width="24" height="2" />
        <rect x="0" y="14" width="24" height="2" />
        <rect x="0" y="18" width="24" height="2" />
        <rect x="0" y="22" width="24" height="2" />
      </g>
      <rect x="0" y="0" width="11" height="10" fill="#3C3B6E" />
      <g fill="#FFFFFF">
        <circle cx="3" cy="3" r="0.6" />
        <circle cx="6" cy="3" r="0.6" />
        <circle cx="9" cy="3" r="0.6" />
        <circle cx="4.5" cy="5" r="0.6" />
        <circle cx="7.5" cy="5" r="0.6" />
        <circle cx="3" cy="7" r="0.6" />
        <circle cx="6" cy="7" r="0.6" />
        <circle cx="9" cy="7" r="0.6" />
      </g>
    </svg>

    <!-- Korea — taegeuk on white, simplified to the red/blue swirl. -->
    <svg
      v-else-if="region === 'korea'"
      viewBox="0 0 24 24"
      :width="dim"
      :height="dim"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="12" cy="12" r="12" fill="#FFFFFF" />
      <path
        d="M12 4 a 8 8 0 0 1 0 16 a 4 4 0 0 0 0 -8 a 4 4 0 0 1 0 -8 z"
        fill="#CD2E3A"
      />
      <path
        d="M12 4 a 8 8 0 0 0 0 16 a 4 4 0 0 1 0 -8 a 4 4 0 0 0 0 -8 z"
        fill="#0047A0"
      />
    </svg>

    <!-- Fallback: neutral globe glyph. -->
    <UIcon
      v-else
      name="i-lucide-globe"
      class="size-3 text-muted"
    />
  </span>
</template>
