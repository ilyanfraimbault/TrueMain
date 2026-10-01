<script setup lang="ts">
import type { LibraryFilters, LibraryGrouping, RecordingsEmptyReason } from '~/utils/recording-library'
import { EMPTY_FILTERS, countLabel, filterItems, formatBytes, groupItems, libraryItems } from '~/utils/recording-library'

/**
 * Recordings (#1755): the full games and clips on this machine, laid out as
 * the reference (DPM's recordings page) in the site's materials — a header with
 * the space used against the budget, the toolbar, and the cards in groups,
 * four across. Filters and grouping survive a trip to a recap and back.
 * `?game=<id>` narrows the page to one game's clips (the dashboard's "Watch"
 * once that game's full recording is gone).
 */
const { library, settings, status, loadState, load, requestPermission } = useRecordings()
const { nameOf } = useChampionStatics()
const route = useRoute()
const router = useRouter()

const filters = useState<LibraryFilters>('recordings-filters', () => ({ ...EMPTY_FILTERS }))
const grouping = useState<LibraryGrouping>('recordings-grouping', () => 'day')
const settingsOpen = ref(false)

watch(() => route.query.game, (game) => {
  const id = Number(game)
  filters.value = { ...filters.value, gameId: Number.isFinite(id) && id > 0 ? id : null }
}, { immediate: true })

onMounted(() => {
  if (loadState.value === 'idle' || loadState.value === 'error') void load()
})

const all = computed(() => libraryItems(library.value))
const shown = computed(() => filterItems(all.value, filters.value, nameOf))
const groups = computed(() => groupItems(shown.value, grouping.value, nameOf))
const clipCount = computed(() => library.value?.clips.length ?? 0)

const gameFilterLabel = computed(() => {
  const item = all.value.find(entry => entry.gameId === filters.value.gameId)
  if (!item) return 'One game'
  const champion = item.championId ? nameOf(item.championId) : 'Game'
  return item.win === null ? champion : `${champion} · ${item.win ? 'Victory' : 'Defeat'}`
})

function clearFilters() {
  filters.value = { ...EMPTY_FILTERS }
  if (route.query.game) void router.replace({ query: {} })
}

const emptyReason = computed<RecordingsEmptyReason | null>(() => {
  if (shown.value.length) return null
  if (all.value.length) return 'no-match'
  const availability = status.value?.availability
  if (!status.value) return 'unavailable'
  if (availability && availability !== 'ready') return availability
  if (!settings.value?.settings.enabled) return 'off'
  return 'nothing'
})

// Capture that stopped working while there is still something to watch: said above the list.
const notice = computed(() => {
  const availability = status.value?.availability
  if (!all.value.length || !availability || availability === 'ready') return null
  return availability
})
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-5">
    <div class="flex items-center gap-3">
      <UIcon name="i-lucide-clapperboard" class="size-6 text-primary" />
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Recordings</h1>

      <div class="ml-auto flex items-center gap-4 text-xs">
        <span v-if="status?.recordingGameId" class="flex items-center gap-1.5 font-medium text-highlighted">
          <span class="size-1.5 animate-tm-pulse rounded-full bg-red-500" />
          Recording
        </span>
        <span class="flex items-center gap-1.5 text-muted">
          <UIcon name="i-lucide-scissors" class="size-3.5 text-primary" />
          <span class="stat-value text-default">{{ clipCount }}</span> clip{{ clipCount === 1 ? '' : 's' }}
        </span>
        <span v-if="library" class="tabular-nums text-muted" title="Space used by recordings over the disk budget">
          <span class="stat-value text-default">{{ formatBytes(library.usedBytes) }}</span> / {{ formatBytes(library.budgetBytes) }}
        </span>
        <UButton
          icon="i-lucide-settings-2"
          color="neutral"
          variant="ghost"
          size="sm"
          aria-label="Recording settings"
          title="Recording settings"
          :disabled="!settings"
          @click="settingsOpen = true"
        />
      </div>
    </div>

    <UAlert
      v-if="notice"
      color="warning"
      variant="subtle"
      class="shrink-0"
      icon="i-lucide-shield-alert"
      :title="notice === 'permission' ? 'Games are not being recorded: Screen Recording is not allowed' : 'Games are not being recorded'"
      :description="status?.message ?? undefined"
      :actions="notice === 'permission' ? [{ label: 'Allow', color: 'warning', variant: 'outline', onClick: () => void requestPermission() }] : []"
    />

    <template v-if="all.length">
      <RecordingsToolbar v-model:filters="filters" v-model:grouping="grouping" :items="all" :shown="countLabel(shown)" />

      <div v-if="filters.gameId !== null" class="-mt-1">
        <UBadge color="primary" variant="subtle" size="md" class="gap-1.5">
          Recorded from {{ gameFilterLabel }}
          <UButton icon="i-lucide-x" size="xs" variant="link" color="primary" class="p-0" aria-label="Show every game" @click="clearFilters" />
        </UBadge>
      </div>
    </template>

    <div v-if="loadState === 'pending' && !library" class="grid grid-cols-4 gap-3">
      <USkeleton v-for="index in 8" :key="index" class="aspect-[4/3] rounded-lg" />
    </div>

    <RecordingsEmpty
      v-else-if="emptyReason"
      :reason="emptyReason"
      @clear="clearFilters"
      @settings="settingsOpen = true"
    />

    <section v-for="group in groups" v-else :key="group.key" class="flex flex-col gap-2">
      <h2 v-if="group.label" class="flex items-center gap-2 text-sm font-semibold text-highlighted">
        {{ group.label }}
        <span class="font-normal text-dimmed">· {{ countLabel(group.items) }}</span>
      </h2>
      <div class="grid grid-cols-4 gap-3">
        <RecordingsCard v-for="item in group.items" :key="`${item.kind}-${item.id}`" :item="item" />
      </div>
    </section>

    <RecordingsSettings v-model:open="settingsOpen" />
  </div>
</template>
