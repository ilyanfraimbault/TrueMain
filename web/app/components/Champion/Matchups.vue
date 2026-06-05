<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'

const props = defineProps<{
  championId: number
  position: ChampionPosition | null
  /** The page's active patch — used when the "This patch" toggle is on. */
  patch: string | null
  champions: ChampionStaticListItem[]
  /** When set, scope the matchups to this player's games. */
  nameTag?: string
}>()

const TOP_N = 5

// Patch toggle: default to the page's current patch, widen to all history on
// demand (more games per matchup → a steadier best/worst ranking).
const scopeToPatch = ref(true)
const effectivePatch = computed(() => (scopeToPatch.value ? props.patch : null))

const selectedOpponentId = ref<number | null>(null)

const { data, status, error } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  { patch: () => effectivePatch.value, nameTag: () => props.nameTag },
)

// Skeleton only on the first load — keep the table on screen while a patch
// switch refetches so toggling This patch / All patches doesn't flash.
const isLoading = computed(() => status.value === 'pending' && !data.value)

// Champion id → static entry for icon + name lookups.
const championById = computed(() => {
  const map = new Map<number, ChampionStaticListItem>()
  for (const c of props.champions) map.set(c.championId, c)
  return map
})

// Exclude the champion itself from the opponent search.
const opponentOptions = computed(() =>
  props.champions.filter(c => c.championId !== props.championId),
)

// The API already returns matchups by win rate descending; this re-sort is a
// defensive copy so the best/worst slicing below never depends on response order.
const sorted = computed<ChampionMatchupEntry[]>(() =>
  [...(data.value?.matchups ?? [])].sort((a, b) => b.winRate - a.winRate),
)
const hasAny = computed(() => sorted.value.length > 0)

const best = computed(() => sorted.value.slice(0, TOP_N))
// Bottom TOP_N, worst (lowest win rate) first, never overlapping `best`: the
// `Math.max` start clamp means that with 6–9 entries `worst` holds only the
// rows past `best` (fewer than TOP_N) rather than re-showing best's rows.
const worst = computed(() =>
  sorted.value.slice(Math.max(TOP_N, sorted.value.length - TOP_N)).reverse(),
)

// Opponent search: the picked champion's row, if it cleared the games floor.
const searched = computed<ChampionMatchupEntry | null>(() =>
  selectedOpponentId.value === null
    ? null
    : sorted.value.find(m => m.opponentChampionId === selectedOpponentId.value) ?? null,
)
const searchedOpponent = computed(() =>
  selectedOpponentId.value === null ? null : championById.value.get(selectedOpponentId.value) ?? null,
)
</script>

<template>
  <section class="flex flex-col gap-3">
    <header class="flex flex-wrap items-center gap-3">
      <div class="flex flex-col gap-0.5">
        <h2 class="text-sm font-semibold">
          Matchups
        </h2>
        <p class="text-xs text-muted">
          Best and worst lane matchups.
        </p>
      </div>
      <div class="ml-auto flex items-center gap-2">
        <ChampionPicker
          :champions="opponentOptions"
          :champion-id="selectedOpponentId"
          placeholder="Search for a champion"
          trigger-class="w-48"
          @update:champion-id="value => (selectedOpponentId = value)"
        />
        <!-- Patch scope toggle: current page patch vs all history. -->
        <div class="flex shrink-0 rounded-md bg-elevated/60 p-0.5 text-xs font-medium">
          <button
            type="button"
            class="rounded px-2 py-1 transition-colors"
            :class="scopeToPatch ? 'bg-primary/15 text-primary' : 'text-muted hover:text-default'"
            @click="scopeToPatch = true"
          >
            This patch
          </button>
          <button
            type="button"
            class="rounded px-2 py-1 transition-colors"
            :class="!scopeToPatch ? 'bg-primary/15 text-primary' : 'text-muted hover:text-default'"
            @click="scopeToPatch = false"
          >
            All patches
          </button>
        </div>
      </div>
    </header>

    <template v-if="isLoading">
      <USkeleton v-for="i in 6" :key="`mu-skel-${i}`" class="h-11 w-full rounded-md" />
    </template>

    <p
      v-else-if="error"
      class="rounded-lg bg-elevated/40 px-4 py-6 text-center text-sm text-muted"
    >
      Couldn't load matchups. Please try again.
    </p>

    <!-- Opponent search: just the picked champion's row (or a games-floor note). -->
    <template v-else-if="selectedOpponentId !== null">
      <ChampionMatchupRow
        v-if="searched"
        :entry="searched"
        :opponent="searchedOpponent"
      />
      <p
        v-else
        class="rounded-lg bg-elevated/40 px-4 py-6 text-center text-sm text-muted"
      >
        Not enough games against {{ searchedOpponent?.name ?? 'this opponent' }} on this lane yet.
      </p>
    </template>

    <p
      v-else-if="!hasAny"
      class="rounded-lg bg-elevated/40 px-4 py-6 text-center text-sm text-muted"
    >
      No matchups with enough games on this lane yet.
    </p>

    <!-- Default: best / worst leaderboard. -->
    <template v-else>
      <div class="flex flex-col gap-1">
        <p class="px-2 text-xs font-semibold uppercase tracking-wide text-emerald-400/80">
          Best matchups
        </p>
        <ChampionMatchupRow
          v-for="m in best"
          :key="`best-${m.opponentChampionId}`"
          :entry="m"
          :opponent="championById.get(m.opponentChampionId) ?? null"
        />
      </div>
      <div v-if="worst.length" class="flex flex-col gap-1">
        <p class="px-2 text-xs font-semibold uppercase tracking-wide text-red-400/80">
          Worst matchups
        </p>
        <ChampionMatchupRow
          v-for="m in worst"
          :key="`worst-${m.opponentChampionId}`"
          :entry="m"
          :opponent="championById.get(m.opponentChampionId) ?? null"
        />
      </div>
    </template>
  </section>
</template>
