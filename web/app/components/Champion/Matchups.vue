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

const TOP_N = 5

const selectedOpponentId = ref<number | null>(null)

// Jungle has no lane opponent — the matchup is the enemy jungler across the map —
// so the copy says "role"/"jungle" there and "lane" for the four lanes.
const isJungle = computed(() => props.position === 'JUNGLE')
const subtitle = computed(() =>
  isJungle.value ? 'Best and worst jungle matchups.' : 'Best and worst lane matchups.',
)
// Suffix for the empty-state notes, matched to the same scope.
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
// search refetches so the rows don't flash out.
const isLoading = computed(() => status.value === 'pending' && !data.value)

// Champion id → static entry for icon + name lookups.
const championById = useChampionsById(() => props.champions)

// Exclude the champion itself from the opponent search.
const opponentOptions = computed(() =>
  props.champions.filter(c => c.championId !== props.championId),
)

const entries = computed<ChampionMatchupEntry[]>(() => data.value?.matchups ?? [])
const hasAny = computed(() => entries.value.length > 0)

// Best five by the *lower* Wilson bound — "at worst, this matchup is this good".
// Ranking the raw win rate instead is what put Sett-on-eleven-games (82%) above
// Ambessa-on-739 (57%) at the top of every jungle champion: on a field of eighty
// opponents the biggest rate is essentially always the smallest sample, so a raw
// sort ranks variance, not matchups. The backend's games floor drops the noise;
// this decides the order of what survives it.
const best = computed(() =>
  [...entries.value]
    .sort((a, b) => b.winRateLowerBound - a.winRateLowerBound)
    .slice(0, TOP_N),
)

// Worst five by the *upper* bound ascending — "at best, this matchup is only this
// good". Deliberately not the mirror of `best`: sorting the lower bound upwards
// would put the thinnest samples at the bottom, which is the same bug pointing
// down. Best's rows are excluded rather than clamped by index, since the two
// sorts are different orders and could otherwise show the same opponent twice.
const worst = computed(() => {
  const bestIds = new Set(best.value.map(m => m.opponentChampionId))
  return [...entries.value]
    .filter(m => !bestIds.has(m.opponentChampionId))
    .sort((a, b) => a.winRateUpperBound - b.winRateUpperBound)
    .slice(0, TOP_N)
})

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
  <SectionCard
    :level="2"
    title="Matchups"
    :subtitle="subtitle"
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
        class="py-6 text-center text-sm text-muted"
      >
        Couldn't load matchups. Please try again.
      </p>

      <!-- Opponent search: just the picked champion's row (or a games-floor note). -->
      <template v-else-if="selectedOpponentId !== null">
        <ChampionMatchupRow
          v-if="searched"
          :entry="searched"
          :opponent="searchedOpponent"
          :to="matchupToolLink(searched.opponentChampionId)"
        />
        <p
          v-else
          class="py-6 text-center text-sm text-muted"
        >
          No recorded games against {{ searchedOpponent?.name ?? 'this opponent' }} {{ scopeSuffix }} yet.
        </p>
      </template>

      <p
        v-else-if="!hasAny"
        class="py-6 text-center text-sm text-muted"
      >
        No matchups with enough games {{ scopeSuffix }} yet.
      </p>

      <!-- Default: best / worst leaderboard. -->
      <template v-else>
        <div class="flex flex-col gap-1">
          <!-- Column captions: two bare percentages side by side are unreadable
               without them, and "lane" vs "game" is exactly the distinction the
               panel exists to make (#919). Mirrors the row's trailing structure —
               same px-2 and two w-12 columns — so the captions sit over their
               values. -->
          <div class="flex items-center gap-3 px-2">
            <p class="flex-1 text-xs font-semibold uppercase tracking-wide text-emerald-400/80">
              Best matchups
            </p>
            <span class="w-12 shrink-0 text-right text-[10px] uppercase tracking-wide text-dimmed">Lane</span>
            <span class="w-12 shrink-0 text-right text-[10px] uppercase tracking-wide text-dimmed">Game</span>
          </div>
          <ChampionMatchupRow
            v-for="m in best"
            :key="`best-${m.opponentChampionId}`"
            :entry="m"
            :opponent="championById.get(m.opponentChampionId) ?? null"
            :to="matchupToolLink(m.opponentChampionId)"
          />
        </div>
        <div v-if="worst.length" class="flex flex-col gap-1">
          <!-- Column captions: two bare percentages side by side are unreadable
               without them, and "lane" vs "game" is exactly the distinction the
               panel exists to make (#919). Mirrors the row's trailing structure —
               same px-2 and two w-12 columns — so the captions sit over their
               values. -->
          <div class="flex items-center gap-3 px-2">
            <p class="flex-1 text-xs font-semibold uppercase tracking-wide text-red-400/80">
              Worst matchups
            </p>
            <span class="w-12 shrink-0 text-right text-[10px] uppercase tracking-wide text-dimmed">Lane</span>
            <span class="w-12 shrink-0 text-right text-[10px] uppercase tracking-wide text-dimmed">Game</span>
          </div>
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
