<script setup lang="ts">
// Design-system playground (`pages/dev/design-system.vue`): typography, the stat
// primitives, the tier ladder and the list rows.

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
  <div class="flex flex-col gap-10">
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
      subtitle="`ListRowSurface`, the material of the compact leaderboard row (champion-page sidebar)."
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
  </div>
</template>
