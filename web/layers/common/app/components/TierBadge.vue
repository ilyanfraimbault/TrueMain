<script setup lang="ts">
// The tier mark: the S/A/B/C/D letter in its medal colour (`--color-tier-*`,
// theme.css), the one component every surface draws a tier with — the
// directory and tier list, the champion banner, the home teaser, and the
// desktop app's draft cards (#2026). The tier is computed server-side
// (ChampionTierCalculator); this only maps the letter to its colour.
// Lives at the top level so it auto-registers as <TierBadge>.
//
// A bare coloured glyph, not a filled pill: the medal ladder (gold vs silver)
// keeps neighbouring tiers apart on its own. `stacked` is the same letter at
// card size with "tier" under it, for a mark laid over champion art, where the
// drop shadow keeps it readable on any splash.
const props = defineProps<{
  /** Tier letter from the API: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
  /** The art-overlay form: a larger letter over a "tier" caption. */
  stacked?: boolean
}>()

// Static per-letter class strings so Tailwind's scanner can see every
// `text-tier-*` utility it must generate — a computed `text-tier-${x}` would
// be invisible to the static scan and render as unstyled text. Colours come
// from the --color-tier-* tokens in theme.css.
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
const ariaLabel = computed(() => (isKnown.value ? `Tier ${normalized.value}` : 'Tier unknown'))
</script>

<template>
  <span
    v-if="stacked"
    class="flex flex-col items-start leading-none"
    :class="colorClass"
    :aria-label="ariaLabel"
  >
    <span class="font-mono text-[22px] font-bold tracking-tight drop-shadow-[0_1px_2px_rgb(0_0_0/0.8)]">{{ label }}</span>
    <span class="mt-0.5 text-[9px] font-medium uppercase tracking-[0.14em] text-default/80">tier</span>
  </span>
  <span
    v-else
    class="inline-flex h-6 min-w-7 items-center justify-center font-mono text-sm font-bold leading-none tabular-nums"
    :class="colorClass"
    :aria-label="ariaLabel"
  >
    {{ label }}
  </span>
</template>
