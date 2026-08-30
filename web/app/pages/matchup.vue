<script setup lang="ts">
import type { CompositionBuildRequest, CompositionSlotInput } from '~~/shared/types/composition'
import { POSITION_OPTIONS, POSITION_BY_VALUE, isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'

useSeoMeta({
  title: 'Matchup',
  description:
    'Pick your champion and your role opponent — the recommended build rebuilds live from real '
    + 'games of that exact matchup, refined by the rest of the draft.',
})

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

const roleLabel = computed(() =>
  playedPosition.value === null
    ? null
    : POSITION_BY_VALUE.get(playedPosition.value)?.label.toLowerCase() ?? null)

// ─── Live recommendation ─────────────────────────────────────────────────────

const { data: recommendation, isLoading, error, submit, clear } = useCompositionBuild()

// The provenance drawer's open state lives here because the two halves of it sit
// in different components: the button is in the stat line above (#1111), the
// drawer itself stays in `RecommendationPanel`, which already fetches the item /
// rune / spell maps it needs at the recommendation's patch.
const gamesDrawerOpen = ref(false)

const isDraftReady = computed(() => playedChampionId.value !== null && playedPosition.value !== null)

function toSlots(
  slots: Record<ChampionPosition, number | null>,
  extra?: { position: ChampionPosition, championId: number } | null,
): CompositionSlotInput[] {
  const picks = POSITION_OPTIONS
    .filter(option => slots[option.value] !== null)
    .map(option => ({ position: option.value as string, championId: slots[option.value] as number }))
  return extra ? [...picks, extra] : picks
}

/**
 * Debounce window between the last draft edit and the refetch. Long enough to
 * swallow a burst of picks, short enough that the page still feels live.
 */
const REFETCH_DEBOUNCE_MS = 400

/**
 * The exact body the recommendation was (or is about to be) fetched with —
 * reused verbatim by the provenance drawer (#940), which re-lists that same
 * selection instead of re-deriving the draft.
 */
const currentDraftRequest = computed<CompositionBuildRequest | null>(() => {
  if (playedChampionId.value === null || playedPosition.value === null) return null
  const position = playedPosition.value
  const opponent = opponentChampionId.value
  return {
    position,
    allies: toSlots(allySlots.value),
    enemies: toSlots(enemySlots.value, opponent === null ? null : { position, championId: opponent }),
  }
})

let refetchTimer: ReturnType<typeof setTimeout> | undefined

// Live mode: every draft edit re-queries after a short debounce — there is no
// submit button. The previous recommendation stays on screen while the next
// one loads (the composable also drops out-of-order responses).
watch(
  [playedChampionId, playedPosition, opponentChampionId, allySlots, enemySlots],
  () => {
    clearTimeout(refetchTimer)
    if (playedChampionId.value === null || playedPosition.value === null) {
      clear()
      return
    }
    const championId = playedChampionId.value
    // Capture the body alongside the champion id: the drawer and the backend's
    // 30 s cache both key on this exact object, so it is composed once and the
    // fetch and the provenance list cannot drift apart.
    const request = currentDraftRequest.value
    if (request === null) return
    refetchTimer = setTimeout(() => {
      void submit(championId, request)
    }, REFETCH_DEBOUNCE_MS)
  },
  { deep: true, immediate: true },
)

onBeforeUnmount(() => clearTimeout(refetchTimer))

function resetContext() {
  allySlots.value = emptySlots()
  enemySlots.value = emptySlots()
}

const hasContextPicks = computed(() =>
  POSITION_OPTIONS.some(option =>
    allySlots.value[option.value] !== null || enemySlots.value[option.value] !== null))

// ─── Graceful degradation ────────────────────────────────────────────────────

/**
 * There is exactly one degraded state left (#1075): the matchup was pinned and
 * **never recorded**, so the API returns zero games rather than relaxing the
 * filter (`CompositionMatchQueryService`: `MatchupFound = !matchupRequested ||
 * selectedMatches.Count > 0`). With no matchup build to render, the champion's
 * standard build is the only thing there is.
 *
 * A *thin* matchup is no longer a degraded state. It used to be hidden below
 * eight games behind an alert and a "Show it anyway" button — which contradicted
 * the rule the champion page has followed since #923 ("show the matchup whatever
 * its volume, with the game count on it, rather than hiding thin sections"), and
 * which mattered far more than it looks: the production measurement behind that
 * decision put the **median** champion x opponent x position pair at 4 games, so
 * the floor was withholding the matchup build for over half of all pairs. The
 * build now always renders, and `RecommendationPanel` qualifies it in place —
 * its own low-sample tooltip fires under 20 games, well above anything this
 * floor caught.
 */
const matchupMissing = computed(() =>
  recommendation.value !== null
  && recommendation.value.matchupRequested
  && !recommendation.value.matchupFound)

function setAlly(position: ChampionPosition, championId: number | null) {
  allySlots.value[position] = championId
}

function setEnemy(position: ChampionPosition, championId: number | null) {
  enemySlots.value[position] = championId
}

/** Tooltip on the standard build's warning icon — the whole explanation, in one line. */
const missingMatchupNotice = computed(() => {
  const champion = playedChampion.value?.name ?? 'this champion'
  const opponent = opponentChampion.value?.name ?? 'that champion'
  const role = roleLabel.value ? ` at ${roleLabel.value}` : ''
  return `No recorded ${champion} vs ${opponent} game${role} — showing ${champion}'s standard build.`
})
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Draft tools"
      title="Matchup"
    />

    <UAlert
      v-if="staticError"
      color="error"
      variant="soft"
      title="Champion list unavailable"
      :description="describeFetchError(staticError)"
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

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      title="Recommendation unavailable"
      :description="describeFetchError(error)"
    />

    <template v-if="recommendation && isDraftReady">
      <!-- Matchup pinned but never recorded: there is no matchup build to
           render, so this falls back to the champion's baseline one. The whole
           explanation rides in the card's warning-icon tooltip — a banner here
           was three lines of prose above a build the reader can already see. -->
      <BuilderFallbackBuild
        v-if="matchupMissing && playedChampionId !== null && playedPosition !== null"
        :champion-id="playedChampionId"
        :position="playedPosition"
        :champion-name="playedChampion?.name ?? null"
        :notice="missingMatchupNotice"
      />

      <SectionCard
        v-else-if="recommendation.build.gamesConsidered === 0"
        :title="playedChampion ? `Recommended for ${playedChampion.name}` : 'Recommendation'"
      >
        <div class="surface rounded-lg px-6 py-12 text-center">
          <p class="font-medium">
            No similar games found
          </p>
          <p class="mt-1 text-sm text-muted">
            Nothing recorded for this champion at this position yet.
          </p>
        </div>
      </SectionCard>

      <!-- Any matchup with at least one game: shown, however thin. The panel
           carries its own low-sample warning icon, so nothing is added here. -->
      <template v-else>
        <div
          class="transition-opacity duration-200"
          :class="isLoading ? 'opacity-60' : ''"
        >
          <BuilderRecommendationPanel
            :recommendation="recommendation"
            :champion-name="playedChampion?.name ?? null"
            :champion-icon-url="playedChampion?.iconUrl ?? null"
            :opponent-name="opponentChampion?.name ?? null"
            :opponent-icon-url="opponentChampion?.iconUrl ?? null"
            :draft-request="currentDraftRequest"
            :champions="champions"
            :games-drawer-open="gamesDrawerOpen"
            @update:games-drawer-open="gamesDrawerOpen = $event"
          />
        </div>
      </template>
    </template>

    <!-- First fetch after the pick: the recommendation panel's own layout with
         nothing resolved in it (see `ChampionBuildCoreSkeleton`), rather than a
         blank page. The header is scaffolded here because the panel owning it
         is what we're waiting for. -->
    <SectionCard v-else-if="isDraftReady && isLoading">
      <template #title>
        <div
          class="flex flex-wrap items-center gap-x-2.5 gap-y-1"
          aria-hidden="true"
        >
          <USkeleton class="size-7 rounded-lg" />
          <USkeleton class="h-4 w-56" />
          <USkeleton class="size-7 rounded-lg" />
        </div>
      </template>
      <ChampionBuildCoreSkeleton />
    </SectionCard>

    <!-- Nothing picked yet. Without this the page is a stage floating over a
         screen of empty background — it reads as broken rather than as waiting
         for input. Dashed, recessed and unlabelled by a heading so it stays a
         placeholder and not a third panel to parse. -->
    <div
      v-if="!isDraftReady"
      class="rounded-xl border border-dashed border-accented bg-muted px-6 py-12 text-center"
    >
      <UIcon
        name="i-lucide-swords"
        class="size-8 text-dimmed"
      />
      <p class="mt-3 font-medium text-highlighted">
        Pick a champion and a role
      </p>
    </div>
  </main>
</template>
