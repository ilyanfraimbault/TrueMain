<script setup lang="ts">
import { THEME_HEX, type ThemeColor } from '~~/shared/utils/theme-tokens'

// A swatch's caption: the token its class paints with, from the generated tokens.
const hexOf = (cls: string) => THEME_HEX[cls.replace(/^(bg|text)-/, '') as ThemeColor]

// Design-system playground (`pages/dev/design-system.vue`): the colour ramps, the data
// axis and the two text-only vocabularies (stats, rune trees).

// Written as literal class strings rather than built from a loop: Tailwind's
// scanner only emits utilities it can see statically, and an interpolated
// `bg-ink-${shade}` would render as an unstyled swatch — the exact failure this
// page exists to catch.
const INK_RAMP = [
  { name: '50', class: 'bg-ink-50' },
  { name: '100', class: 'bg-ink-100' },
  { name: '200', class: 'bg-ink-200' },
  { name: '300', class: 'bg-ink-300' },
  { name: '400', class: 'bg-ink-400' },
  { name: '500', class: 'bg-ink-500' },
  { name: '600', class: 'bg-ink-600' },
  { name: '700', class: 'bg-ink-700' },
  { name: '800', class: 'bg-ink-800' },
  { name: '900', class: 'bg-ink-900' },
  { name: '950', class: 'bg-ink-950' },
]

const ROSEGOLD_RAMP = [
  { name: '200', class: 'bg-rosegold-200' },
  { name: '300', class: 'bg-rosegold-300' },
  { name: '400', class: 'bg-rosegold-400' },
  { name: '500', class: 'bg-rosegold-500' },
  { name: '600', class: 'bg-rosegold-600' },
  { name: '700', class: 'bg-rosegold-700' },
]

// The captions' hexes come from the generated tokens, the swatches from the CSS vars.
const DATA_AXIS = [
  { name: 'data-good', class: 'bg-data-good', use: 'Above average, win — rosegold-400' },
  { name: 'data-good-dim', class: 'bg-data-good-dim', use: 'Large fills on the good side' },
  { name: 'data-mid', class: 'bg-data-mid', use: 'Average, no signal — ink-400' },
  { name: 'data-bad-dim', class: 'bg-data-bad-dim', use: 'Large fills below average' },
  { name: 'data-bad', class: 'bg-data-bad', use: 'Below average, loss — ink-500' },
]

// The top of the data axis, kept out of `DATA_AXIS` because it is the one step
// that is never a fill: `--color-gold` is text and small marks only, so a
// swatch here would invite the first `bg-gold` on a measurement.
const DATA_STANDOUT = {
  name: 'gold',
  class: 'text-gold',
  use: 'Standout — a Perfect KDA, a 75+ performance score. The same token the MVP crown wears.',
}

// Text emphasis only, like `--color-stat-*` — so they are shown as *words*,
// which is the only way they are ever allowed to appear. A swatch here would
// invite the first `bg-rune-domination` in the app.
const RUNE_TONES = [
  { name: 'rune-precision', class: 'text-rune-precision', tree: 'Precision' },
  { name: 'rune-domination', class: 'text-rune-domination', tree: 'Domination' },
  { name: 'rune-sorcery', class: 'text-rune-sorcery', tree: 'Sorcery' },
  { name: 'rune-resolve', class: 'text-rune-resolve', tree: 'Resolve' },
  { name: 'rune-inspiration', class: 'text-rune-inspiration', tree: 'Inspiration' },
]

// Riot's in-client vocabulary for stats, damage types and keywords, rendered by
// the tooltip parser's tag-class map. Words, never swatches: these are the
// tokens most likely to be mistaken for a scale, and the page should not be the
// first place someone sees one as a fill. Grouped the way `theme.css` groups
// them so the two read side by side.
// Each tone as [class, use]: the class stays a literal for Tailwind's scanner.
const STAT_FAMILIES = ([
  ['Defensive / sustain', [
    ['text-stat-health', 'Flat HP / HP regen'],
    ['text-stat-armor', 'Armor'],
    ['text-stat-mr', 'Magic resist'],
    ['text-stat-tenacity', 'Tenacity / slow resist'],
    ['text-stat-hsp', 'Heal & shield power'],
    ['text-stat-heal-reduction', 'Grievous wounds'],
  ]],
  ['Resources / utility', [
    ['text-stat-mana', 'Mana / mana regen'],
    ['text-stat-haste', 'Ability haste'],
    ['text-stat-speed', 'Move speed / on-hit'],
  ]],
  ['Offensive — physical', [
    ['text-stat-ad', 'Attack damage'],
    ['text-stat-lethality', 'Lethality / armor pen'],
    ['text-stat-crit', 'Crit chance / crit damage'],
    ['text-stat-as', 'Attack speed'],
    ['text-stat-vamp', 'Lifesteal / omnivamp'],
  ]],
  ['Offensive — magical', [
    ['text-stat-ap', 'Ability power'],
    ['text-stat-magicpen', 'Magic penetration'],
  ]],
  ['Damage types', [
    ['text-stat-true', 'True damage'],
    ['text-stat-adaptive', 'Adaptive damage (verified)'],
  ]],
  ['Misc / structural', [
    ['text-stat-status', 'CC keywords — Stun, Airborne'],
    ['text-stat-stealth', 'Stealth keywords'],
    ['text-stat-shield', 'Shield value'],
    ['text-stat-gold', 'Gold per X seconds'],
    ['text-stat-passive', 'Item passive label'],
    ['text-stat-active', 'Item active label'],
  ]],
] as const).map(([family, tones]) => ({
  family,
  tones: tones.map(([cls, use]) => ({ name: cls.replace(/^text-/, ''), class: cls, use })),
}))
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
          <span class="font-mono text-[10px] text-dimmed">{{ hexOf(stop.class) }}</span>
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
          <span class="font-mono text-[10px] text-dimmed">{{ hexOf(stop.class) }}</span>
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
          <span class="font-mono text-[10px] text-dimmed">{{ hexOf(DATA_STANDOUT.class) }}</span>
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
              <span class="font-mono text-[10px] text-dimmed">{{ hexOf(tone.class) }}</span>
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
          <span class="text-xs text-muted">{{ hexOf(tone.class) }}</span>
        </div>
      </div>
    </SectionCard>
  </div>
</template>
