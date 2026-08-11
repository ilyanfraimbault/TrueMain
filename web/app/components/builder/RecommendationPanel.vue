<script setup lang="ts">
import type { CompositionBuildRequest, CompositionBuildResponse } from '~~/shared/types/composition'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { RateBand } from '~/utils/rate-tone'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { winRateBand } from '~/utils/rate-tone'

/**
 * Full composition recommendation (#563): confidence strip + the same core
 * panels the champion page renders (spells, starter, skill order, boots, core
 * path, runes) and the pruned build tree. Self-contained: mounts only when a
 * recommendation exists, so the static asset fetches (items, rune tree,
 * summoners, champion spells) fire lazily with the right patch instead of on
 * page load.
 */
const props = defineProps<{
  recommendation: CompositionBuildResponse
  championName: string | null
  championIconUrl: string | null
  /** Role opponent, when the matchup is pinned — headlines the card (#921). */
  opponentName?: string | null
  opponentIconUrl?: string | null
  /**
   * Same body the recommendation was fetched with — reused verbatim by the
   * provenance drawer (#940) so it lists exactly this recommendation's
   * selection rather than re-deriving the draft from scratch.
   */
  draftRequest: CompositionBuildRequest | null
  champions: ChampionStaticListItem[]
}>()

/**
 * Below this many sampled games the aggregation is thin enough that single
 * games swing every dimension — the panel stays visible but carries an
 * explicit warning instead of fabricated certainty.
 */
const LOW_SAMPLE_FLOOR = 20

/** Mean similarity under this reads as "barely draft-specific". */
const LOW_SIMILARITY_FLOOR = 0.2

const build = computed(() => props.recommendation.build)
const confidence = computed(() => props.recommendation.confidence)

const assetsPatch = computed(() => props.recommendation.patch ?? null)
const { runeTree, itemsMap } = useBuildAssets(assetsPatch)
const { data: summonersMap } = useStaticSummonerSpells(assetsPatch)
const { data: championStatic } = useChampionStatic(
  () => props.recommendation.championId,
  () => assetsPatch.value,
)

const winRate = computed(() =>
  build.value.gamesConsidered > 0 ? build.value.wins / build.value.gamesConsidered : null)

// The matchup is the headline when it is pinned: the card answers "what do I
// build into this opponent", not "what does this champion build".
const headline = computed(() => {
  if (!props.championName) {
    return 'Recommended build'
  }
  return props.opponentName
    ? `Recommended build for ${props.championName} vs ${props.opponentName}`
    : `Recommended build for ${props.championName}`
})

const draftRequested = computed(() => confidence.value.maxPossibleScore > 0)
const lowSample = computed(() => confidence.value.sampleSize < LOW_SAMPLE_FLOOR)
const lowSimilarity = computed(() =>
  draftRequested.value && confidence.value.meanSimilarity < LOW_SIMILARITY_FLOOR)

// A terse warning shown inline next to the title when the sample is thin —
// just the fact, no advisory tail (the numbers strip already qualifies it).
const lowDataMessage = computed(() => {
  if (lowSample.value) {
    return `Only ${confidence.value.sampleSize} similar game${confidence.value.sampleSize === 1 ? '' : 's'}`
  }
  if (lowSimilarity.value) {
    return 'Few games resemble this draft'
  }
  return null
})

const gamesDrawerOpen = ref(false)

/**
 * One cell of the sample strip. Every figure here describes **this
 * recommendation's sample** and nothing else — how many games it rests on, how
 * close they were to the draft, how they ended.
 *
 * The matchup's own record (games, win rate, matchup rate, lane figures) used to
 * sit here too, and moved out to `BuilderMatchupStats` (#1098): it belongs to the
 * matchup rather than to this sample, and mounting it here meant it vanished on
 * the one path where this panel doesn't render — the standard-build fallback.
 */
interface StatCell {
  label: string
  value: string
  caption: string
  hint: string
  tone: RateBand
}

const stats = computed<StatCell[]>(() => [
  {
    label: 'Games used',
    value: String(build.value.gamesConsidered),
    caption: `${confidence.value.truemainGameCount} by mains · of `
      + `${confidence.value.candidatePoolSize.toLocaleString('en-US')} scanned`,
    hint: 'The build below is computed from these games only — games piloted by a '
      + 'main of the champion first, then the most similar to your draft, out of all '
      + 'recent games scanned for this champion and role.',
    tone: 'default',
  },
  {
    label: 'Draft match',
    value: draftRequested.value ? formatPercentage(confidence.value.meanSimilarity) : '—',
    caption: 'avg similarity',
    hint: 'Average similarity between those games and your draft.',
    tone: 'default',
  },
  {
    label: 'Win rate',
    value: winRate.value === null ? '—' : formatPercentage(winRate.value),
    caption: 'across those games',
    hint: 'Win rate across the games the build is computed from.',
    tone: winRateBand(winRate.value),
  },
])
</script>

<template>
  <SectionCard>
    <template #title>
      <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1">
        <SkeletonImage
          v-if="championIconUrl"
          :src="championIconUrl"
          :alt="championName ?? ''"
          :width="28"
          :height="28"
          class="size-7 rounded-lg ring-1 ring-primary/40"
        />
        <h2 class="text-sm font-medium text-default">
          {{ headline }}
        </h2>
        <SkeletonImage
          v-if="opponentIconUrl"
          :src="opponentIconUrl"
          :alt="opponentName ?? ''"
          :width="28"
          :height="28"
          class="size-7 rounded-lg ring-1 ring-accented"
        />
        <!-- Thin-data qualifier: only the icon shows next to the title; the
             message lives in its tooltip so it never crowds the header. -->
        <UTooltip
          v-if="lowDataMessage"
          :text="lowDataMessage"
          :delay-duration="150"
        >
          <UIcon
            name="i-lucide-triangle-alert"
            class="size-4 text-warning"
          />
        </UTooltip>
      </div>
    </template>
    <div class="space-y-6">
      <!-- Sample strip — always first: the numbers qualify everything below.
           Only what describes this sample; the matchup's own record is its own
           strip above the card (#1098). -->
      <div class="grid grid-cols-3 gap-4">
        <div
          v-for="stat in stats"
          :key="stat.label"
          :title="stat.hint"
          class="flex items-start gap-1"
        >
          <StatBlock
            :value="stat.value"
            :label="stat.label"
            :caption="stat.caption"
            :tone="stat.tone"
          />
          <!-- Opens the provenance drawer: only meaningful once there's a
               sample to list, and only for the stat it annotates. A filled
               square button, not the borderless ghost icon it used to be — at
               `ghost` + `:padded="false"` it read as part of the label rather
               than as a control, and nothing said the games behind the number
               could be opened. -->
          <UTooltip
            v-if="stat.label === 'Games used' && build.gamesConsidered > 0"
            text="See the games this build was computed from"
            :delay-duration="150"
          >
            <UButton
              icon="i-lucide-eye"
              color="neutral"
              variant="subtle"
              size="xs"
              square
              aria-label="See the games this build was computed from"
              @click="() => { gamesDrawerOpen = true }"
            />
          </UTooltip>
        </div>
      </div>

      <ChampionBuildPanelCore
        :summoner-spells="build.summonerSpells"
        :starter-items="build.starterItems"
        :skill-order="build.skillOrder"
        :boots="build.boots"
        :item-path="build.corePath"
        :rune-page="build.runePage"
        :champion-static="championStatic ?? null"
        :items-map="itemsMap"
        :summoners-map="summonersMap ?? {}"
        :rune-tree="runeTree"
        no-runes-message="No rune data in the sampled games."
      />

      <!-- Build tree — same component as the champion page, recomputed from the
           sampled games only. -->
      <ChampionBuildPanelBuildTree
        v-if="build.buildTree.length > 0"
        :tree="build.buildTree"
        :first-item-id="build.firstItemId"
        :item-path="build.corePath?.itemIds ?? []"
        :items-map="itemsMap"
      />
    </div>

    <BuilderGamesDrawer
      :open="gamesDrawerOpen"
      :champion-id="recommendation.championId"
      :draft-request="draftRequest"
      :champion-name="championName"
      :champions="champions"
      :items="itemsMap"
      :summoner-spells="summonersMap ?? {}"
      :rune-tree="runeTree"
      @update:open="gamesDrawerOpen = $event"
    />
  </SectionCard>
</template>
