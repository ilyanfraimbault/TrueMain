<script setup lang="ts">
import type { BuildDivergence, DivergenceDimension } from '~~/shared/types/divergence'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'
import { formatPercentage } from '~~/shared/utils/ddragon'

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

const isLoading = computed(() => status.value === 'pending' && !data.value)

/**
 * Per-dimension wording. `noun` names the decision, `verb` completes the
 * coaching sentence ("… you <verb> X"). `ordered` decides whether the icons are
 * chained with chevrons — a starter set is a basket, a build path is a sequence.
 */
const DIMENSION_COPY: Record<DivergenceDimension, { noun: string, verb: string, ordered: boolean }> = {
  starterItems: { noun: 'Starter', verb: 'start', ordered: false },
  boots: { noun: 'Boots', verb: 'build', ordered: false },
  itemPath: { noun: 'Core items', verb: 'build', ordered: true },
  skillOrder: { noun: 'Skill order', verb: 'max', ordered: true },
}

function copyFor(dimension: DivergenceDimension) {
  return DIMENSION_COPY[dimension]
}

/**
 * The coaching line under a row. Built entirely from the payload's own rates —
 * nothing here is estimated or rounded into a claim the API didn't make.
 */
function hintFor(row: BuildDivergence): string {
  const { verb } = copyFor(row.dimension)
  const mainsShare = formatPercentage(row.mains.pickRate)
  const yoursAmongMains = formatPercentage(row.mainsRateOnPlayerChoice)

  if (!row.diverges) {
    return `Same call as the mains — ${mainsShare} of their games ${verb} this too.`
  }

  return `${mainsShare} of mains games ${verb} theirs, and only ${yoursAmongMains} `
    + `${verb} what you do.`
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
            <UBadge
              :color="row.diverges ? 'warning' : 'success'"
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
                 goes for them rather than letting the low share imply a mistake. -->
            <template v-if="row.diverges && row.mainsWinRateOnPlayerChoice !== null">
              They win {{ formatPercentage(row.mainsWinRateOnPlayerChoice) }} of those.
            </template>
          </p>
        </div>
      </template>
    </div>
  </SectionCard>
</template>
