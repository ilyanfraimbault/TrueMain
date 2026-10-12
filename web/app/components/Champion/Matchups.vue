<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import type { ChampionPosition } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'
import { rankMatchups } from '~~/shared/utils/matchup-ranking'

const props = defineProps<{
  championId: number
  position: ChampionPosition | null
  champions: ChampionStaticListItem[]
  /** When set, scope the matchups to this player's games. */
  nameTag?: string
  /** Elo filter (exact tier or "X+" threshold); ignored for the player scope. */
  eloBracket?: string
  /**
   * Patch the surrounding page is showing. Not optional in spirit: the aggregate
   * behind this panel keeps patches whose raw matches retention has already
   * dropped, so leaving it unset makes the panel span more history than every
   * other number on the page — which is how it came to read 53 739 games under a
   * header saying 4 603.
   */
  patch?: string | null
}>()

const selectedOpponentId = ref<number | null>(null)

// Jungle has no lane opponent — the matchup is the enemy jungler across the map —
// so the empty-state notes say "in the jungle" there and "on this lane" for the four lanes.
const isJungle = computed(() => props.position === 'JUNGLE')
const scopeSuffix = computed(() => (isJungle.value ? 'in the jungle' : 'on this lane'))

const { data, status, error } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  {
    nameTag: () => props.nameTag,
    opponentChampionId: () => selectedOpponentId.value,
    eloBracket: () => props.eloBracket,
    patch: () => props.patch,
  },
)

// Skeleton only on the first load — keep the table on screen while an opponent
// search refetches so the rows don't flash out. `idle` counts as loading: the
// fetch is client-only, so the server renders this panel before it ever ran, and
// reading that as "no matchups" put a false empty state in the HTML a crawler
// indexes (#1954).
const isLoading = computed(() => isLoadingStatus(status.value) && !data.value)

// Champion id → static entry for icon + name lookups.
const championById = useChampionsById(() => props.champions)

// Exclude the champion itself from the opponent search.
const opponentOptions = computed(() =>
  props.champions.filter(c => c.championId !== props.championId),
)

const entries = computed<ChampionMatchupEntry[]>(() => data.value?.matchups ?? [])
const hasAny = computed(() => entries.value.length > 0)

// Best / worst five on the Wilson bounds — the ranking and its rationale live in
// `rankMatchups`, shared with the server-rendered matchup sentences (#1954).
const ranked = computed(() => rankMatchups(entries.value))
const best = computed(() => ranked.value.best)
const worst = computed(() => ranked.value.worst)

// Opponent search: the backend returns just this opponent's head-to-head (one
// entry or none), so the row is that entry when the player has met them.
const searched = computed<ChampionMatchupEntry | null>(() =>
  selectedOpponentId.value === null
    ? null
    : entries.value.find(m => m.opponentChampionId === selectedOpponentId.value) ?? null,
)
const searchedOpponent = computed(() =>
  selectedOpponentId.value === null ? null : championById.value.get(selectedOpponentId.value) ?? null,
)

// Every row leads to the `/matchup` tool with the whole matchup already pinned —
// the three inputs that page deep-links (#939), so the reader goes from "I lose
// to Nidalee" straight to "here is what to build into her" without re-picking
// two champions and a role. Null while the position is unresolved: the tool
// treats the role as a hard filter and 400s without one, so a link missing it
// would be a link to an error.
function matchupToolLink(opponentChampionId: number): string | undefined {
  if (!props.position) return undefined
  const query = new URLSearchParams({
    champion: String(props.championId),
    position: props.position,
    opponent: String(opponentChampionId),
  })
  return `/matchup?${query.toString()}`
}
</script>

<template>
  <!-- Same tightened padding as the Truemains card above it (`p-3 sm:p-4` is
       the app default): both sit in the champion page's narrow sidebar, where
       the card's own padding was costing more width than the rows it holds. -->
  <SectionCard
    :level="2"
    title="Matchups"
    :ui="{ header: 'p-2 sm:px-2.5 sm:py-2', body: 'p-1.5 sm:p-2' }"
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

    <div class="flex flex-col gap-2">
      <template v-if="isLoading">
        <USkeleton v-for="i in 6" :key="`mu-skel-${i}`" class="h-8 w-full rounded-md" />
      </template>

      <FetchErrorAlert
        v-else-if="error"
        :error="error"
        title="Failed to load the matchups"
        class="my-2"
      />

      <!-- Opponent search: just the picked champion's row (or a games-floor note). -->
      <template v-else-if="selectedOpponentId !== null">
        <div v-if="searched" class="flex flex-col gap-1">
          <ChampionMatchupColumns label="Matchup" />
          <ChampionMatchupRow
            :entry="searched"
            :opponent="searchedOpponent"
            :to="matchupToolLink(searched.opponentChampionId)"
          />
        </div>
        <UEmpty
          v-else
          size="sm"
          icon="i-lucide-swords"
          :description="`No recorded games against ${searchedOpponent?.name ?? 'this opponent'} ${scopeSuffix} yet.`"
        />
      </template>

      <UEmpty
        v-else-if="!hasAny"
        size="sm"
        icon="i-lucide-swords"
        :description="`No matchups with enough games ${scopeSuffix} yet.`"
      />

      <!-- Default: best / worst leaderboard. -->
      <template v-else>
        <div class="flex flex-col gap-1">
          <ChampionMatchupColumns label="Best matchups" label-class="text-data-good" />
          <ChampionMatchupRow
            v-for="m in best"
            :key="`best-${m.opponentChampionId}`"
            :entry="m"
            :opponent="championById.get(m.opponentChampionId) ?? null"
            :to="matchupToolLink(m.opponentChampionId)"
          />
        </div>
        <div v-if="worst.length" class="flex flex-col gap-1">
          <ChampionMatchupColumns label="Worst matchups" label-class="text-data-bad" />
          <ChampionMatchupRow
            v-for="m in worst"
            :key="`worst-${m.opponentChampionId}`"
            :entry="m"
            :opponent="championById.get(m.opponentChampionId) ?? null"
            :to="matchupToolLink(m.opponentChampionId)"
          />
        </div>
      </template>
    </div>
  </SectionCard>
</template>
