<script setup lang="ts">
// Design-system playground (`pages/dev/design-system.vue`): the colour ramps, the data
// axis and the two text-only vocabularies (stats, rune trees).

// Written as literal class strings rather than built from a loop: Tailwind's
// scanner only emits utilities it can see statically, and an interpolated
// `bg-ink-${shade}` would render as an unstyled swatch — the exact failure this
// page exists to catch.
const INK_RAMP = [
  { name: '50', class: 'bg-ink-50', hex: '#f7f7f8' },
  { name: '100', class: 'bg-ink-100', hex: '#eeeef0' },
  { name: '200', class: 'bg-ink-200', hex: '#d9d9dd' },
  { name: '300', class: 'bg-ink-300', hex: '#b7b7be' },
  { name: '400', class: 'bg-ink-400', hex: '#8b8b95' },
  { name: '500', class: 'bg-ink-500', hex: '#6a6a74' },
  { name: '600', class: 'bg-ink-600', hex: '#4e4e57' },
  { name: '700', class: 'bg-ink-700', hex: '#3a3a42' },
  { name: '800', class: 'bg-ink-800', hex: '#26262c' },
  { name: '900', class: 'bg-ink-900', hex: '#17171b' },
  { name: '950', class: 'bg-ink-950', hex: '#0b0b0d' },
]

const ROSEGOLD_RAMP = [
  { name: '200', class: 'bg-rosegold-200', hex: '#f6cfc7' },
  { name: '300', class: 'bg-rosegold-300', hex: '#eeaea3' },
  { name: '400', class: 'bg-rosegold-400', hex: '#e58f83' },
  { name: '500', class: 'bg-rosegold-500', hex: '#d9736c' },
  { name: '600', class: 'bg-rosegold-600', hex: '#c25a58' },
  { name: '700', class: 'bg-rosegold-700', hex: '#a1454a' },
]

// Hexes are restated here for the caption only — the swatch itself paints from
// the CSS var, so a stale hex shows up as a caption that disagrees with the
// colour beside it. Keep them in step with `--color-data-*` in theme.css.
const DATA_AXIS = [
  { name: 'data-good', class: 'bg-data-good', hex: '#e58f83', use: 'Above average, win — rosegold-400' },
  { name: 'data-good-dim', class: 'bg-data-good-dim', hex: '#b88d8c', use: 'Large fills on the good side' },
  { name: 'data-mid', class: 'bg-data-mid', hex: '#8b8b95', use: 'Average, no signal — ink-400' },
  { name: 'data-bad-dim', class: 'bg-data-bad-dim', hex: '#7b7b85', use: 'Large fills below average' },
  { name: 'data-bad', class: 'bg-data-bad', hex: '#6a6a74', use: 'Below average, loss — ink-500' },
]

// The top of the data axis, kept out of `DATA_AXIS` because it is the one step
// that is never a fill: `--color-gold` is text and small marks only, so a
// swatch here would invite the first `bg-gold` on a measurement.
const DATA_STANDOUT = {
  name: 'gold',
  class: 'text-gold',
  hex: '#d9b676',
  use: 'Standout — a Perfect KDA, a 75+ performance score. The same token the MVP crown wears.',
}

// Text emphasis only, like `--color-stat-*` — so they are shown as *words*,
// which is the only way they are ever allowed to appear. A swatch here would
// invite the first `bg-rune-domination` in the app.
const RUNE_TONES = [
  { name: 'rune-precision', class: 'text-rune-precision', hex: '#c8aa6e', tree: 'Precision' },
  { name: 'rune-domination', class: 'text-rune-domination', hex: '#d0424b', tree: 'Domination' },
  { name: 'rune-sorcery', class: 'text-rune-sorcery', hex: '#7a9df0', tree: 'Sorcery' },
  { name: 'rune-resolve', class: 'text-rune-resolve', hex: '#61b96b', tree: 'Resolve' },
  { name: 'rune-inspiration', class: 'text-rune-inspiration', hex: '#49aab9', tree: 'Inspiration' },
]

// Riot's in-client vocabulary for stats, damage types and keywords, rendered by
// the tooltip parser's tag-class map. Words, never swatches: these are the
// tokens most likely to be mistaken for a scale, and the page should not be the
// first place someone sees one as a fill. Grouped the way `theme.css` groups
// them so the two read side by side.
const STAT_FAMILIES = [
  {
    family: 'Defensive / sustain',
    tones: [
      { name: 'stat-health', class: 'text-stat-health', hex: '#24a564', use: 'Flat HP / HP regen' },
      { name: 'stat-armor', class: 'text-stat-armor', hex: '#f3c057', use: 'Armor' },
      { name: 'stat-mr', class: 'text-stat-mr', hex: '#54e6ff', use: 'Magic resist' },
      { name: 'stat-tenacity', class: 'text-stat-tenacity', hex: '#8c72ff', use: 'Tenacity / slow resist' },
      { name: 'stat-hsp', class: 'text-stat-hsp', hex: '#6be695', use: 'Heal & shield power' },
      { name: 'stat-heal-reduction', class: 'text-stat-heal-reduction', hex: '#8d5874', use: 'Grievous wounds' },
    ],
  },
  {
    family: 'Resources / utility',
    tones: [
      { name: 'stat-mana', class: 'text-stat-mana', hex: '#00a6ed', use: 'Mana / mana regen' },
      { name: 'stat-haste', class: 'text-stat-haste', hex: '#ede2cf', use: 'Ability haste' },
      { name: 'stat-speed', class: 'text-stat-speed', hex: '#ffffff', use: 'Move speed / on-hit' },
    ],
  },
  {
    family: 'Offensive — physical',
    tones: [
      { name: 'stat-ad', class: 'text-stat-ad', hex: '#f19425', use: 'Attack damage' },
      { name: 'stat-lethality', class: 'text-stat-lethality', hex: '#f65e57', use: 'Lethality / armor pen' },
      { name: 'stat-crit', class: 'text-stat-crit', hex: '#ee2a00', use: 'Crit chance / crit damage' },
      { name: 'stat-as', class: 'text-stat-as', hex: '#ffe991', use: 'Attack speed' },
      { name: 'stat-vamp', class: 'text-stat-vamp', hex: '#d70045', use: 'Lifesteal / omnivamp' },
    ],
  },
  {
    family: 'Offensive — magical',
    tones: [
      { name: 'stat-ap', class: 'text-stat-ap', hex: '#7e78ff', use: 'Ability power' },
      { name: 'stat-magicpen', class: 'text-stat-magicpen', hex: '#cc6efc', use: 'Magic penetration' },
    ],
  },
  {
    family: 'Damage types',
    tones: [
      { name: 'stat-true', class: 'text-stat-true', hex: '#f5f5f5', use: 'True damage' },
      { name: 'stat-adaptive', class: 'text-stat-adaptive', hex: '#48c4b7', use: 'Adaptive damage (verified)' },
    ],
  },
  {
    family: 'Misc / structural',
    tones: [
      { name: 'stat-status', class: 'text-stat-status', hex: '#9366a9', use: 'CC keywords — Stun, Airborne' },
      { name: 'stat-stealth', class: 'text-stat-stealth', hex: '#b472a6', use: 'Stealth keywords' },
      { name: 'stat-shield', class: 'text-stat-shield', hex: '#e0c56e', use: 'Shield value' },
      { name: 'stat-gold', class: 'text-stat-gold', hex: '#c8aa6e', use: 'Gold per X seconds' },
      { name: 'stat-passive', class: 'text-stat-passive', hex: '#ffffff', use: 'Item passive label' },
      { name: 'stat-active', class: 'text-stat-active', hex: '#f3c057', use: 'Item active label' },
    ],
  },
]
</script>

<template>
  <div class="flex flex-col gap-10">
    <!-- ─── Colour ramps ─────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Ink — the neutral"
      subtitle="Every surface, border and text colour derives from this ramp."
    >
      <div class="flex flex-wrap gap-2">
        <div
          v-for="stop in INK_RAMP"
          :key="stop.name"
          class="flex flex-col gap-1"
        >
          <div
            class="size-16 rounded-md border border-default"
            :class="stop.class"
          />
          <span class="stat-label">{{ stop.name }}</span>
          <span class="font-mono text-[10px] text-dimmed">{{ stop.hex }}</span>
        </div>
      </div>
    </SectionCard>

    <SectionCard
      :level="2"
      title="Rose gold — the brand accent"
      subtitle="Brand, interaction — and, since #1096, the good end of a measurement. Still never a generic surface tint: an accent on every panel stops being an accent."
    >
      <div class="flex flex-wrap gap-2">
        <div
          v-for="stop in ROSEGOLD_RAMP"
          :key="stop.name"
          class="flex flex-col gap-1"
        >
          <div
            class="size-16 rounded-md border border-default"
            :class="stop.class"
          />
          <span class="stat-label">{{ stop.name }}</span>
          <span class="font-mono text-[10px] text-dimmed">{{ stop.hex }}</span>
        </div>
      </div>
    </SectionCard>

    <SectionCard
      :level="2"
      title="Data axis"
      subtitle="One-sided at the bottom: rose gold marks what is above average, everything below simply steps down the neutral ramp. A losing value is not flagged in a warning colour, it is just not highlighted — so the bad end is deliberately quieter than the average one. The top carries one rarer step above it."
    >
      <div class="flex flex-col gap-6">
        <div class="flex flex-wrap gap-4">
          <div
            v-for="stop in DATA_AXIS"
            :key="stop.name"
            class="flex flex-col gap-1"
          >
            <div
              class="h-16 w-40 rounded-md border border-default"
              :class="stop.class"
            />
            <span class="stat-label">{{ stop.name }}</span>
            <span class="text-xs text-muted">{{ stop.use }}</span>
          </div>
        </div>

        <!--
          Shown as a number rather than a swatch: `--color-gold` is the one step
          of the axis that is text and small marks only.
        -->
        <div class="flex flex-col gap-1 border-t border-default pt-4">
          <div class="flex items-baseline gap-3">
            <span
              class="stat-value text-3xl"
              :class="DATA_STANDOUT.class"
            >Perfect</span>
            <span
              class="stat-value text-3xl"
              :class="DATA_STANDOUT.class"
            >82</span>
            <UIcon
              name="i-lucide-crown"
              class="size-5 text-gold"
            />
          </div>
          <span class="stat-label">{{ DATA_STANDOUT.name }}</span>
          <span class="text-xs text-muted">{{ DATA_STANDOUT.use }}</span>
          <span class="font-mono text-[10px] text-dimmed">{{ DATA_STANDOUT.hex }}</span>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Stat vocabulary ──────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Stat and damage-type vocabulary"
      subtitle="Riot's in-client colours for stats, damage types and keywords, emitted by the tooltip parser's tag-class map. A vocabulary, not a scale — text emphasis inside tooltip prose only, never a fill, never on a measurement. Shown as words for that reason."
    >
      <div class="flex flex-col gap-5">
        <div
          v-for="family in STAT_FAMILIES"
          :key="family.family"
          class="flex flex-col gap-2"
        >
          <span class="stat-label">{{ family.family }}</span>
          <div class="flex flex-wrap gap-x-6 gap-y-3">
            <div
              v-for="tone in family.tones"
              :key="tone.name"
              class="flex flex-col gap-0.5"
            >
              <span
                class="text-sm font-semibold"
                :class="tone.class"
              >{{ tone.use }}</span>
              <span class="stat-label">{{ tone.name }}</span>
              <span class="font-mono text-[10px] text-dimmed">{{ tone.hex }}</span>
            </div>
          </div>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Rune trees ───────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Rune trees"
      subtitle="Riot's five trees in their own colours, for prose that names runes (the champion page's build paragraph). A vocabulary, not a scale — text emphasis only, never a fill, never a measurement. The same exception the rank colours earn: a player reads the tree off the colour before the word."
    >
      <div class="flex flex-wrap gap-x-6 gap-y-3">
        <div
          v-for="tone in RUNE_TONES"
          :key="tone.name"
          class="flex flex-col gap-0.5"
        >
          <span
            class="text-sm font-medium"
            :class="tone.class"
          >{{ tone.tree }}</span>
          <span class="stat-label">{{ tone.name }}</span>
          <span class="text-xs text-muted">{{ tone.hex }}</span>
        </div>
      </div>
    </SectionCard>
  </div>
</template>
