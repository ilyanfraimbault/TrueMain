<script setup lang="ts">
import type { BuildDivergence, DivergenceDimension } from '~~/shared/types/divergence'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { isLoadingStatus } from '~/utils/async-data'

const props = defineProps<{
  nameTag: string
  championId: number
  patch?: string | null
  position?: ChampionPosition | null
  itemsMap: Record<number, StaticItemData>
  championStatic: ChampionStaticData | null
}>()

const { data, status, error } = usePlayerBuildDivergence(
  () => props.nameTag,
  () => props.championId,
  {
    patch: () => props.patch,
    position: () => props.position,
  },
)

// `idle` counts as loading: with a lazy client-only fetch the SSR shell and the
// first client tick sit in `idle`, and treating that as "settled" made the card
// flash "No aggregated games on this champion yet" before the request even
// started. Keeping `!data` in the condition still avoids a skeleton flash when a
// patch/position change refetches over data already on screen.
const isLoading = computed(() => isLoadingStatus(status.value) && !data.value)

/**
 * Per-dimension wording. `noun` names the decision for the row heading; `verb`
 * is the predicate the hint sentence hangs on. `ordered` decides whether the
 * icons are chained with chevrons — a starter set is a basket, a build path is a
 * sequence.
 *
 * Each verb has to read correctly after both "Mains …" and "who … it", which is
 * why they are per-dimension phrases rather than one generic word: the mains
 * *open on* a starter, *finish* boots, *follow* a path and *max* a skill.
 */
const DIMENSION_COPY: Record<DivergenceDimension, { noun: string, verb: string, ordered: boolean }> = {
  starterItems: { noun: 'Starter', verb: 'open on', ordered: false },
  boots: { noun: 'Boots', verb: 'finish', ordered: false },
  itemPath: { noun: 'Core items', verb: 'follow', ordered: true },
  skillOrder: { noun: 'Skill order', verb: 'max', ordered: true },
}

function copyFor(dimension: DivergenceDimension) {
  return DIMENSION_COPY[dimension]
}

/**
 * The coaching line under a row. Built entirely from the payload's own rates —
 * nothing here is estimated or rounded into a claim the API didn't make.
 *
 * The subject is always the mains (people, plural), never a percentage: a share
 * of *games* cannot "build" or "max" anything, which is what made the earlier
 * phrasing parse wrong halfway through. Shares stay in prepositional phrases,
 * and "theirs" / "yours" point at the two labelled columns directly above.
 */
function hintFor(row: BuildDivergence): string {
  const { verb } = copyFor(row.dimension)
  const mainsShare = formatPercentage(row.mains.pickRate)

  if (!row.diverges) {
    return `Same as the mains, who ${verb} it in ${mainsShare} of their games.`
  }

  // "only 0.0%" would be a rounding artefact standing in for "nobody" — say so.
  const yours = row.mainsGamesOnPlayerChoice === 0
    ? 'none of them'
    : `only ${formatPercentage(row.mainsRateOnPlayerChoice)}`

  return `Mains ${verb} theirs in ${mainsShare} of their games — yours appears in ${yours}.`
}

const rows = computed(() => data.value?.dimensions ?? [])
const divergingCount = computed(() => rows.value.filter(row => row.diverges).length)

const subtitle = computed(() => {
  const payload = data.value
  if (!payload) return 'How your habits compare to the champion\'s mains.'

  const mains = payload.mainsPlayers
  const games = payload.mainsGames
  return `Your ${payload.playerGames} ${payload.playerGames === 1 ? 'game' : 'games'} on `
    + `${payload.patch} ${payload.position.toLowerCase()} vs ${games} `
    + `${games === 1 ? 'game' : 'games'} from ${mains} other ${mains === 1 ? 'main' : 'mains'}.`
})

/**
 * Why the comparison is withheld, or null when it is shown. The two floors are
 * the backend's own — echoed in the payload so this copy never invents a bar.
 */
const emptyReason = computed(() => {
  const payload = data.value
  if (!payload || payload.dimensions.length > 0) return null

  if (!payload.minSampleMet) {
    const missing = payload.minPlayerGames - payload.playerGames
    return `Only ${payload.playerGames} ${payload.playerGames === 1 ? 'game' : 'games'} on record here. `
      + `${missing} more and we'll show how your build lines up with the mains — under that, `
      + 'the "difference" would just be noise.'
  }

  if (!payload.referenceSampleMet) {
    return `Not enough other mains on this champion and lane yet (${payload.mainsGames} `
      + `${payload.mainsGames === 1 ? 'game' : 'games'}, ${payload.minMainsGames} needed) to say what "the mains" do.`
  }

  return 'No comparable build data on this patch yet.'
})
</script>

<template>
  <SectionCard
    :level="2"
    title="You vs mains"
    :subtitle="subtitle"
  >
    <template
      v-if="rows.length"
      #actions
    >
      <UBadge
        :color="divergingCount > 0 ? 'primary' : 'neutral'"
        variant="soft"
        size="sm"
      >
        {{ divergingCount }} of {{ rows.length }} differ
      </UBadge>
    </template>

    <div class="flex flex-col gap-3">
      <template v-if="isLoading">
        <USkeleton
          v-for="i in 3"
          :key="`div-skel-${i}`"
          class="h-28 w-full rounded-md"
        />
      </template>

      <p
        v-else-if="error"
        class="py-6 text-center text-sm text-muted"
      >
        Couldn't load the comparison. Please try again.
      </p>

      <!-- 404 → we hold no aggregate at all for this player on the champion. -->
      <p
        v-else-if="!data"
        class="py-6 text-center text-sm text-muted"
      >
        No aggregated games on this champion yet, so there's nothing to compare.
      </p>

      <p
        v-else-if="emptyReason"
        class="py-6 text-center text-sm text-muted"
      >
        {{ emptyReason }}
      </p>

      <template v-else>
        <div
          v-for="row in rows"
          :key="row.dimension"
          class="flex flex-col gap-2 rounded-lg border border-default/60 p-3"
        >
          <div class="flex flex-wrap items-center justify-between gap-2">
            <h3 class="text-sm font-medium text-default">
              {{ copyFor(row.dimension).noun }}
            </h3>
            <!-- Brand primary for the row worth reading, neutral for the rest.
                 Deliberately NOT a warning/success pair: a divergence is the
                 interesting row, not the wrong one — the hint below can well
                 read "and they win 52% of those". -->
            <UBadge
              :color="row.diverges ? 'primary' : 'neutral'"
              variant="soft"
              size="sm"
            >
              {{ row.diverges ? 'Differs' : 'Matches' }}
            </UBadge>
          </div>

          <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <ChampionDivergenceChoice
              label="You"
              :choice="row.player"
              share-suffix="of your games"
              :items-map="itemsMap"
              :champion-static="championStatic"
              :ordered="copyFor(row.dimension).ordered"
            />
            <ChampionDivergenceChoice
              label="Mains"
              :choice="row.mains"
              share-suffix="of mains games"
              :items-map="itemsMap"
              :champion-static="championStatic"
              :ordered="copyFor(row.dimension).ordered"
              :highlight="row.diverges"
            />
          </div>

          <p class="text-xs text-muted">
            {{ hintFor(row) }}
            <!-- Rare ≠ bad: when mains do play the player's choice, show how it
                 goes for them rather than letting the low share imply a mistake.
                 Gated on the game count as well as the win rate so this can
                 never contradict the "none of them" wording above — the API
                 nulls the rate at zero games, and this holds even if it didn't. -->
            <template
              v-if="row.diverges && row.mainsGamesOnPlayerChoice > 0
                && row.mainsWinRateOnPlayerChoice !== null"
            >
              They win {{ formatPercentage(row.mainsWinRateOnPlayerChoice) }} of those.
            </template>
          </p>
        </div>
      </template>
    </div>
  </SectionCard>
</template>
