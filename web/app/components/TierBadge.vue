<script setup lang="ts">
// Filled S/A/B/C/D pill. The tier is computed server-side
// (ChampionTierCalculator); this component only maps the letter to its colour.
// Lives at the top level so it auto-registers as <TierBadge>.
//
// A filled pill rather than the bare coloured glyph it used to be. Since the
// ladder was realigned onto the data axis (#1060), `A` (#7fc9c0) and `B`
// (#8b8b95) are one desaturation step apart — as bare letters at `text-lg` the
// two were genuinely hard to tell apart, which is fatal for the one column a
// tier list exists to be sorted by. A saturated fill separates them at a
// glance, and it makes the tier the loudest thing in the row, which is what it
// should be.
const props = defineProps<{
  /** Tier letter from the API: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
}>()

// Static per-letter class strings so Tailwind's scanner can see every
// `bg-tier-*` utility it must generate — a computed `bg-tier-${x}` would be
// invisible to the static scan and render as an unstyled pill. Colours come
// from the --color-tier-* tokens in main.css.
//
// The letter is `ink-950` on every tier, not `text-inverted`: the fills run
// from a bright teal to a mid grey, all of them light enough to need dark ink,
// and `inverted` is a theme token that would flip if a light mode ever returned
// — while these fills would not.
const TIER_CLASS: Record<string, string> = {
  S: 'bg-tier-s text-ink-950',
  A: 'bg-tier-a text-ink-950',
  B: 'bg-tier-b text-ink-950',
  C: 'bg-tier-c text-ink-950',
  D: 'bg-tier-d text-ink-950',
}

const normalized = computed(() => props.tier?.toUpperCase() ?? '')
const isKnown = computed(() => normalized.value in TIER_CLASS)
// Unknown / empty tiers (e.g. a row that predates tiering) stay an outlined
// muted dash rather than a filled pill — an unmeasured tier must not read as
// loudly as a measured one.
const colorClass = computed(() =>
  TIER_CLASS[normalized.value] ?? 'text-muted ring-1 ring-inset ring-accented')
const label = computed(() => (isKnown.value ? normalized.value : '–'))
</script>

<template>
  <span
    class="inline-flex h-6 min-w-7 items-center justify-center rounded-md px-1.5 font-mono text-sm font-bold leading-none tabular-nums"
    :class="colorClass"
    :aria-label="isKnown ? `Tier ${normalized}` : 'Tier unknown'"
  >
    {{ label }}
  </span>
</template>
