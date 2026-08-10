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

const DATA_AXIS = [
  { name: 'data-good', class: 'bg-data-good', hex: '#3ad6c4', use: 'Above average, win' },
  { name: 'data-good-dim', class: 'bg-data-good-dim', hex: '#7fc9c0', use: 'Large fills on the good side' },
  { name: 'data-mid', class: 'bg-data-mid', hex: '#8b8b95', use: 'Average, no signal' },
  { name: 'data-bad-dim', class: 'bg-data-bad-dim', hex: '#d9a45f', use: 'Large fills on the bad side' },
  { name: 'data-bad', class: 'bg-data-bad', hex: '#f0a13c', use: 'Below average, loss' },
]

const ELEVATION = [
  { token: '--ui-bg', util: 'bg-default', hex: '#0b0b0d', use: 'Page' },
  { token: '--ui-bg-muted', util: 'bg-muted', hex: '#131317', use: 'Recessed — inset strips, empty states' },
  { token: '--ui-bg-elevated', util: 'bg-elevated', hex: '#1b1b20', use: 'Raised — cards, rows' },
  { token: '--ui-bg-accented', util: 'bg-accented', hex: '#24242a', use: 'Interactive — hover, nested' },
]

const TIERS = ['S', 'A', 'B', 'C', 'D']

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
      subtitle="Brand and interaction only: logo, active nav, focus rings, primary buttons, links, selected states. Never a data value, never a generic surface tint."
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
      subtitle="Cold → warm rather than green → red: rose gold is itself a desaturated red, so a red loss value next to the accent is a coin flip to read. Teal and amber share no hue with the brand."
    >
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

    <!-- ─── Typography ───────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Typography"
      subtitle="Inter carries prose and headings; Geist Mono carries measurements. A stat is always a pair, and the gap between the two registers is the point."
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
          <span class="stat-label">Measurements — Geist Mono</span>
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

    <!-- ─── Tier ladder ──────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Tier ladder"
      subtitle="Rides the data axis so a champion’s tier and its win rate speak one language. Replaces a medal metaphor whose gold and bronze now read as warnings."
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
