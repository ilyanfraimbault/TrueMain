<script setup lang="ts">
// The matchup page, as the site and the desktop app both render it (#1732):
// each app's route file wraps this with its own head tags and nothing else.
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '#common/utils/positions'

const route = useRoute()
const router = useRouter()

// ─── Draft state ─────────────────────────────────────────────────────────────

function queryChampionId(value: unknown): number | null {
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null
}

const playedChampionId = ref<number | null>(queryChampionId(route.query.champion))
const initialPosition = typeof route.query.position === 'string' ? route.query.position : ''
const playedPosition = ref<ChampionPosition | null>(
  isChampionPosition(initialPosition) ? initialPosition : null,
)
/**
 * The role opponent lives outside the enemy slot map: it is the matchup, the
 * one pick that hard-filters the sampled games, and it is owned by the centre
 * stage. It is folded back into the enemy slots only when the request is built.
 */
const opponentChampionId = ref<number | null>(queryChampionId(route.query.opponent))

function emptySlots(): Record<ChampionPosition, number | null> {
  return { TOP: null, JUNGLE: null, MIDDLE: null, BOTTOM: null, UTILITY: null }
}

const allySlots = ref(emptySlots())
const enemySlots = ref(emptySlots())

// The played role is owned by the matchup stage on both sides — a leftover
// pick there would be a second control for the same slot (and an ally on the
// player's own position is rejected by the API).
watch(playedPosition, (position) => {
  if (position) {
    allySlots.value[position] = null
    enemySlots.value[position] = null
  }
})

// Deep-link the matchup (champion, role, opponent) so a matchup is shareable.
// The eight remaining slots stay ephemeral — a full draft in the URL is noise.
//
// Built from the current query rather than from scratch, the same way
// `useChampionFilters.setFilter` and `useRouteFilterSetter` do: this watcher
// owns exactly three keys and must not evict anything else the URL carries
// (`utm_source`, `ref`, a future `?elo=`) on the first pick.
watch([playedChampionId, playedPosition, opponentChampionId], ([champion, position, opponent]) => {
  const query = { ...route.query }
  if (champion) query.champion = String(champion)
  else delete query.champion
  if (position) query.position = position
  else delete query.position
  if (opponent) query.opponent = String(opponent)
  else delete query.opponent
  void router.replace({ query })
})

// ─── Reference data ──────────────────────────────────────────────────────────

const { data: staticList, error: staticError } = useChampionStaticList()
const champions = computed(() => staticList.value ?? [])
const championsById = useChampionsById(champions)

const playedChampion = computed(() =>
  playedChampionId.value === null ? null : championsById.value.get(playedChampionId.value) ?? null)

const opponentChampion = computed(() =>
  opponentChampionId.value === null ? null : championsById.value.get(opponentChampionId.value) ?? null)

// ─── Live recommendation ─────────────────────────────────────────────────────

const { isLoading, error, staleError, recommendation, shownDraftRequest, retryDraft } = useMatchupRecommendation({
  playedChampionId,
  playedPosition,
  opponentChampionId,
  allySlots,
  enemySlots,
})

// The provenance drawer's open state lives here because the two halves of it sit
// in different components: the button is in the stat line above (#1111), the
// drawer itself stays in `RecommendationPanel`, which already fetches the item /
// rune / spell maps it needs at the recommendation's patch.
const gamesDrawerOpen = ref(false)

// The header's star (#2080): the matchup itself — champion, opponent, position.
const favoritePick = computed(() =>
  playedChampionId.value !== null && opponentChampionId.value !== null && playedPosition.value !== null
    ? {
        kind: 'matchup' as const,
        championId: playedChampionId.value,
        opponentChampionId: opponentChampionId.value,
        position: playedPosition.value,
      }
    : null)
const favoritePickLabel = computed(() =>
  `${playedChampion.value?.name ?? 'this champion'} vs ${opponentChampion.value?.name ?? 'this opponent'}`)

const isDraftReady = computed(() => playedChampionId.value !== null && playedPosition.value !== null)

function resetContext() {
  allySlots.value = emptySlots()
  enemySlots.value = emptySlots()
}

const hasContextPicks = computed(() =>
  POSITION_OPTIONS.some(option =>
    allySlots.value[option.value] !== null || enemySlots.value[option.value] !== null))

function setAlly(position: ChampionPosition, championId: number | null) {
  allySlots.value[position] = championId
}

function setEnemy(position: ChampionPosition, championId: number | null) {
  enemySlots.value[position] = championId
}
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Draft tools"
      title="Matchup"
    >
      <template #actions>
        <FavoriteToggle v-if="favoritePick" :pick="favoritePick" :pick-label="favoritePickLabel" />
      </template>
    </PageHeader>

    <FetchErrorAlert
      :error="staticError"
      title="Failed to load the champion list"
    />

    <BuilderMatchupStage
      :champions="champions"
      :played-champion-id="playedChampionId"
      :played-position="playedPosition"
      :opponent-champion-id="opponentChampionId"
      @update:played-champion-id="playedChampionId = $event"
      @update:played-position="playedPosition = $event"
      @update:opponent-champion-id="opponentChampionId = $event"
    />

    <!-- The page's one line of numbers (#1111): the recommendation's sample and
         the matchup's lane figures together. Mounted on the champion / role /
         opponent triple alone, so it survives the standard-build fallback below
         — exactly the state where a reader needs to know how much (or how
         little) is behind the page. -->
    <BuilderMatchupStats
      v-if="playedChampionId !== null && playedPosition !== null && opponentChampionId !== null"
      :position="playedPosition"
      :champion-name="playedChampion?.name ?? null"
      :opponent-name="opponentChampion?.name ?? null"
      :recommendation="recommendation ?? null"
      :loading="isLoading"
      @show-games="gamesDrawerOpen = true"
    />

    <BuilderTeamContext
      v-if="isDraftReady && playedPosition"
      :champions="champions"
      :played-position="playedPosition"
      :ally-slots="allySlots"
      :enemy-slots="enemySlots"
      :has-picks="hasContextPicks"
      @update:ally="setAlly"
      @update:enemy="setEnemy"
      @clear="resetContext"
    />

    <FetchErrorAlert
      :error="error"
      title="Failed to load the recommendation"
    />

    <StaleContentNotice
      v-if="isDraftReady && !isLoading"
      :error="staleError"
      subject="the previous recommendation"
      :on-retry="retryDraft"
    />

    <BuilderMatchupRecommendation
      v-if="playedChampionId !== null && playedPosition !== null"
      v-model:games-drawer-open="gamesDrawerOpen"
      :loading="isLoading"
      :recommendation="recommendation"
      :played-champion-id="playedChampionId"
      :played-position="playedPosition"
      :played-champion="playedChampion"
      :opponent-champion="opponentChampion"
      :draft-request="shownDraftRequest"
      :champions="champions"
    />

    <!-- Nothing picked yet. Without this the page is a stage floating over a
         screen of empty background — it reads as broken rather than as waiting
         for input. Dashed, recessed and unlabelled by a heading so it stays a
         placeholder and not a third panel to parse. -->
    <UEmpty
      v-if="!isDraftReady"
      variant="naked"
      icon="i-lucide-swords"
      description="Pick a champion and a role"
      class="rounded-xl border border-dashed border-accented bg-muted"
      :ui="{ description: 'font-medium text-highlighted' }"
    />
  </div>
</template>
