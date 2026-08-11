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
watch([playedChampionId, playedPosition, opponentChampionId], ([champion, position, opponent]) => {
  void router.replace({
    query: {
      ...(champion ? { champion: String(champion) } : {}),
      ...(position ? { position } : {}),
      ...(opponent ? { opponent: String(opponent) } : {}),
    },
  })
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
    const position = playedPosition.value
    const opponent = opponentChampionId.value
    refetchTimer = setTimeout(() => {
      void submit(championId, {
        position,
        allies: toSlots(allySlots.value),
        enemies: toSlots(
          enemySlots.value,
          opponent === null ? null : { position, championId: opponent },
        ),
      })
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
 * Below this many games of the requested matchup, every dimension of the
 * aggregate is one or two games away from flipping. Rather than dressing that
 * up as a matchup read, the page shows the champion's standard build with an
 * explicit notice (the thin build stays one click away).
 */
const MATCHUP_SAMPLE_FLOOR = 8

/** Matchup pinned but never recorded — the API found zero games. */
const matchupMissing = computed(() =>
  recommendation.value !== null
  && recommendation.value.matchupRequested
  && !recommendation.value.matchupFound)

const matchupSampleSize = computed(() => recommendation.value?.build.gamesConsidered ?? 0)

/** Matchup pinned and found, but on too few games to be read as a matchup build. */
const matchupTooThin = computed(() =>
  recommendation.value !== null
  && recommendation.value.matchupRequested
  && recommendation.value.matchupFound
  && matchupSampleSize.value > 0
  && matchupSampleSize.value < MATCHUP_SAMPLE_FLOOR)

/** Opt-in escape hatch from the thin-sample fallback, reset on every matchup change. */
const showThinMatchupBuild = ref(false)
watch([playedChampionId, playedPosition, opponentChampionId], () => {
  showThinMatchupBuild.value = false
})

const showFallbackBuild = computed(() =>
  matchupMissing.value || (matchupTooThin.value && !showThinMatchupBuild.value))

function revealThinMatchupBuild() {
  showThinMatchupBuild.value = true
}

function hideThinMatchupBuild() {
  showThinMatchupBuild.value = false
}

function setAlly(position: ChampionPosition, championId: number | null) {
  allySlots.value[position] = championId
}

function setEnemy(position: ChampionPosition, championId: number | null) {
  enemySlots.value[position] = championId
}

const fallbackNotice = computed(() => {
  const champion = playedChampion.value?.name ?? 'this champion'
  const opponent = opponentChampion.value?.name ?? 'that champion'
  const role = roleLabel.value ? ` at ${roleLabel.value}` : ''
  if (matchupMissing.value) {
    return {
      title: `No recorded ${champion} vs ${opponent} game`,
      description: `Nothing in our data for this matchup${role} — showing `
        + `${champion}'s standard build instead.`,
    }
  }
  const games = matchupSampleSize.value
  return {
    title: `Only ${games} recorded ${champion} vs ${opponent} game${games === 1 ? '' : 's'}`,
    description: `Too thin to derive a matchup build${role} — showing `
      + `${champion}'s standard build instead.`,
  }
})
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Draft tools"
      title="Matchup"
      description="Pick your champion and your role opponent — the build below rebuilds live from real games of that matchup."
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
      :played-champion="playedChampion"
      :played-champion-id="playedChampionId"
      :played-position="playedPosition"
      :opponent-champion="opponentChampion"
      :opponent-champion-id="opponentChampionId"
      @update:played-champion-id="playedChampionId = $event"
      @update:played-position="playedPosition = $event"
      @update:opponent-champion-id="opponentChampionId = $event"
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
      <!-- Matchup pinned but unusable (never recorded, or a handful of games):
           say so and fall back to the champion's baseline build instead of
           fabricating a matchup-specific one. -->
      <template v-if="showFallbackBuild">
        <UAlert
          color="warning"
          variant="soft"
          icon="i-lucide-search-x"
          :title="fallbackNotice.title"
          :description="fallbackNotice.description"
        >
          <template
            v-if="matchupTooThin"
            #actions
          >
            <UButton
              color="neutral"
              variant="outline"
              size="xs"
              @click="revealThinMatchupBuild"
            >
              Show it anyway
            </UButton>
          </template>
        </UAlert>
        <BuilderFallbackBuild
          v-if="playedChampionId !== null && playedPosition !== null"
          :champion-id="playedChampionId"
          :position="playedPosition"
          :champion-name="playedChampion?.name ?? null"
        />
      </template>

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

      <template v-else>
        <!-- Thin matchup build shown on demand: keep the caveat on screen and
             offer the way back to the standard build. -->
        <UAlert
          v-if="matchupTooThin"
          color="warning"
          variant="soft"
          icon="i-lucide-triangle-alert"
          :title="`Built from ${matchupSampleSize} game${matchupSampleSize === 1 ? '' : 's'} of this matchup`"
          description="Single games swing every dimension at this sample size."
        >
          <template #actions>
            <UButton
              color="neutral"
              variant="outline"
              size="xs"
              @click="hideThinMatchupBuild"
            >
              Back to the standard build
            </UButton>
          </template>
        </UAlert>

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
            :opponent-champion-id="opponentChampionId"
            :draft-request="currentDraftRequest"
            :champions="champions"
          />
        </div>
      </template>
    </template>

    <!-- First fetch after the pick: a lightweight skeleton instead of a blank page. -->
    <ChampionBuildTabsSkeleton v-else-if="isDraftReady && isLoading" />

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
      <p class="mx-auto mt-1 max-w-md text-sm text-muted">
        The build appears here, rebuilt from real games of that champion in that role.
        Add a role opponent to narrow it down to the matchup.
      </p>
    </div>
  </main>
</template>
