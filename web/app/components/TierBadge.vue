<script setup lang="ts">
// Filled S/A/B/C/D pill. The tier is computed server-side
// (ChampionTierCalculator); this component only maps the letter to its colour.
// Lives at the top level so it auto-registers as <TierBadge>.
//
// A filled pill rather than the bare coloured glyph this used to be. The
// original reason was that the teal ladder put `A` and `B` one desaturation
// step apart and they were hard to tell apart as bare letters; the medal ladder
// is back (gold vs silver is not a subtle difference) so that argument no
// longer applies. The pill stays for the remaining one: it makes the tier the
// loudest thing in the row, which is what the column a tier list is sorted by
// should be. Reverting to the bare letter is a one-line change if that reads as
// too much.
//
// Dark ink on every fill, checked rather than assumed: the medal stops give
// 8.0 / 10.6 / 10.9 / 5.2 / 4.7 : 1 against `ink-950`, so all five clear AA.
const props = defineProps<{
  /** Tier letter from the API: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
}>()

// Static per-letter class strings so Tailwind's scanner can see every
// `text-tier-*` utility it must generate — a computed `text-tier-${x}` would
// be invisible to the static scan and render as unstyled text. Colours come
// from the --color-tier-* tokens in main.css.
const TIER_CLASS: Record<string, string> = {
  S: 'text-tier-s',
  A: 'text-tier-a',
  B: 'text-tier-b',
  C: 'text-tier-c',
  D: 'text-tier-d',
}

const normalized = computed(() => props.tier?.toUpperCase() ?? '')
const isKnown = computed(() => normalized.value in TIER_CLASS)
// Unknown / empty tiers (e.g. a row that predates tiering) stay a muted dash
// rather than a coloured letter — an unmeasured tier must not read as loudly
// as a measured one.
const colorClass = computed(() => TIER_CLASS[normalized.value] ?? 'text-muted')
const label = computed(() => (isKnown.value ? normalized.value : '–'))
</script>

<template>
  <span
    class="inline-flex h-6 min-w-7 items-center justify-center font-mono text-sm font-bold leading-none tabular-nums"
    :class="colorClass"
    :aria-label="isKnown ? `Tier ${normalized}` : 'Tier unknown'"
  >
    {{ label }}
  </span>
</template>
