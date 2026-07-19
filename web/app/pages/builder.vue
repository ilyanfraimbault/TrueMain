<script setup lang="ts">
import type { CompositionSlotInput } from '~~/shared/types/composition'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'

useSeoMeta({
  title: 'Composition Builder',
  description:
    'Pick your champion and role, sketch both team comps, and watch the build from the most similar real games update live.',
})

const route = useRoute()
const router = useRouter()

// ─── Draft state ─────────────────────────────────────────────────────────────

const initialChampion = Number(route.query.champion)
const playedChampionId = ref<number | null>(
  Number.isInteger(initialChampion) && initialChampion > 0 ? initialChampion : null,
)
const initialPosition = typeof route.query.position === 'string' ? route.query.position : ''
const playedPosition = ref<ChampionPosition | null>(
  isChampionPosition(initialPosition) ? initialPosition : null,
)

function emptySlots(): Record<ChampionPosition, number | null> {
  return { TOP: null, JUNGLE: null, MIDDLE: null, BOTTOM: null, UTILITY: null }
}

const allySlots = ref(emptySlots())
const enemySlots = ref(emptySlots())

// The player's own slot is the picked champion — an ally there would be
// rejected by the API, so the row disappears and any leftover pick is cleared.
watch(playedPosition, (position) => {
  if (position) {
    allySlots.value[position] = null
  }
})

// Deep-link the player pick (champion, position) — the nine team slots stay
// ephemeral, a full draft in the URL would be noise.
watch([playedChampionId, playedPosition], ([champion, position]) => {
  void router.replace({
    query: {
      ...(champion ? { champion: String(champion) } : {}),
      ...(position ? { position } : {}),
    },
  })
})

// ─── Reference data ──────────────────────────────────────────────────────────

const { data: staticList, error: staticError } = useChampionStaticList()
const champions = computed(() => staticList.value ?? [])
const championsById = useChampionsById(champions)

const playedChampion = computed(() =>
  playedChampionId.value === null ? null : championsById.value.get(playedChampionId.value) ?? null)

const laneOpponentName = computed(() => {
  if (playedPosition.value === null) {
    return null
  }
  const id = enemySlots.value[playedPosition.value]
  return id === null ? null : championsById.value.get(id)?.name ?? null
})

// ─── Live recommendation ─────────────────────────────────────────────────────

const { data: recommendation, isLoading, error, submit, clear } = useCompositionBuild()

const isDraftReady = computed(() => playedChampionId.value !== null && playedPosition.value !== null)

function toSlots(slots: Record<ChampionPosition, number | null>): CompositionSlotInput[] {
  return POSITION_OPTIONS
    .filter(option => slots[option.value] !== null)
    .map(option => ({ position: option.value, championId: slots[option.value] as number }))
}

/**
 * Debounce window between the last draft edit and the refetch. Long enough to
 * swallow a burst of picks, short enough that the page still feels live.
 */
const REFETCH_DEBOUNCE_MS = 400

let refetchTimer: ReturnType<typeof setTimeout> | undefined

// Live mode: every draft edit re-queries after a short debounce — there is no
// submit button. The previous recommendation stays on screen while the next
// one loads (the composable also drops out-of-order responses).
watch(
  [playedChampionId, playedPosition, allySlots, enemySlots],
  () => {
    clearTimeout(refetchTimer)
    if (playedChampionId.value === null || playedPosition.value === null) {
      clear()
      return
    }
    const championId = playedChampionId.value
    const position = playedPosition.value
    refetchTimer = setTimeout(() => {
      void submit(championId, {
        position,
        allies: toSlots(allySlots.value),
        enemies: toSlots(enemySlots.value),
      })
    }, REFETCH_DEBOUNCE_MS)
  },
  { deep: true, immediate: true },
)

onBeforeUnmount(() => clearTimeout(refetchTimer))

function resetDraft() {
  allySlots.value = emptySlots()
  enemySlots.value = emptySlots()
}

const hasDraftPicks = computed(() =>
  POSITION_OPTIONS.some(option =>
    allySlots.value[option.value] !== null || enemySlots.value[option.value] !== null))

// Both columns keep the canonical role order so every ally row sits directly
// across from the enemy in the same lane (top vs top, jungle vs jungle, …).

const matchupMissing = computed(() =>
  recommendation.value !== null
  && recommendation.value.matchupRequested
  && !recommendation.value.matchupFound)
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Draft tools"
      title="Composition builder"
      description="Pick your champion and role — the build below updates live as you fill in the draft."
    />

    <UAlert
      v-if="staticError"
      color="error"
      variant="soft"
      title="Champion list unavailable"
      :description="describeFetchError(staticError)"
    />

    <!-- One card: the player pick and both team drafts, all draft configuration
         in a single surface. -->
    <section
      class="glass space-y-5 rounded-xl p-4 sm:p-6"
      aria-label="Draft"
    >
      <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
        <p class="text-xs font-medium uppercase tracking-wider text-muted">
          You are playing
        </p>
        <ChampionPicker
          :champions="champions"
          :champion-id="playedChampionId"
          placeholder="Choose your champion"
          size="lg"
          trigger-class="w-full max-w-64 sm:w-64"
          @update:champion-id="playedChampionId = $event"
        />
        <RolePicker
          :position="playedPosition"
          hide-all
          @update:position="playedPosition = $event"
        />
        <UButton
          v-if="isDraftReady && hasDraftPicks"
          class="ms-auto"
          variant="ghost"
          color="neutral"
          size="xs"
          icon="i-lucide-eraser"
          @click="resetDraft"
        >
          Clear draft
        </UButton>
      </div>

      <div
        v-if="isDraftReady"
        class="grid gap-x-6 gap-y-4 sm:grid-cols-2"
      >
        <div class="space-y-2">
          <h3 class="text-xs font-medium uppercase tracking-wider text-muted">
            Your team
          </h3>
          <ul class="space-y-1.5">
            <li
              v-for="option in POSITION_OPTIONS"
              :key="option.value"
              class="flex items-center gap-3 rounded-lg px-2.5 py-1.5"
              :class="option.value === playedPosition
                ? 'bg-primary/5 ring-1 ring-inset ring-primary/25'
                : 'glass-hover'"
            >
              <SkeletonImage
                :src="option.iconUrl"
                :alt="option.label"
                :width="18"
                :height="18"
                class="size-[18px] shrink-0 opacity-80"
              />
              <!-- The player's own lane is locked: it mirrors the pick above and
                   can only change from the "You are playing" control. -->
              <ChampionPicker
                v-if="option.value === playedPosition"
                :champions="champions"
                :champion-id="playedChampionId"
                size="sm"
                trigger-class="w-full"
                class="flex-1"
                disabled
              />
              <ChampionPicker
                v-else
                :champions="champions"
                :champion-id="allySlots[option.value]"
                placeholder="Any champion"
                size="sm"
                trigger-class="w-full"
                class="flex-1"
                @update:champion-id="allySlots[option.value] = $event"
              />
            </li>
          </ul>
        </div>

        <div class="space-y-2">
          <h3 class="text-xs font-medium uppercase tracking-wider text-muted">
            Enemy team
          </h3>
          <ul class="space-y-1.5">
            <li
              v-for="option in POSITION_OPTIONS"
              :key="option.value"
              class="flex items-center gap-3 rounded-lg px-2.5 py-1.5"
              :class="option.value === playedPosition
                ? 'bg-primary/5 ring-1 ring-inset ring-primary/25'
                : 'glass-hover'"
            >
              <SkeletonImage
                :src="option.iconUrl"
                :alt="option.label"
                :width="18"
                :height="18"
                class="size-[18px] shrink-0 opacity-80"
              />
              <ChampionPicker
                :champions="champions"
                :champion-id="enemySlots[option.value]"
                :placeholder="option.value === playedPosition ? 'Your lane opponent' : 'Any champion'"
                size="sm"
                trigger-class="w-full"
                class="flex-1"
                @update:champion-id="enemySlots[option.value] = $event"
              />
            </li>
          </ul>
        </div>
      </div>

      <p
        v-else
        class="text-sm text-muted"
      >
        Choose your champion and role to sketch both teams — the build updates live below.
      </p>
    </section>

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      title="Recommendation unavailable"
      :description="describeFetchError(error)"
    />

    <template v-if="recommendation && isDraftReady">
      <!-- Matchup requested but never recorded: say so and fall back to the
           champion's baseline build instead of fabricating a draft-specific one. -->
      <template v-if="matchupMissing">
        <UAlert
          color="warning"
          variant="soft"
          icon="i-lucide-search-x"
          title="No game with this matchup"
          :description="`We have no recorded ${playedChampion?.name ?? 'games'} game against ${laneOpponentName ?? 'that champion'} ${playedPosition ? 'at ' + playedPosition.toLowerCase() : ''} — showing the champion's standard build instead.`"
        />
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
        <div class="glass rounded-lg px-6 py-12 text-center">
          <p class="font-medium">
            No similar games found
          </p>
          <p class="mt-1 text-sm text-muted">
            Nothing recorded for this champion at this position yet.
          </p>
        </div>
      </SectionCard>

      <div
        v-else
        class="transition-opacity duration-200"
        :class="isLoading ? 'opacity-60' : ''"
      >
        <BuilderRecommendationPanel
          :recommendation="recommendation"
          :champion-name="playedChampion?.name ?? null"
          :champion-icon-url="playedChampion?.iconUrl ?? null"
        />
      </div>
    </template>

    <!-- First fetch after the pick: a lightweight skeleton instead of a blank page. -->
    <ChampionBuildTabsSkeleton v-else-if="isDraftReady && isLoading" />
  </main>
</template>
