<script setup lang="ts">
// Champions panel — per-champion games/mains/otps from
// `GET /api/ops/stats/champions`, filterable by region/patch/position.
// Ranked solo/duo (queue 420) only — the store keeps nothing else since #680,
// so the page never sends `queue` (the backend still accepts it).
// A sortable table plus two bar charts (top-N by games, top-N by mains).
//
// IMPORTANT data caveat surfaced in the UI: `games` honors every filter, but
// `mains`/`otps`/`extendedSamples` honor the `region` filter ONLY — they ignore
// patch/position. The table header and a note make this explicit.
//
// `extendedSamples` has no column of its own (#1442). It counts the mains that only
// cleared the *relaxed* per-champion play-rate threshold — a tuning diagnostic of the
// coverage relaxation, not a property of the champion — and on production that is 111
// rows out of 68k mains, at most 5 on any one champion and 0 on 123 of the 173. As a
// sortable column it was a column of zeros; it rides under the Mains figure it
// qualifies instead, and only when there is something to say.
import type { TableColumn } from '@nuxt/ui'
import type { ChampionStatsRow } from '~~/shared/types/ops'
import { ALL_POSITIONS_ICON_URL, POSITION_OPTIONS } from '~/utils/positions'
import { formatNumber } from '~~/shared/utils/format'

interface ChampionRowView extends ChampionStatsRow {
  name: string
  iconUrl: string | null
}

// --- Filters -----------------------------------------------------------------
// Store keeps queue 420 (ranked solo/duo) only since #680 — every other queue
// id returns an empty page, so the page no longer offers a queue filter and
// never sends `queue` at all (the backend still accepts it).
const region = ref<string>(ALL)
const patch = ref<string>('')
const position = ref<string>(ALL)

const filters = computed(() => ({
  region: region.value === ALL ? undefined : region.value,
  patch: patch.value.trim() || undefined,
  position: position.value === ALL ? undefined : position.value,
}))

const hasActiveFilters = computed(() =>
  Boolean(
    region.value !== ALL
    || patch.value.trim()
    || position.value !== ALL,
  ),
)
function resetFilters() {
  region.value = ALL
  patch.value = ''
  position.value = ALL
}

const { data, pending, error, refresh } = useChampionStats(filters)
const { nameFor, iconFor, pending: staticPending } = useChampionStatic()

// Join the ops rows with DDragon names/icons.
const rows = computed<ChampionRowView[]>(() =>
  (data.value ?? []).map(row => ({
    ...row,
    name: nameFor(row.championId),
    iconUrl: iconFor(row.championId),
  })),
)

// Client-side champion-name search. Scoped to the TABLE only — the charts keep
// showing the full top-N so they stay a stable overview while you look one up.
const search = ref('')
const tableRows = computed<ChampionRowView[]>(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return rows.value
  return rows.value.filter(r => r.name.toLowerCase().includes(q))
})

const loading = computed(() => pending.value || staticPending.value)

// --- Table -------------------------------------------------------------------
const sorting = ref([{ id: 'games', desc: true }])

const columns: TableColumn<ChampionRowView>[] = [
  {
    accessorKey: 'name',
    header: ({ column }) => sortableHeader(column, 'Champion'),
  },
  {
    accessorKey: 'games',
    header: ({ column }) => sortableHeader(column, 'Games', 'right'),
  },
  {
    accessorKey: 'mains',
    header: ({ column }) => sortableHeader(column, 'Mains', 'right'),
  },
  {
    accessorKey: 'otps',
    header: ({ column }) => sortableHeader(column, 'OTPs', 'right'),
  },
]

// --- Charts: top-N by games and by mains -------------------------------------
const TOP_N = 12

const topByGames = computed(() =>
  [...rows.value]
    .sort((a, b) => b.games - a.games)
    .slice(0, TOP_N)
    .map(r => ({ label: r.name, games: r.games })),
)
const topByMains = computed(() =>
  [...rows.value]
    .filter(r => r.mains > 0)
    .sort((a, b) => b.mains - a.mains)
    .slice(0, TOP_N)
    .map(r => ({ label: r.name, mains: r.mains })),
)

// Charts grow with the number of bars; the skeletons mirror it to avoid CLS.
const topByGamesChartHeight = computed(() =>
  barChartHeight(topByGames.value.length, { min: 260, step: 28 }),
)
const topByMainsChartHeight = computed(() =>
  barChartHeight(topByMains.value.length, { min: 260, step: 28 }),
)

const gamesCategories = { games: { name: 'Games', color: CHART_PRIMARY } }
// amber-400 for the secondary metric so the two charts read as distinct series.
const mainsCategories = { mains: { name: 'Mains', color: CHART_ACCENT_AMBER } }

// Horizontal bars: the champion name lives on the LEFT (category) axis, looked
// up by bar index, so these feed `:y-formatter` (not `:x-formatter`).
const gamesLabelFormatter = computed(() =>
  indexLabelFormatter(topByGames.value, r => r.label),
)
const mainsLabelFormatter = computed(() =>
  indexLabelFormatter(topByMains.value, r => r.label),
)
</script>

<template>
  <UDashboardPanel id="champions">
    <template #header>
      <UDashboardNavbar title="Champions" icon="i-lucide-swords">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <USelect
            v-model="region"
            :items="REGION_ITEMS"
            icon="i-lucide-globe"
            placeholder="Region"
            class="w-40"
          />
          <UFieldGroup size="md" class="rounded-md bg-default ring ring-inset ring-accented">
            <UTooltip text="All positions">
              <UButton
                :variant="position === ALL ? 'soft' : 'ghost'"
                color="neutral"
                square
                aria-label="All positions"
                @click="position = ALL"
              >
                <NuxtImg :src="ALL_POSITIONS_ICON_URL" alt="All positions" width="18" height="18" class="size-[18px]" />
              </UButton>
            </UTooltip>
            <UTooltip v-for="option in POSITION_OPTIONS" :key="option.value" :text="option.label">
              <UButton
                :variant="position === option.value ? 'soft' : 'ghost'"
                color="neutral"
                square
                :aria-label="option.label"
                @click="position = option.value"
              >
                <NuxtImg :src="option.iconUrl" :alt="option.label" width="18" height="18" class="size-[18px]" />
              </UButton>
            </UTooltip>
          </UFieldGroup>
          <UInput
            v-model="patch"
            icon="i-lucide-git-branch"
            placeholder="Patch e.g. 16.4"
            class="w-44"
          />
        </template>
        <template #right>
          <UButton
            v-if="hasActiveFilters"
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            label="Clear"
            @click="resetFilters"
          />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load champion stats"
        class="mb-6"
      />

      <!-- Charts -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-6 mb-6">
        <UCard :ui="{ root: 'overflow-visible' }">
          <template #header>
            <PanelTitle variant="label" :title="`Top ${TOP_N} by games`" />
          </template>
          <USkeleton v-if="loading" class="h-[240px] w-full" />
          <div
            v-else-if="topByGames.length === 0"
            class="h-[240px] flex items-center justify-center text-sm text-muted"
          >
            No champion games for these filters.
          </div>
          <ChartsBarChart
            v-else
            :data="topByGames"
            :height="topByGamesChartHeight"
            :categories="gamesCategories"
            :y-axis="['games']"
            :y-num-ticks="topByGames.length"
            :x-formatter="formatCount"
            :y-formatter="gamesLabelFormatter"
            :tooltip-title-formatter="labelTooltipTitle"
            v-bind="horizontalBarProps(120)"
          />
        </UCard>

        <UCard :ui="{ root: 'overflow-visible' }">
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <PanelTitle variant="label" :title="`Top ${TOP_N} by mains`" />
              <UBadge
                color="neutral"
                variant="subtle"
                size="sm"
                label="region-scoped"
              />
            </div>
          </template>
          <USkeleton v-if="loading" class="h-[240px] w-full" />
          <div
            v-else-if="topByMains.length === 0"
            class="h-[240px] flex items-center justify-center text-sm text-muted"
          >
            No mains for these filters.
          </div>
          <ChartsBarChart
            v-else
            :data="topByMains"
            :height="topByMainsChartHeight"
            :categories="mainsCategories"
            :y-axis="['mains']"
            :y-num-ticks="topByMains.length"
            :x-formatter="formatCount"
            :y-formatter="mainsLabelFormatter"
            :tooltip-title-formatter="labelTooltipTitle"
            v-bind="horizontalBarProps(120)"
          />
        </UCard>
      </div>

      <!-- Table -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex items-center justify-between gap-3">
            <PanelTitle title="Per-champion stats" subtitle="Ranked solo/duo only.">
              <template #info>
                <p>
                  <strong>Games</strong> honor every filter.
                </p>
                <p>
                  <strong>Mains / OTPs</strong> honor region only.
                </p>
                <p>
                  <strong>N relaxed</strong>, under a Mains figure, counts the mains that
                  only cleared the relaxed play-rate threshold the pipeline applies to
                  under-covered champions — the ones a champion would lose if the
                  relaxation went away.
                </p>
              </template>
            </PanelTitle>
            <div class="flex items-center gap-3">
              <UInput
                v-model="search"
                icon="i-lucide-search"
                placeholder="Search champion"
                class="w-48"
                :ui="{ trailing: 'pe-1' }"
              >
                <template v-if="search" #trailing>
                  <UButton
                    icon="i-lucide-x"
                    color="neutral"
                    variant="link"
                    size="sm"
                    aria-label="Clear search"
                    @click="void (search = '')"
                  />
                </template>
              </UInput>
              <UBadge
                v-if="!loading"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(tableRows.length)} champions`"
              />
            </div>
          </div>
        </template>

        <UTable
          v-model:sorting="sorting"
          :data="tableRows"
          :columns="columns"
          :loading="loading"
          loading-color="primary"
          :ui="{ td: 'py-2' }"
        >
          <template #name-cell="{ row }">
            <div class="flex items-center gap-2.5">
              <NuxtImg
                v-if="row.original.iconUrl"
                :src="row.original.iconUrl"
                :alt="row.original.name"
                width="28"
                height="28"
                loading="lazy"
                class="size-7 rounded-md ring-1 ring-default"
              />
              <div
                v-else
                class="size-7 rounded-md bg-elevated ring-1 ring-default"
              />
              <span class="font-medium text-highlighted">
                {{ row.original.name }}
              </span>
            </div>
          </template>
          <template #games-cell="{ row }">
            <div class="text-right tabular-nums">
              {{ formatNumber(row.original.games) }}
            </div>
          </template>
          <template #mains-cell="{ row }">
            <div class="text-right tabular-nums">
              {{ formatNumber(row.original.mains) }}
              <UTooltip
                v-if="row.original.extendedSamples > 0"
                :text="`${formatNumber(row.original.extendedSamples)} of these mains only cleared the relaxed play-rate threshold applied to under-covered champions (MainAnalysis:PlayRateFloor).`"
              >
                <span class="block text-xs font-normal text-dimmed">
                  {{ formatNumber(row.original.extendedSamples) }} relaxed
                </span>
              </UTooltip>
            </div>
          </template>
          <template #otps-cell="{ row }">
            <div class="text-right tabular-nums">
              {{ formatNumber(row.original.otps) }}
            </div>
          </template>

          <template #empty>
            <div class="py-10 text-center text-sm text-muted">
              No champions match these filters.
            </div>
          </template>
        </UTable>
      </UCard>
    </template>
  </UDashboardPanel>
</template>
