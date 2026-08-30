<script setup lang="ts">
/**
 * Design-system review page — the project's stand-in for a component gallery.
 *
 * There is no Storybook here and `web/vitest.config.ts` deliberately omits
 * `@vitejs/plugin-vue`, so no test in this app can mount an SFC and no snapshot
 * can catch a token regression. This page is the compensating control: it puts
 * every foundation on one screen so a palette or elevation change can be judged
 * in a single screenshot instead of by touring the real pages and hoping the
 * affected state was on one of them.
 *
 * Like the other `pages/dev/*` playgrounds it is stripped from production
 * builds by the `pages:extend` hook in `nuxt.config.ts`.
 */

definePageMeta({ layout: 'default' })

useSeoMeta({
  title: 'Design system playground',
  description: 'Isolated visual review of the app’s colour, elevation and type foundations.',
})

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
// colour beside it. Keep them in step with `--color-data-*` in main.css.
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
// first place someone sees one as a fill. Grouped the way `main.css` groups
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

// The brand mark stands in for perk artwork: this page fetches nothing, and the
// treatment needs a *coloured* subject or `grayscale` has nothing to show.
const PERK_STATES = [
  { label: 'selected-perk', class: 'selected-perk' },
  { label: 'deselected', class: 'deselected' },
  { label: 'deselected', class: 'deselected' },
]

const ELEVATION = [
  { token: '--ui-bg', util: 'bg-default', hex: '#0b0b0d', use: 'Page' },
  { token: '--ui-bg-muted', util: 'bg-muted', hex: '#131317', use: 'Recessed — inset strips, empty states' },
  { token: '--ui-bg-elevated', util: 'bg-elevated', hex: '#1b1b20', use: 'Raised — cards, rows' },
  { token: '--ui-bg-accented', util: 'bg-accented', hex: '#24242a', use: 'Interactive — hover, nested' },
]

const TIERS = ['S', 'A', 'B', 'C', 'D']

// `max: 0.09` on the pick rates: a real champion directory's pick rates all sit
// in a narrow band near the bottom of 0..1, so the column is normalised against
// its own peak. Presence keeps `max: 1` to show the difference.
const METRIC_BARS = [
  { label: 'Pick rate', value: 0.082, max: 0.09, tone: 'neutral' as const, display: '8.2%' },
  { label: 'Pick rate', value: 0.031, max: 0.09, tone: 'neutral' as const, display: '3.1%' },
  { label: 'Pick rate', value: 0.004, max: 0.09, tone: 'neutral' as const, display: '0.4%' },
  { label: 'Win rate', value: 0.542, max: 1, tone: 'good' as const, display: '54.2%' },
  { label: 'Win rate', value: 0.478, max: 1, tone: 'bad' as const, display: '47.8%' },
]

const TEXT_TOKENS = [
  { util: 'text-highlighted', use: 'The value the user came for' },
  { util: 'text-default', use: 'Primary content' },
  { util: 'text-muted', use: 'Secondary content' },
  { util: 'text-dimmed', use: 'Tertiary / disabled' },
]
</script>

<template>
  <UContainer class="flex flex-col gap-10 py-10">
    <PageHeader
      eyebrow="Playground"
      title="Design system"
      description="Every foundation on one screen. Judge a token change here before touring the real pages."
    />

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

    <!-- ─── Elevation ────────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Elevation ladder"
      subtitle="Four opaque steps. Nested here on purpose — if two steps read as one, the nesting below collapses visually and the ladder needs re-spacing."
    >
      <div class="flex flex-col gap-4">
        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div
            v-for="step in ELEVATION"
            :key="step.token"
            class="flex flex-col gap-2 rounded-lg border border-default p-3"
            :class="step.util"
          >
            <span class="stat-label">{{ step.util }}</span>
            <span class="font-mono text-xs text-muted">{{ step.hex }}</span>
            <span class="text-xs text-dimmed">{{ step.use }}</span>
          </div>
        </div>

        <!-- The real test: the steps stacked, not side by side. -->
        <div class="rounded-lg bg-default p-4">
          <span class="stat-label">bg-default</span>
          <div class="mt-2 rounded-lg bg-muted p-4">
            <span class="stat-label">bg-muted</span>
            <div class="mt-2 rounded-lg bg-elevated p-4">
              <span class="stat-label">bg-elevated</span>
              <div class="mt-2 rounded-lg bg-accented p-4">
                <span class="stat-label">bg-accented</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Materials ────────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Materials"
      subtitle="`surface` is the app-wide panel; `surface-hover` steps it up the ladder on hover. There is no translucent material — the former `glass` was removed once nothing used it."
    >
      <div class="grid gap-3 sm:grid-cols-2">
        <div class="surface rounded-xl p-4">
          <p class="text-sm font-medium text-default">
            surface
          </p>
          <p class="mt-1 text-xs text-muted">
            Opaque fill, neutral hairline, one soft shadow.
          </p>
        </div>
        <div class="surface surface-hover cursor-pointer rounded-xl p-4">
          <p class="text-sm font-medium text-default">
            surface + surface-hover
          </p>
          <p class="mt-1 text-xs text-muted">
            Hover me — the fill moves up one step.
          </p>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Selection state ──────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Picked vs. not picked"
      subtitle="`deselected` and `selected-perk`, the pair behind every rune panel. The artwork alone is not a reliable signal — a selected grey rune reads exactly like its unpicked neighbours — so the faint primary ring carries the state and the siblings fade out of the way. Shown on the brand mark because this page fetches nothing; the treatment is the same."
    >
      <div class="flex flex-wrap items-start gap-6">
        <div
          v-for="(state, index) in PERK_STATES"
          :key="`${state.label}-${index}`"
          class="flex flex-col items-center gap-1"
        >
          <img
            src="/brand/truemain-mark.svg"
            alt=""
            width="40"
            height="40"
            class="size-10 rounded-full transition"
            :class="state.class"
          >
          <span class="stat-label">{{ state.label }}</span>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Typography ───────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Typography"
      subtitle="Inter carries everything the reader reads, measurements included (#1111). Geist Mono is kept only where monospace is the meaning rather than a flourish. A stat is always a pair, and the gap between the two registers is the point — size, weight, casing and tracking, not family."
    >
      <div class="flex flex-col gap-6">
        <div class="flex flex-col gap-2">
          <span class="stat-label">Display &amp; headings — Inter</span>
          <p class="text-4xl font-semibold tracking-tight text-highlighted sm:text-5xl">
            Real builds from <span class="text-primary">real mains</span>.
          </p>
          <p class="text-2xl font-semibold tracking-tight text-highlighted">
            Page title — text-2xl
          </p>
          <p class="text-sm text-muted">
            Body copy — text-sm text-muted. Builds, runes and skill orders from the players who
            truly mastered your champion.
          </p>
        </div>

        <div class="flex flex-col gap-2">
          <span class="stat-label">Measurements — Inter, tabular-nums</span>
          <div class="flex flex-wrap items-end gap-8">
            <div class="flex flex-col items-start gap-1">
              <span class="stat-value text-3xl">54.2%</span>
              <span class="stat-label">Win rate</span>
            </div>
            <div class="flex flex-col items-start gap-1">
              <span class="stat-value text-xl">2.4</span>
              <span class="stat-label">KDA</span>
            </div>
            <div class="flex flex-col items-start gap-1">
              <span class="stat-value text-base">155 974</span>
              <span class="stat-label">Games</span>
            </div>
            <div class="flex flex-col items-start gap-1">
              <span class="stat-value text-base text-data-good">+3.1</span>
              <span class="stat-label">Delta</span>
            </div>
            <div class="flex flex-col items-start gap-1">
              <span class="stat-value text-base text-data-bad">−1.8</span>
              <span class="stat-label">Delta</span>
            </div>
          </div>
        </div>

        <!--
          What is left of Geist Mono after #1111: glyphs where the monospace
          *is* the meaning. Listed so the next reader can tell "still used" from
          "forgotten".
        -->
        <div class="flex flex-col gap-2">
          <span class="stat-label">Where Geist Mono survives</span>
          <div class="flex flex-wrap items-center gap-8">
            <div class="flex flex-col items-start gap-1">
              <span class="font-mono text-xl font-bold text-highlighted">S A B C D</span>
              <span class="stat-label">Tier letters</span>
            </div>
            <div class="flex flex-col items-start gap-1">
              <span class="font-mono text-xl font-semibold text-dimmed">?</span>
              <span class="stat-label">Empty-slot glyph (builder)</span>
            </div>
            <div class="flex flex-col items-start gap-1">
              <span class="font-mono text-xl text-muted">#e58f83</span>
              <span class="stat-label">Hex codes on this page</span>
            </div>
          </div>
        </div>

        <div class="flex flex-col gap-1">
          <span class="stat-label">Semantic text tokens</span>
          <p
            v-for="token in TEXT_TOKENS"
            :key="token.util"
            class="text-sm"
            :class="token.util"
          >
            {{ token.util }} — {{ token.use }}
          </p>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Stat primitives ──────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="StatBlock"
      subtitle="The value/label pair, at every scale and every tone. `default` is “no better/worse reading exists”; `mid` is “measured, and it is average”."
    >
      <div class="flex flex-col gap-6">
        <div class="flex flex-wrap items-end gap-8">
          <StatBlock value="54.2%" label="Win rate" size="xl" tone="good" />
          <StatBlock value="47.8%" label="Win rate" size="lg" tone="bad" />
          <StatBlock value="50.1%" label="Win rate" size="md" tone="mid" />
          <StatBlock value="155 974" label="Games" size="md" />
          <StatBlock value="2.4" label="KDA" size="sm" />
        </div>
        <div class="flex flex-wrap items-start gap-8">
          <StatBlock
            value="100"
            label="Games used"
            caption="65 by mains · of 5,000 scanned"
          />
          <StatBlock
            value="—"
            label="Gold @15"
            caption="nothing decided yet"
          />
          <StatBlock
            value="+262"
            label="Gold @15"
            tone="good"
            caption="avg over 204 games"
            align="end"
          />
        </div>
      </div>
    </SectionCard>

    <SectionCard
      :level="2"
      title="MetricBar"
      subtitle="A rate as a length. `max` normalises a column against its own peak — a 6% pick rate against a 100% track is a rounding error you cannot see."
    >
      <div class="flex flex-col gap-4">
        <!-- Keyed on the pair, not the label: three of these are "Pick rate". -->
        <div
          v-for="bar in METRIC_BARS"
          :key="`${bar.label}-${bar.display}`"
          class="flex items-center gap-3"
        >
          <span class="stat-label w-28 shrink-0">{{ bar.label }}</span>
          <span class="w-40 shrink-0">
            <MetricBar
              :value="bar.value"
              :max="bar.max"
              :tone="bar.tone"
              :label="`${bar.label} ${bar.value}`"
            />
          </span>
          <span class="stat-value text-sm">{{ bar.display }}</span>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Tier ladder ──────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Tier ladder"
      subtitle="The medal scale — rose gold, gold, silver, bronze, iron. Five ranks read as five ranks without a legend. #1060 briefly replaced it with a teal→amber ladder; with the warm end of the axis withdrawn (#1096) the collision that motivated it is gone."
    >
      <div class="flex flex-wrap items-center gap-6">
        <div
          v-for="tier in TIERS"
          :key="tier"
          class="flex flex-col items-center gap-1"
        >
          <TierBadge :tier="tier" />
          <span class="stat-label">{{ tier }}</span>
        </div>
        <div class="flex flex-col items-center gap-1">
          <TierBadge tier="" />
          <span class="stat-label">Unknown</span>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Rows ─────────────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="List row"
      subtitle="`ListRowSurface`, the shared material behind the champion directory and the truemain leaderboard."
    >
      <div class="flex flex-col gap-2">
        <ListRowSurface
          v-for="row in [
            { name: 'Viego', tier: 'S', wr: '52.9%', pr: '6.7%' },
            { name: 'Thresh', tier: 'A', wr: '54.2%', pr: '7.5%' },
            { name: 'Yasuo', tier: 'C', wr: '49.1%', pr: '4.2%' },
          ]"
          :key="row.name"
          class="gap-4"
        >
          <TierBadge :tier="row.tier" />
          <span class="flex-1 text-sm font-medium text-highlighted">{{ row.name }}</span>
          <div class="flex flex-col items-end">
            <span class="stat-value text-sm">{{ row.wr }}</span>
            <span class="stat-label">WR</span>
          </div>
          <div class="flex flex-col items-end">
            <span class="stat-value text-sm">{{ row.pr }}</span>
            <span class="stat-label">PR</span>
          </div>
        </ListRowSurface>
      </div>
    </SectionCard>
  </UContainer>
</template>
