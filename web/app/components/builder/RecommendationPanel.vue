<script setup lang="ts">
import type { CompositionBuildRequest, CompositionBuildResponse } from '~~/shared/types/composition'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

/**
 * Full composition recommendation (#563): the same core panels the champion page
 * renders (spells, starter, skill order, boots, core path, runes) plus the pruned
 * build tree. Self-contained: mounts only when a recommendation exists, so the
 * static asset fetches (items, rune tree, summoners, champion spells) fire lazily
 * with the right patch instead of on page load.
 *
 * It no longer carries a numbers strip — that merged into `BuilderMatchupStats`
 * above it (#1111). What stays here is the thin-sample warning icon beside the
 * title, which qualifies *this card's* build rather than any single figure.
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
  /** Owned by the page, because the strip that opens it lives there now (#1111). */
  gamesDrawerOpen: boolean
}>()

const emit = defineEmits<{ 'update:gamesDrawerOpen': [boolean] }>()

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

/**
 * The sample strip that used to live here moved out to `BuilderMatchupStats`
 * (#1111), which now carries the recommendation's figures and the matchup's lane
 * figures on one line — the page had grown two `games` numbers and two `win rate`
 * numbers a few centimetres apart, measuring different populations, with nothing
 * on screen saying so.
 *
 * The provenance drawer stays here, because it needs the item / rune / spell maps
 * this component already fetches at the recommendation's patch. The strip only
 * asks for it, through the page.
 */
const gamesDrawerOpen = computed({
  get: () => props.gamesDrawerOpen,
  set: value => emit('update:gamesDrawerOpen', value),
})
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
