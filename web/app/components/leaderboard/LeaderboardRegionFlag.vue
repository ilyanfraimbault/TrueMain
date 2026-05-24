<script setup lang="ts">
import type { RegionSlug } from '~~/shared/types/leaderboard'

// Inline rectangular flags (24×16 viewBox) for the three exposed region
// pills. Kept local to avoid pulling in a flag icon pack for three icons.
// The SVGs are intentionally stylised — at this size the eye reads the flag
// from the colour signature, not from cartographic accuracy.
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
    class="inline-flex shrink-0 overflow-hidden rounded-sm ring-1 ring-default/40"
    :style="{ width: `${w}px`, height: `${h}px` }"
    :aria-label="label"
    :title="label"
    role="img"
  >
    <!-- Europe — circle of 12 yellow stars on EU blue. -->
    <svg
      v-if="region === 'europe'"
      viewBox="0 0 24 16"
      preserveAspectRatio="none"
      :width="w"
      :height="h"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect width="24" height="16" fill="#003399" />
      <g fill="#FFCC00">
        <circle cx="12" cy="3" r="0.75" />
        <circle cx="14.5" cy="3.7" r="0.75" />
        <circle cx="16.5" cy="5.6" r="0.75" />
        <circle cx="17.25" cy="8" r="0.75" />
        <circle cx="16.5" cy="10.4" r="0.75" />
        <circle cx="14.5" cy="12.3" r="0.75" />
        <circle cx="12" cy="13" r="0.75" />
        <circle cx="9.5" cy="12.3" r="0.75" />
        <circle cx="7.5" cy="10.4" r="0.75" />
        <circle cx="6.75" cy="8" r="0.75" />
        <circle cx="7.5" cy="5.6" r="0.75" />
        <circle cx="9.5" cy="3.7" r="0.75" />
      </g>
    </svg>

    <!-- Americas — stylised stars-and-stripes (NA1 dominates the group). -->
    <svg
      v-else-if="region === 'americas'"
      viewBox="0 0 24 16"
      preserveAspectRatio="none"
      :width="w"
      :height="h"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect width="24" height="16" fill="#FFFFFF" />
      <g fill="#B22234">
        <rect x="0" y="0" width="24" height="1.23" />
        <rect x="0" y="2.46" width="24" height="1.23" />
        <rect x="0" y="4.92" width="24" height="1.23" />
        <rect x="0" y="7.38" width="24" height="1.23" />
        <rect x="0" y="9.85" width="24" height="1.23" />
        <rect x="0" y="12.31" width="24" height="1.23" />
        <rect x="0" y="14.77" width="24" height="1.23" />
      </g>
      <rect x="0" y="0" width="9.6" height="8.61" fill="#3C3B6E" />
      <g fill="#FFFFFF">
        <circle cx="1.6" cy="1.5" r="0.5" />
        <circle cx="4" cy="1.5" r="0.5" />
        <circle cx="6.4" cy="1.5" r="0.5" />
        <circle cx="8" cy="1.5" r="0.5" />
        <circle cx="2.8" cy="3" r="0.5" />
        <circle cx="5.2" cy="3" r="0.5" />
        <circle cx="7.2" cy="3" r="0.5" />
        <circle cx="1.6" cy="4.3" r="0.5" />
        <circle cx="4" cy="4.3" r="0.5" />
        <circle cx="6.4" cy="4.3" r="0.5" />
        <circle cx="8" cy="4.3" r="0.5" />
        <circle cx="2.8" cy="5.7" r="0.5" />
        <circle cx="5.2" cy="5.7" r="0.5" />
        <circle cx="7.2" cy="5.7" r="0.5" />
        <circle cx="1.6" cy="7.1" r="0.5" />
        <circle cx="4" cy="7.1" r="0.5" />
        <circle cx="6.4" cy="7.1" r="0.5" />
        <circle cx="8" cy="7.1" r="0.5" />
      </g>
    </svg>

    <!-- Korea — Taegukgi: white field, taegeuk centred, four trigrams in
         the corners (positions 4 corners follow the canonical layout). -->
    <svg
      v-else-if="region === 'korea'"
      viewBox="0 0 24 16"
      preserveAspectRatio="none"
      :width="w"
      :height="h"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect width="24" height="16" fill="#FFFFFF" />
      <!-- Taegeuk: red top semicircle, blue bottom semicircle, plus the
           two small inner circles that give the yin-yang twist. -->
      <g transform="translate(12 8)">
        <path d="M -4,0 A 4,4 0 0,1 4,0 L -4,0 Z" fill="#CD2E3A" />
        <path d="M -4,0 A 4,4 0 0,0 4,0 L -4,0 Z" fill="#0047A0" />
        <circle cx="-2" cy="0" r="2" fill="#CD2E3A" />
        <circle cx="2" cy="0" r="2" fill="#0047A0" />
      </g>
      <!-- Four trigrams (geon / gam / li / gon) in the corners. Each bar
           is 0.45 tall; gaps between segments are 0.5 wide. -->
      <g fill="#000">
        <!-- Top-left (☰ geon — heaven, 3 solid bars) -->
        <g transform="translate(1.5 2.5)">
          <rect width="3" height="0.45" />
          <rect y="0.85" width="3" height="0.45" />
          <rect y="1.7" width="3" height="0.45" />
        </g>
        <!-- Top-right (☵ gam — water, broken / solid / broken) -->
        <g transform="translate(19.5 2.5)">
          <rect width="1.25" height="0.45" />
          <rect x="1.75" width="1.25" height="0.45" />
          <rect y="0.85" width="3" height="0.45" />
          <rect y="1.7" width="1.25" height="0.45" />
          <rect x="1.75" y="1.7" width="1.25" height="0.45" />
        </g>
        <!-- Bottom-left (☲ li — fire, solid / broken / solid) -->
        <g transform="translate(1.5 11.4)">
          <rect width="3" height="0.45" />
          <rect y="0.85" width="1.25" height="0.45" />
          <rect x="1.75" y="0.85" width="1.25" height="0.45" />
          <rect y="1.7" width="3" height="0.45" />
        </g>
        <!-- Bottom-right (☷ gon — earth, 3 broken bars) -->
        <g transform="translate(19.5 11.4)">
          <rect width="1.25" height="0.45" />
          <rect x="1.75" width="1.25" height="0.45" />
          <rect y="0.85" width="1.25" height="0.45" />
          <rect x="1.75" y="0.85" width="1.25" height="0.45" />
          <rect y="1.7" width="1.25" height="0.45" />
          <rect x="1.75" y="1.7" width="1.25" height="0.45" />
        </g>
      </g>
    </svg>

    <!-- Fallback: neutral globe glyph. -->
    <UIcon
      v-else
      name="i-lucide-globe"
      class="size-3 text-muted"
    />
  </span>
</template>
