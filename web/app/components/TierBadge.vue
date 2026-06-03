<script setup lang="ts">
// Bare colour-coded S/A/B/C/D performance-tier letter for the champions list —
// no pill, no outline, just the coloured glyph. The tier is computed
// server-side (ChampionTierCalculator); this component only maps the letter to
// its colour. Lives at the top level so it auto-registers as <TierBadge>.
const props = defineProps<{
  /** Tier letter from the API: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
}>()

// Static per-letter class strings so Tailwind's scanner can see every
// `text-tier-*` utility it must generate — a computed `text-tier-${x}` would
// be invisible to the static scan and fall back to the default colour. Colours
// come from the --color-tier-* tokens in main.css.
const TIER_CLASS: Record<string, string> = {
  S: 'text-tier-s',
  A: 'text-tier-a',
  B: 'text-tier-b',
  C: 'text-tier-c',
  D: 'text-tier-d',
}

const normalized = computed(() => props.tier?.toUpperCase() ?? '')
const isKnown = computed(() => normalized.value in TIER_CLASS)
// Unknown / empty tiers (e.g. a row that predates tiering) render a muted,
// uncoloured dash rather than a broken badge.
const colorClass = computed(() => TIER_CLASS[normalized.value] ?? 'text-muted')
const label = computed(() => (isKnown.value ? normalized.value : '–'))
</script>

<template>
  <span
    class="inline-flex size-6 items-center justify-center text-lg font-extrabold tabular-nums"
    :class="colorClass"
    :aria-label="isKnown ? `Tier ${normalized}` : 'Tier unknown'"
  >
    {{ label }}
  </span>
</template>
