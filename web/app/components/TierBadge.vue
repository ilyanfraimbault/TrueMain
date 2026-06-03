<script setup lang="ts">
// Color-coded S/A/B/C/D performance-tier pill for the champions list. The
// tier itself is computed server-side (ChampionTierCalculator); this component
// only maps the letter to its colour. Lives at the top level so it
// auto-registers as <TierBadge>.
const props = defineProps<{
  /** Tier letter from the API: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
}>()

// Static per-letter class strings so Tailwind's scanner can see every
// `text-tier-*` / `ring-tier-*` utility it must generate — a computed
// `text-tier-${x}` would be invisible to the static scan and fall back to
// the default colour. Colours come from the --color-tier-* tokens in main.css.
const TIER_CLASS: Record<string, string> = {
  S: 'text-tier-s ring-tier-s/40',
  A: 'text-tier-a ring-tier-a/40',
  B: 'text-tier-b ring-tier-b/40',
  C: 'text-tier-c ring-tier-c/40',
  D: 'text-tier-d ring-tier-d/40',
}

const normalized = computed(() => props.tier?.toUpperCase() ?? '')
const isKnown = computed(() => normalized.value in TIER_CLASS)
// Unknown / empty tiers (e.g. a row that predates tiering) render a muted,
// uncoloured dash rather than a broken badge.
const colorClass = computed(() => TIER_CLASS[normalized.value] ?? 'text-muted ring-default/40')
const label = computed(() => (isKnown.value ? normalized.value : '–'))
</script>

<template>
  <span
    class="inline-flex size-6 items-center justify-center rounded-md bg-elevated/40 text-sm font-bold ring-1 backdrop-blur-sm tabular-nums"
    :class="colorClass"
    :aria-label="isKnown ? `Tier ${normalized}` : 'Tier unknown'"
  >
    {{ label }}
  </span>
</template>
