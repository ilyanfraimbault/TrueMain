<script setup lang="ts">
import type { ChampionPowerspikeEvent } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'

definePageMeta({ layout: 'default' })

useSeoMeta({
  title: 'Charts playground · TrueMain',
  description: 'Visual review of the ChartsLineChart wrapper with mock data.',
})

// ─── LP history (single-series, emerald) ───────────────────────────────────
interface LpPoint extends Record<string, unknown> {
  ts: string
  lp: number
}

const lpHistory: LpPoint[] = [
  { ts: '2026-05-10', lp: 1420 },
  { ts: '2026-05-12', lp: 1443 },
  { ts: '2026-05-13', lp: 1421 },
  { ts: '2026-05-15', lp: 1465 },
  { ts: '2026-05-17', lp: 1488 },
  { ts: '2026-05-18', lp: 1472 },
  { ts: '2026-05-20', lp: 1510 },
  { ts: '2026-05-22', lp: 1534 },
  { ts: '2026-05-24', lp: 1521 },
  { ts: '2026-05-26', lp: 1556 },
]

const lpCategories = {
  lp: { name: 'LP', color: '#e58f83' },
}

const lpXFormatter = (tick: number): string => {
  const point = lpHistory[tick]
  if (!point) return ''
  return new Date(point.ts).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
  })
}

// ─── Champion winrate by patch (multi-series) ──────────────────────────────
interface WinratePoint extends Record<string, unknown> {
  patch: string
  yone: number
  ahri: number
  zed: number
}

const winrates: WinratePoint[] = [
  { patch: '14.20', yone: 0.51, ahri: 0.49, zed: 0.50 },
  { patch: '14.21', yone: 0.49, ahri: 0.50, zed: 0.51 },
  { patch: '14.22', yone: 0.50, ahri: 0.52, zed: 0.50 },
  { patch: '14.23', yone: 0.52, ahri: 0.51, zed: 0.48 },
  { patch: '14.24', yone: 0.50, ahri: 0.53, zed: 0.49 },
  { patch: '15.1', yone: 0.48, ahri: 0.51, zed: 0.50 },
  { patch: '15.2', yone: 0.49, ahri: 0.50, zed: 0.52 },
]

const winrateCategories = {
  yone: { name: 'Yone', color: '#e58f83' },
  ahri: { name: 'Ahri', color: '#f59e0b' },
  zed: { name: 'Zed',  color: '#71717a' },
}

const winrateXFormatter = (tick: number): string =>
  winrates[tick]?.patch ?? ''

const winrateYFormatter = (tick: number): string =>
  `${(tick * 100).toFixed(0)}%`

// ─── Champion power spikes (item bars) ─────────────────────────────────────
// Mock of GET /champions/{id}/powerspikes events, endpoint order (magnitude
// desc). Icons come off live DDragon so the strip renders realistically.
const DD_ITEM = 'https://ddragon.leagueoflegends.com/cdn/15.10.1/img/item'
const spikeItemsMap: Record<number, StaticItemData> = {
  3078: { id: 3078, name: 'Trinity Force', iconUrl: `${DD_ITEM}/3078.png`, totalGold: 3333 },
  3074: { id: 3074, name: 'Ravenous Hydra', iconUrl: `${DD_ITEM}/3074.png`, totalGold: 3300 },
  3053: { id: 3053, name: 'Sterak\'s Gage', iconUrl: `${DD_ITEM}/3053.png`, totalGold: 3200 },
  3071: { id: 3071, name: 'Black Cleaver', iconUrl: `${DD_ITEM}/3071.png`, totalGold: 3000 },
  3006: { id: 3006, name: 'Berserker\'s Greaves', iconUrl: `${DD_ITEM}/3006.png`, totalGold: 1100 },
  3026: { id: 3026, name: 'Guardian Angel', iconUrl: `${DD_ITEM}/3026.png`, totalGold: 3200 },
}
const spikeEvents: ChampionPowerspikeEvent[] = [
  { type: 'item', refId: 3078, avgMinute: 14.3, spikeMagnitude: 0.082, games: 412 },
  { type: 'item', refId: 3074, avgMinute: 21.6, spikeMagnitude: 0.064, games: 355 },
  { type: 'level', refId: 6, avgMinute: 8.1, spikeMagnitude: 0.051, games: 470 },
  { type: 'item', refId: 3053, avgMinute: 27.4, spikeMagnitude: 0.037, games: 198 },
  { type: 'item', refId: 3006, avgMinute: 9.8, spikeMagnitude: 0.021, games: 445 },
  { type: 'item', refId: 3071, avgMinute: 24.9, spikeMagnitude: -0.018, games: 176 },
  { type: 'item', refId: 3026, avgMinute: 31.2, spikeMagnitude: -0.041, games: 88 },
]
</script>

<template>
  <main class="mx-auto flex w-full max-w-5xl flex-col gap-8 p-4 md:p-6">
    <header class="flex flex-col gap-1">
      <p class="text-xs font-semibold uppercase tracking-wide text-muted">
        Dev playground
      </p>
      <h1 class="text-2xl font-semibold">
        Charts wrapper
      </h1>
      <p class="text-sm text-muted">
        Exercises <code>ChartsLineChart</code> with mock data. Real consumers
        (LP history on the account profile, champion winrate by patch) land
        in follow-up issues once the matching API endpoints exist.
      </p>
    </header>

    <section class="flex flex-col gap-3">
      <h2 class="text-sm font-semibold">
        LP history (single series)
      </h2>
      <div class="rounded-lg border border-default bg-elevated p-4">
        <ChartsLineChart
          :data="lpHistory"
          :categories="lpCategories"
          :height="240"
          :x-formatter="lpXFormatter"
          y-label="LP"
        />
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h2 class="text-sm font-semibold">
        Champion winrate by patch (multi-series)
      </h2>
      <div class="rounded-lg border border-default bg-elevated p-4">
        <ChartsLineChart
          :data="winrates"
          :categories="winrateCategories"
          :height="260"
          :x-formatter="winrateXFormatter"
          :y-formatter="winrateYFormatter"
        />
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h2 class="text-sm font-semibold">
        Champion power spikes (item bars)
      </h2>
      <ChampionPowerspikesChart
        :events="spikeEvents"
        :items-map="spikeItemsMap"
      />
    </section>

    <section class="flex flex-col gap-3">
      <h2 class="text-sm font-semibold">
        Empty state
      </h2>
      <div class="rounded-lg border border-default bg-elevated p-4">
        <ChartsLineChart
          :data="[]"
          :categories="lpCategories"
          :height="180"
          empty-message="No matches yet — play a ranked game to seed LP history."
        />
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h2 class="text-sm font-semibold">
        Loading state
      </h2>
      <div class="rounded-lg border border-default bg-elevated p-4">
        <ChartsLineChart
          :data="[]"
          :categories="lpCategories"
          :height="180"
          :loading="true"
        />
      </div>
    </section>
  </main>
</template>
