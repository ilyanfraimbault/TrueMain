<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'

const props = defineProps<{
  championId: number
  position: ChampionPosition | null
  champions: ChampionStaticListItem[]
  /** When set, scope the matchups to this player's games. */
  nameTag?: string
}>()

const TOP_N = 5

const selectedOpponentId = ref<number | null>(null)

const { data, status, error } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  {
    nameTag: () => props.nameTag,
    opponentChampionId: () => selectedOpponentId.value,
  },
)

// Skeleton only on the first load — keep the table on screen while an opponent
// search refetches so the rows don't flash out.
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

// Opponent search: the backend returns just this opponent's head-to-head (one
// entry or none), so the row is that entry when the player has met them.
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
  <SectionCard
    title="Matchups"
    subtitle="Best and worst lane matchups."
  >
    <template #actions>
      <ChampionPicker
        :champions="opponentOptions"
        :champion-id="selectedOpponentId"
        placeholder="Search for a champion"
        trigger-class="w-48"
        @update:champion-id="value => (selectedOpponentId = value)"
      />
    </template>

    <div class="flex flex-col gap-3">
      <template v-if="isLoading">
        <USkeleton v-for="i in 6" :key="`mu-skel-${i}`" class="h-11 w-full rounded-md" />
      </template>

      <p
        v-else-if="error"
        class="px-4 py-6 text-center text-sm text-muted"
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
          class="px-4 py-6 text-center text-sm text-muted"
        >
          No recorded games against {{ searchedOpponent?.name ?? 'this opponent' }} on this lane yet.
        </p>
      </template>

      <p
        v-else-if="!hasAny"
        class="px-4 py-6 text-center text-sm text-muted"
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
    </div>
  </SectionCard>
</template>
