<script setup lang="ts">
// Champions panel — per-champion games/mains/otps from
// `GET /api/ops/stats/champions`, filterable by region/patch/position/queue.
// A sortable table plus two bar charts (top-N by games, top-N by mains).
//
// IMPORTANT data caveat surfaced in the UI: `games` honors every filter, but
// `mains`/`otps`/`extendedSamples` honor the `region` filter ONLY — they ignore
// patch/position/queue. The table header and a note make this explicit.
import type { TableColumn } from '@nuxt/ui'
import type { ChampionStatsRow } from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

interface ChampionRowView extends ChampionStatsRow {
  name: string
  iconUrl: string | null
}

// --- Filters (empty string = no filter; the composable strips empties) -------
const region = ref<string>('')
const patch = ref<string>('')
const position = ref<string>('')
const queue = ref<string>('')

const regionItems = [
  { label: 'All regions', value: '' },
  { label: 'EUW1', value: 'EUW1' },
  { label: 'KR', value: 'KR' },
  { label: 'NA1', value: 'NA1' },
]
const positionItems = [
  { label: 'All positions', value: '' },
  { label: 'Top', value: 'TOP' },
  { label: 'Jungle', value: 'JUNGLE' },
  { label: 'Middle', value: 'MIDDLE' },
  { label: 'Bottom', value: 'BOTTOM' },
  { label: 'Support', value: 'UTILITY' },
]
// Common SR queues; "All" leaves the param off.
const queueItems = [
  { label: 'All queues', value: '' },
  { label: 'Ranked Solo (420)', value: '420' },
  { label: 'Ranked Flex (440)', value: '440' },
  { label: 'Normal Draft (400)', value: '400' },
  { label: 'Normal Blind (430)', value: '430' },
  { label: 'ARAM (450)', value: '450' },
]

const filters = computed(() => ({
  region: region.value || undefined,
  patch: patch.value.trim() || undefined,
  position: position.value || undefined,
  queue: queue.value ? Number(queue.value) : undefined,
}))

const hasActiveFilters = computed(() =>
  Boolean(region.value || patch.value.trim() || position.value || queue.value),
)
function resetFilters() {
  region.value = ''
  patch.value = ''
  position.value = ''
  queue.value = ''
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
  {
    accessorKey: 'extendedSamples',
    header: ({ column }) => sortableHeader(column, 'Ext. samples', 'right'),
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

const gamesCategories = { games: { name: 'Games', color: '#34d399' } }
// amber-400 for the secondary metric so the two charts read as distinct series.
const mainsCategories = { mains: { name: 'Mains', color: '#fbbf24' } }

const gamesXFormatter = computed(() =>
  indexLabelFormatter(topByGames.value, r => r.label),
)
const mainsXFormatter = computed(() =>
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
            :items="regionItems"
            icon="i-lucide-globe"
            placeholder="Region"
            class="w-40"
          />
          <USelect
            v-model="position"
            :items="positionItems"
            icon="i-lucide-map-pin"
            placeholder="Position"
            class="w-44"
          />
          <USelect
            v-model="queue"
            :items="queueItems"
            icon="i-lucide-list-filter"
            placeholder="Queue"
            class="w-52"
          />
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
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Failed to load champion stats"
        :description="error.message"
        class="mb-6"
      />

      <!-- Charts -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-6 mb-6">
        <UCard :ui="{ root: 'overflow-visible' }">
          <template #header>
            <p class="text-xs text-muted uppercase">
              Top {{ TOP_N }} by games
            </p>
          </template>
          <USkeleton v-if="loading" class="h-[240px] w-full" />
          <div
            v-else-if="topByGames.length === 0"
            class="h-[240px] flex items-center justify-center text-sm text-muted"
          >
            No champion games for these filters.
          </div>
          <ClientOnly v-else>
            <NcBarChart
              :data="topByGames"
              :height="240"
              :categories="gamesCategories"
              :y-axis="['games']"
              :x-num-ticks="topByGames.length"
              :x-formatter="gamesXFormatter"
              :y-formatter="formatCount"
              :radius="4"
              hide-legend
            />
            <template #fallback>
              <USkeleton class="h-[240px] w-full" />
            </template>
          </ClientOnly>
        </UCard>

        <UCard :ui="{ root: 'overflow-visible' }">
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <p class="text-xs text-muted uppercase">
                Top {{ TOP_N }} by mains
              </p>
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
          <ClientOnly v-else>
            <NcBarChart
              :data="topByMains"
              :height="240"
              :categories="mainsCategories"
              :y-axis="['mains']"
              :x-num-ticks="topByMains.length"
              :x-formatter="mainsXFormatter"
              :y-formatter="formatCount"
              :radius="4"
              hide-legend
            />
            <template #fallback>
              <USkeleton class="h-[240px] w-full" />
            </template>
          </ClientOnly>
        </UCard>
      </div>

      <!-- Table -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <div>
              <p class="text-sm font-medium text-highlighted">
                Per-champion stats
              </p>
              <p class="text-xs text-dimmed mt-0.5">
                <span class="font-medium">Games</span> honor every filter ·
                <span class="font-medium">Mains / OTPs / Ext. samples</span>
                honor region only.
              </p>
            </div>
            <UBadge
              v-if="!loading"
              color="neutral"
              variant="subtle"
              :label="`${formatNumber(rows.length)} champions`"
            />
          </div>
        </template>

        <UTable
          v-model:sorting="sorting"
          :data="rows"
          :columns="columns"
          :loading="loading"
          loading-color="primary"
          sticky
          class="max-h-[640px]"
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
            </div>
          </template>
          <template #otps-cell="{ row }">
            <div class="text-right tabular-nums">
              {{ formatNumber(row.original.otps) }}
            </div>
          </template>
          <template #extendedSamples-cell="{ row }">
            <div class="text-right tabular-nums text-muted">
              {{ formatNumber(row.original.extendedSamples) }}
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
